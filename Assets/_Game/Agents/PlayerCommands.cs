using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Sets the player off along a route from their current node. The trip takes the route's travel
    /// turns; <see cref="TravelSystem"/> moves the player on arrival.
    /// </summary>
    public sealed class TravelCommand : ICommand
    {
        public readonly NodeId Destination;

        public TravelCommand(NodeId destination) => Destination = destination;

        public CommandResult Validate(SimContext ctx)
        {
            var nodes = ctx.World.Get<NodeTable>();
            if (!Destination.IsValid || Destination.Value >= nodes.Count) return CommandResult.Fail("Unknown destination.");
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("Already travelling.");
            return FindRoute(ctx, player.Node) >= 0 ? CommandResult.Success : CommandResult.Fail("No route to destination.");
        }

        // TODO: charge the route's transport cost once the player carries cargo.
        public void Apply(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            player.TravelDestination = Destination.Value;
            player.ArrivalTurn = ctx.Turn + ctx.World.Get<RouteTable>().TravelTurns[FindRoute(ctx, player.Node)];
        }

        int FindRoute(SimContext ctx, int from)
        {
            var routes = ctx.World.Get<RouteTable>();
            int start = routes.OutStart[from];
            for (int k = start; k < start + routes.OutCount[from]; k++)
                if (routes.To[routes.RouteIndex[k]] == Destination.Value) return routes.RouteIndex[k];
            return -1;
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

        public BuyGoodsCommand(GoodId good, int quantity)
        {
            Good = good;
            Quantity = quantity;
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
            player.LocalCoins -= (long)filled * price;
            ctx.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good, filled);
            ctx.World.Get<MarketFlows>().Buy(cell, filled);
            ctx.Events.Publish(new GoodsTraded(new NodeId(player.Node), Good, true, Quantity, filled, price, ctx.Turn));
        }

        int Fill(SimContext ctx, int cell)
        {
            var markets = ctx.World.Get<MarketStore>();
            var load = ctx.World.Get<CargoTable>().LoadOf(CargoHolders.PlayerCart, markets.GoodWeight, markets.GoodSize);
            long fill = System.Math.Min(Quantity, markets.Stock[cell]);
            fill = System.Math.Min(fill, PlayerTrade.CartCapacity(ctx)
                .MaxAdditional(load, markets.GoodWeight[Good.Value], markets.GoodSize[Good.Value]));
            fill = System.Math.Min(fill, ctx.World.Get<PlayerState>().LocalCoins / markets.Price[cell]);
            return (int)System.Math.Max(0, fill);
        }
    }

    /// <summary>Sells goods from the cart at the player's market at its current price, for local coins.</summary>
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
            var error = PlayerTrade.Check(ctx, Good, Quantity, out _);
            if (error != null) return CommandResult.Fail(error);
            return Fill(ctx) > 0 ? CommandResult.Success : CommandResult.Fail("The cart holds none of this good.");
        }

        public void Apply(SimContext ctx)
        {
            PlayerTrade.Check(ctx, Good, Quantity, out int cell);
            int filled = Fill(ctx);
            var player = ctx.World.Get<PlayerState>();
            int price = ctx.World.Get<MarketStore>().Price[cell];
            player.LocalCoins += (long)filled * price;
            ctx.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good, -filled);
            ctx.World.Get<MarketFlows>().Sell(cell, filled);
            ctx.Events.Publish(new GoodsTraded(new NodeId(player.Node), Good, false, Quantity, filled, price, ctx.Turn));
        }

        int Fill(SimContext ctx) => System.Math.Min(Quantity, ctx.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, Good));
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

    /// <summary>Buys an NPC facility. Signals an expansion direction toward its industry.</summary>
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
            ctx.World.Get<PlayerState>().CoinsAt(facilities.Node[Facility.Value]) -= Price;
            facilities.Owner[Facility.Value] = OwnerIds.Player;
            facilities.Mode[Facility.Value] = (byte)ControlMode.Manual;
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
            return ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) >= Cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx) => ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) -= Cost;

        public GuidanceSignal GetGuidance(SimContext ctx) =>
            new GuidanceSignal(Focus, ctx.Services.Get<GameDatabase>().Config.GuidanceWeightFoundCompany, Headquarters);
    }
}
