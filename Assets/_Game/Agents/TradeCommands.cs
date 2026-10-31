using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
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
}
