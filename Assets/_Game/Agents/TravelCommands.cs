using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Sets the player off along a route from their current node, paying the trip's costs up front
    /// (<see cref="TravelCosts"/>), optionally with guards and guild insurance. The cargo must fit the cart,
    /// or the own ship on a water route. One encounter roll happens as the trip starts (DEC-026). The trip
    /// takes the route's travel turns; <see cref="TravelSystem"/> moves the player on arrival.
    /// </summary>
    public sealed class TravelCommand : ICommand
    {
        public readonly NodeId Destination;
        public readonly bool Guards;
        public readonly bool Insurance;

        public TravelCommand(NodeId destination, bool guards = false, bool insurance = false)
        {
            Destination = destination;
            Guards = guards;
            Insurance = insurance;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var nodes = ctx.World.Get<NodeTable>();
            if (!Destination.IsValid || Destination.Value >= nodes.Count) return CommandResult.Fail("Unknown destination.");
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("Already travelling.");
            if (!TryQuote(ctx, out var quote)) return CommandResult.Fail("No route to destination.");
            if (!PlayerTrip.CargoFits(ctx.World, ctx.Services.Get<GameDatabase>(), quote.Route))
                return CommandResult.Fail("The cargo does not fit what carries it on this route.");
            return quote.AffordableWith(player.Coins) ? CommandResult.Success : CommandResult.Fail("Not enough coins for the trip.");
        }

        public void Apply(SimContext ctx)
        {
            TryQuote(ctx, out var quote);
            var player = ctx.World.Get<PlayerState>();
            player.Coins[quote.FeeContinent] -= quote.DepartureCost;
            player.Coins[quote.TollContinent] -= quote.Toll;
            // Fees, passage and guards are wages at the origin; insurance goes to a guild there; the toll to
            // the entered nation (DEC-061).
            var purses = ctx.World.Get<PurseState>();
            purses.PayHouseholds(player.Node, quote.Fee + quote.Passage + quote.Guards);
            int guild = MoneyLedger.InsuranceGuild(ctx.World, player.Node);
            if (guild >= 0) ctx.World.Get<GroupTable>().Cash[guild] += quote.Insurance;
            else purses.PayHouseholds(player.Node, quote.Insurance);
            MoneyLedger.ToState(ctx.World, Destination.Value, quote.Toll);
            // A ship that sails lands with the player and docks at the destination port (DEC-053).
            var ship = ctx.World.Get<PlayerShip>();
            if (quote.Passage == 0 && ((RouteTerrain)ctx.World.Get<RouteTable>().Terrain[quote.Route]).IsWater()) ship.DockedAt = Destination.Value;
            player.TravelDestination = Destination.Value;
            player.ArrivalTurn = ctx.Turn + ctx.World.Get<RouteTable>().TravelTurns[quote.Route];
            ctx.Events.Publish(new TripStarted(Destination, player.ArrivalTurn, quote, ctx.Turn));
            Encounter(ctx, quote);
        }

        // Cash losses take a share of the coins left in the departure purse; cargo losses a share of every
        // good. Insurance refunds part of the loss's value in the departure coin ([Draft]).
        void Encounter(SimContext ctx, TripQuote quote)
        {
            var loss = Encounters.Roll(ctx.Seed, ctx.Turn, 0, 0, quote.EncounterChance);
            if (loss == EncounterLoss.None) return;
            var config = ctx.Services.Get<GameDatabase>().Config;
            var player = ctx.World.Get<PlayerState>();
            long value;
            if (loss == EncounterLoss.Cash)
            {
                value = config.EncounterCashLoss.MulInt(Math.Max(0, player.Coins[quote.FeeContinent]));
                player.Coins[quote.FeeContinent] -= value;
                ctx.World.Get<PurseState>().PayHouseholds(player.Node, value); // [Draft] spent where the trip began
            }
            else
            {
                var lost = new System.Collections.Generic.List<CargoItem>();
                Encounters.TakeCargo(ctx.World.Get<CargoTable>(), CargoHolders.PlayerCart, config.EncounterCargoLoss, lost);
                var lostQuote = new TravelCosts(ctx.World, ctx.Services.Get<GameDatabase>()).Quote(quote.Route, lost);
                value = ctx.World.Get<CurrencyState>().Convert(lostQuote.CargoValue, quote.TollContinent, quote.FeeContinent);
            }
            // The premium is on the cargo: the policy covers no coins, as for merchants.
            long refund = Insurance && loss == EncounterLoss.Cargo ? config.InsuranceRefund.MulInt(value) : 0;
            player.Coins[quote.FeeContinent] += refund;
            // [Draft] The insuring guild pays what its cash covers, the households at the origin the rest.
            MoneyLedger.PayFromGroup(ctx.World, MoneyLedger.InsuranceGuild(ctx.World, player.Node), player.Node, refund);
            ctx.Events.Publish(new TripEncounter(Destination, loss, value, refund, ctx.Turn));
        }

        bool TryQuote(SimContext ctx, out TripQuote quote) =>
            PlayerTrip.TryQuote(ctx.World, ctx.Services.Get<GameDatabase>(), Destination, new TripOptions(Guards, Insurance, false), out quote);
    }

    /// <summary>An encounter on the road took cash or cargo (value in the departure coin) and insurance paid some back.</summary>
    public sealed class TripEncounter : ISimEvent
    {
        public readonly NodeId Destination;
        public readonly EncounterLoss Loss;
        public readonly long LossValue;
        public readonly long Refund;
        public readonly int Turn;

        public TripEncounter(NodeId destination, EncounterLoss loss, long lossValue, long refund, int turn)
        {
            Destination = destination;
            Loss = loss;
            LossValue = lossValue;
            Refund = refund;
            Turn = turn;
        }
    }

    public static class PlayerTrip
    {
        /// <summary>What travelling from the player's node to <paramref name="destination"/> with the cart costs now.</summary>
        public static bool TryQuote(WorldState world, GameDatabase db, NodeId destination, TripOptions options, out TripQuote quote)
        {
            var costs = new TravelCosts(world, db);
            int route = costs.FindRoute(world.Get<PlayerState>().Node, destination.Value);
            if (route < 0)
            {
                quote = default;
                return false;
            }
            var cart = new System.Collections.Generic.List<CargoItem>();
            world.Get<CargoTable>().Collect(CargoHolders.PlayerCart, cart);
            int node = world.Get<PlayerState>().Node;
            bool ship = world.Get<PlayerShip>().AvailableAt(node);
            bool member = GuildBenefits.MemberHere(world, node, world.Get<SimClock>().Turn);
            quote = costs.Quote(route, cart, new TripOptions(options.Guards, options.Insurance, ship, member));
            return true;
        }

        /// <summary>On a water route an own ship carries the load; otherwise (and on passage) the cart's limits apply (DEC-034).</summary>
        public static CargoCapacity Carrier(WorldState world, GameDatabase db, int route)
        {
            var config = db.Config;
            bool ship = ((RouteTerrain)world.Get<RouteTable>().Terrain[route]).IsWater()
                        && world.Get<PlayerShip>().AvailableAt(world.Get<PlayerState>().Node);
            return ship ? new CargoCapacity(config.ShipMaxWeight, config.ShipMaxSize) : new CargoCapacity(config.CartMaxWeight, config.CartMaxSize);
        }

        public static bool CargoFits(WorldState world, GameDatabase db, int route)
        {
            var markets = world.Get<MarketStore>();
            var load = world.Get<CargoTable>().LoadOf(CargoHolders.PlayerCart, markets.GoodWeight, markets.GoodSize);
            var capacity = Carrier(world, db, route);
            return load.Weight <= capacity.MaxWeight && load.Size <= capacity.MaxSize;
        }
    }

    public sealed class TripStarted : ISimEvent
    {
        public readonly NodeId Destination;
        public readonly int ArrivalTurn;
        public readonly TripQuote Paid;
        public readonly int Turn;

        public TripStarted(NodeId destination, int arrivalTurn, TripQuote paid, int turn)
        {
            Destination = destination;
            ArrivalTurn = arrivalTurn;
            Paid = paid;
            Turn = turn;
        }
    }

    public sealed class PlayerArrived : ISimEvent
    {
        public readonly NodeId Node;
        public readonly int Turn;

        public PlayerArrived(NodeId node, int turn)
        {
            Node = node;
            Turn = turn;
        }
    }

    /// <summary>
    /// Completes trips: on the arrival turn the player moves to the destination. Runs right after commands,
    /// so LOD and every later phase of that turn already see the player at the new node.
    /// </summary>
    public sealed class TravelSystem : ISimSystem
    {
        PlayerState _player;

        public string Id => "agents.travel";
        public SimPhase Phase => SimPhase.Commands;
        public int Order => SimOrder.Late;

        public void Initialize(SimContext ctx) => _player = ctx.World.Get<PlayerState>();

        public void Execute(SimContext ctx)
        {
            if (!_player.IsTravelling || ctx.Turn < _player.ArrivalTurn) return;
            _player.Node = _player.TravelDestination;
            _player.TravelDestination = -1;
            _player.ArrivalTurn = -1;
            ctx.Events.Publish(new PlayerArrived(new NodeId(_player.Node), ctx.Turn));
        }
    }
}
