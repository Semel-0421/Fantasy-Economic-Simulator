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
            long refund = Insurance ? config.InsuranceRefund.MulInt(value) : 0;
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

    /// <summary>A buy or sell the player made, with how much of the order was filled.</summary>
    public sealed class GoodsTraded : ISimEvent
    {
        public readonly NodeId Market;
        public readonly GoodId Good;
        public readonly bool Bought;
        public readonly int Requested;
        public readonly int Filled;
        public readonly int Price;
        public readonly int Turn;

        public GoodsTraded(NodeId market, GoodId good, bool bought, int requested, int filled, int price, int turn)
        {
            Market = market;
            Good = good;
            Bought = bought;
            Requested = requested;
            Filled = filled;
            Price = price;
            Turn = turn;
        }
    }

    /// <summary>
    /// Buys goods at the player's market at its current price, paid in the local coin, into the cart.
    /// An order fills up to the smallest of the stock, the cart's room and what the coins pay for (DEC-020).
    /// </summary>
    public sealed class BuyGoodsCommand : ICommand
    {
        public readonly GoodId Good;
        public readonly int Quantity;
        /// <summary>At a bank branch, what the coins carried do not cover comes out of the deposit there (DEC-036).</summary>
        public readonly bool PayFromDeposit;

        public BuyGoodsCommand(GoodId good, int quantity, bool payFromDeposit = false)
        {
            Good = good;
            Quantity = quantity;
            PayFromDeposit = payFromDeposit;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerTrade.Check(ctx, Good, Quantity, out int cell);
            if (error != null) return CommandResult.Fail(error);
            return Fill(ctx, cell) > 0 ? CommandResult.Success : CommandResult.Fail("Nothing can be bought: no stock, room or cash.");
        }

        public void Apply(SimContext ctx)
        {
            PlayerTrade.Check(ctx, Good, Quantity, out int cell);
            int filled = Fill(ctx, cell);
            var player = ctx.World.Get<PlayerState>();
            int price = ctx.World.Get<MarketStore>().Price[cell];
            long cost = (long)filled * price;
            long fromCoins = Math.Min(cost, player.LocalCoins);
            player.LocalCoins -= fromCoins;
            if (cost > fromCoins)
            {
                var bank = ctx.World.Get<BankState>();
                int nation = ctx.World.Get<NodeBanks>().BranchNation(player.Node);
                bank.PlayerDeposit[nation] -= cost - fromCoins;
                bank.Reserve[nation] -= cost - fromCoins;
            }
            ctx.World.Get<PurseState>().ChangeFloat(player.Node, cost);
            ctx.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good, filled);
            ctx.World.Get<MarketFlows>().Buy(cell, filled);
            ctx.Events.Publish(new GoodsTraded(new NodeId(player.Node), Good, true, Quantity, filled, price, ctx.Turn));
        }

        int Fill(SimContext ctx, int cell)
        {
            var markets = ctx.World.Get<MarketStore>();
            var load = ctx.World.Get<CargoTable>().LoadOf(CargoHolders.PlayerCart, markets.GoodWeight, markets.GoodSize);
            long fill = Math.Min(Quantity, markets.Stock[cell]);
            fill = Math.Min(fill, PlayerTrade.CartCapacity(ctx)
                .MaxAdditional(load, markets.GoodWeight[Good.Value], markets.GoodSize[Good.Value]));
            fill = Math.Min(fill, Funds(ctx) / markets.Price[cell]);
            return (int)Math.Max(0, fill);
        }

        long Funds(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            long funds = player.LocalCoins;
            int nation = PayFromDeposit ? ctx.World.Get<NodeBanks>().BranchNation(player.Node) : -1;
            if (nation < 0) return funds;
            var bank = ctx.World.Get<BankState>();
            return funds + Math.Min(bank.PlayerDeposit[nation], bank.Reserve[nation]);
        }
    }

    /// <summary>
    /// Sells goods from the cart at the player's market at its current price, for local coins, as many as the
    /// traders there can pay for (DEC-061).
    /// </summary>
    public sealed class SellGoodsCommand : ICommand
    {
        public readonly GoodId Good;
        public readonly int Quantity;

        public SellGoodsCommand(GoodId good, int quantity)
        {
            Good = good;
            Quantity = quantity;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerTrade.Check(ctx, Good, Quantity, out int cell);
            if (error != null) return CommandResult.Fail(error);
            if (!LicenseRules.PlayerMaySell(ctx.World, ctx.Services.Get<GameDatabase>(), Good, ctx.World.Get<PlayerState>().Node, ctx.Turn))
                return CommandResult.Fail("Selling this good here needs this nation's license.");
            if (ctx.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, Good) <= 0) return CommandResult.Fail("The cart holds none of this good.");
            return Fill(ctx, cell) > 0 ? CommandResult.Success : CommandResult.Fail("The traders here have no money to pay.");
        }

        public void Apply(SimContext ctx)
        {
            PlayerTrade.Check(ctx, Good, Quantity, out int cell);
            int filled = Fill(ctx, cell);
            var player = ctx.World.Get<PlayerState>();
            int price = ctx.World.Get<MarketStore>().Price[cell];
            player.LocalCoins += (long)filled * price;
            ctx.World.Get<PurseState>().ChangeFloat(player.Node, -(long)filled * price);
            ctx.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good, -filled);
            ctx.World.Get<MarketFlows>().Sell(cell, filled);
            ctx.Events.Publish(new GoodsTraded(new NodeId(player.Node), Good, false, Quantity, filled, price, ctx.Turn));
        }

        int Fill(SimContext ctx, int cell) => ctx.World.Get<PurseState>().Sellable(ctx.World.Get<PlayerState>().Node,
            Math.Min(Quantity, ctx.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, Good)), ctx.World.Get<MarketStore>().Price[cell]);
    }

    static class PlayerTrade
    {
        /// <summary>Checks shared by buying and selling; returns an error or null, and the market cell.</summary>
        public static string Check(SimContext ctx, GoodId good, int quantity, out int cell)
        {
            cell = -1;
            if (quantity <= 0) return "Quantity must be positive.";
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return "Cannot trade while travelling.";
            if (!good.IsValid || good.Value >= ctx.Services.Get<GameDatabase>().GoodCount) return "Unknown good.";
            return ctx.World.Get<MarketStore>().TryFindCell(player.Node, good, out cell) ? null : "This market does not trade that good.";
        }

        public static CargoCapacity CartCapacity(SimContext ctx)
        {
            var config = ctx.Services.Get<GameDatabase>().Config;
            return new CargoCapacity(config.CartMaxWeight, config.CartMaxSize);
        }
    }

    /// <summary>Goods moved between the cart and a facility's warehouse.</summary>
    public sealed class CargoMoved : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly GoodId Good;
        public readonly bool ToWarehouse;
        public readonly int Requested;
        public readonly int Moved;

        public CargoMoved(FacilityId facility, GoodId good, bool toWarehouse, int requested, int moved)
        {
            Facility = facility;
            Good = good;
            ToWarehouse = toWarehouse;
            Requested = requested;
            Moved = moved;
        }
    }

    /// <summary>
    /// Moves goods between the cart and the warehouse of a player-owned facility at the player's node.
    /// Fills up to what the source holds and the destination has room for.
    /// </summary>
    public sealed class MoveCargoCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly GoodId Good;
        public readonly int Quantity;
        public readonly bool ToWarehouse;

        public MoveCargoCommand(FacilityId facility, GoodId good, int quantity, bool toWarehouse)
        {
            Facility = facility;
            Good = good;
            Quantity = quantity;
            ToWarehouse = toWarehouse;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Quantity <= 0) return CommandResult.Fail("Quantity must be positive.");
            if (!Good.IsValid || Good.Value >= ctx.Services.Get<GameDatabase>().GoodCount) return CommandResult.Fail("Unknown good.");
            // Goods can still be taken out of a lost facility's warehouse during the claim period (DEC-052).
            var error = PlayerSite.Check(ctx, Facility, allowLostWarehouse: !ToWarehouse);
            if (error != null) return CommandResult.Fail(error);
            return Fill(ctx) > 0 ? CommandResult.Success : CommandResult.Fail("Nothing to move, or no room.");
        }

        public void Apply(SimContext ctx)
        {
            int moved = Fill(ctx);
            var cargo = ctx.World.Get<CargoTable>();
            int warehouse = CargoHolders.Warehouse(Facility);
            cargo.Change(ToWarehouse ? CargoHolders.PlayerCart : warehouse, Good, -moved);
            cargo.Change(ToWarehouse ? warehouse : CargoHolders.PlayerCart, Good, moved);
            ctx.Events.Publish(new CargoMoved(Facility, Good, ToWarehouse, Quantity, moved));
        }

        int Fill(SimContext ctx)
        {
            var cargo = ctx.World.Get<CargoTable>();
            var markets = ctx.World.Get<MarketStore>();
            var config = ctx.Services.Get<GameDatabase>().Config;
            int warehouse = CargoHolders.Warehouse(Facility);
            int to = ToWarehouse ? warehouse : CargoHolders.PlayerCart;
            var capacity = ToWarehouse
                ? new CargoCapacity(config.WarehouseMaxWeight, config.WarehouseMaxSize)
                : new CargoCapacity(config.CartMaxWeight, config.CartMaxSize);
            int held = cargo.QuantityOf(ToWarehouse ? CargoHolders.PlayerCart : warehouse, Good);
            int room = capacity.MaxAdditional(cargo.LoadOf(to, markets.GoodWeight, markets.GoodSize),
                markets.GoodWeight[Good.Value], markets.GoodSize[Good.Value]);
            return Math.Min(Quantity, Math.Min(held, room));
        }
    }

    /// <summary>
    /// Pays coins into (positive amount) or takes them out of (negative) a player-owned facility at the
    /// player's node. Facilities pay for their inputs from this cash.
    /// </summary>
    public sealed class MoveFacilityCashCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly long Amount;

        public MoveFacilityCashCommand(FacilityId facility, long amount)
        {
            Facility = facility;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount == 0 || Amount == long.MinValue) return CommandResult.Fail("Amount must not be zero.");
            var error = PlayerSite.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            if (Amount > 0 && ctx.World.Get<PlayerState>().LocalCoins < Amount) return CommandResult.Fail("Not enough cash.");
            if (Amount < 0 && ctx.World.Get<FacilityTable>().Cash[Facility.Value] < -Amount) return CommandResult.Fail("The facility does not hold that much.");
            return CommandResult.Success;
        }

        public void Apply(SimContext ctx)
        {
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            ctx.World.Get<FacilityTable>().Cash[Facility.Value] += Amount;
        }
    }

    static class PlayerSite
    {
        /// <summary>
        /// The facility exists, the player owns it (or, with <paramref name="allowLostWarehouse"/>, still has a
        /// claim on its warehouse) and stands at its node.
        /// </summary>
        public static string Check(SimContext ctx, FacilityId facility, bool allowLostWarehouse = false)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!facility.IsValid || facility.Value >= facilities.Count) return "Unknown facility.";
            bool claim = allowLostWarehouse && facilities.WarehouseClaimUntil[facility.Value] >= 0;
            if (facilities.Owner[facility.Value] != OwnerIds.Player && !claim) return "Facility is not owned by the player.";
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling || player.Node != facilities.Node[facility.Value]) return "The player is not at the facility.";
            return null;
        }
    }

    /// <summary>Switches a player-owned facility between direct management and automation.</summary>
    public sealed class SetFacilityControlCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly ControlMode Mode;

        public SetFacilityControlCommand(FacilityId facility, ControlMode mode)
        {
            Facility = facility;
            Mode = mode;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!Facility.IsValid || Facility.Value >= facilities.Count) return CommandResult.Fail("Unknown facility.");
            return facilities.Owner[Facility.Value] == OwnerIds.Player
                ? CommandResult.Success
                : CommandResult.Fail("Facility is not owned by the player.");
        }

        public void Apply(SimContext ctx) => ctx.World.Get<FacilityTable>().Mode[Facility.Value] = (byte)Mode;
    }

    /// <summary>Buys an NPC facility, with the cash it holds. Signals an expansion direction toward its industry.</summary>
    public sealed class BuyFacilityCommand : ICommand, IGuidanceSource
    {
        public readonly FacilityId Facility;
        public readonly long Price;
        public readonly GuidanceAxis Axis;

        public BuyFacilityCommand(FacilityId facility, long price, GuidanceAxis axis)
        {
            Facility = facility;
            Price = price;
            Axis = axis;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!Facility.IsValid || Facility.Value >= facilities.Count) return CommandResult.Fail("Unknown facility.");
            if (Price < 0) return CommandResult.Fail("Price must not be negative.");
            if (facilities.Owner[Facility.Value] != OwnerIds.Npc) return CommandResult.Fail("Facility is not for sale.");
            if (ctx.World.Get<PlayerState>().CoinsAt(facilities.Node[Facility.Value]) < Price) return CommandResult.Fail("Not enough cash.");
            return CommandResult.Success;
        }

        public void Apply(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            int node = facilities.Node[Facility.Value];
            ctx.World.Get<PlayerState>().CoinsAt(node) -= Price;
            // [Draft] The seller repays its bank loan from the price; the bank writes off the rest. The seller's
            // group gets what is left, or the purse there if it has none (DEC-061).
            long proceeds = Price;
            var loans = ctx.World.Get<NpcLoanTable>();
            int loan = loans.Find(NpcBorrowers.Facility(Facility.Value));
            if (loan >= 0)
            {
                long repaid = Math.Min(Price, loans.Balance[loan]);
                ctx.World.Get<BankState>().Reserve[loans.Nation[loan]] += repaid;
                proceeds -= repaid;
                loans.RemoveRow(loan);
            }
            int seller = facilities.Group[Facility.Value];
            if (seller >= 0) ctx.World.Get<GroupTable>().Cash[seller] += proceeds;
            else ctx.World.Get<PurseState>().PayHouseholds(node, proceeds);
            facilities.Owner[Facility.Value] = OwnerIds.Player;
            facilities.Mode[Facility.Value] = (byte)ControlMode.Manual;
            facilities.WarehouseClaimUntil[Facility.Value] = -1; // a lost warehouse bought back is the player's again
            facilities.Group[Facility.Value] = ctx.World.Get<GroupTable>().PlayerCompany();
        }

        public GuidanceSignal GetGuidance(SimContext ctx) => new GuidanceSignal(
            Axis,
            ctx.Services.Get<GameDatabase>().Config.GuidanceWeightBuyFacility,
            new NodeId(ctx.World.Get<FacilityTable>().Node[Facility.Value]));
    }

    /// <summary>Founds the player's company. Fixes the long-term direction of the business without gating anything.</summary>
    public sealed class FoundCompanyCommand : ICommand, IGuidanceSource
    {
        public readonly NodeId Headquarters;
        public readonly GuidanceAxis Focus;
        public readonly long Cost;

        public FoundCompanyCommand(NodeId headquarters, GuidanceAxis focus, long cost)
        {
            Headquarters = headquarters;
            Focus = focus;
            Cost = cost;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var nodes = ctx.World.Get<NodeTable>();
            if (!Headquarters.IsValid || Headquarters.Value >= nodes.Count) return CommandResult.Fail("Unknown headquarters node.");
            if (Cost < 0) return CommandResult.Fail("Cost must not be negative.");
            if (ctx.World.Get<GroupTable>().PlayerCompany() >= 0) return CommandResult.Fail("The company is already founded.");
            return ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) >= Cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        /// <summary>The player's company becomes an interest group and owns the player's facilities (DEC-056).</summary>
        public void Apply(SimContext ctx)
        {
            ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) -= Cost;
            // The founding money is the company's starting capital (DEC-061).
            int company = ctx.World.Get<GroupTable>().Add(GroupKind.Company, Headquarters.Value, -1, Cost, player: true);
            var facilities = ctx.World.Get<FacilityTable>();
            for (int f = 0; f < facilities.Count; f++)
                if (facilities.Owner[f] == OwnerIds.Player) facilities.Group[f] = company;
        }

        public GuidanceSignal GetGuidance(SimContext ctx) =>
            new GuidanceSignal(Focus, ctx.Services.Get<GameDatabase>().Config.GuidanceWeightFoundCompany, Headquarters);
    }
}
