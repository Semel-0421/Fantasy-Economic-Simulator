using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// DEC-128: each node has plots. A facility stands on one; the player may hold some; the node's households hold the
    /// rest and sell them. [Draft] A plot's price follows how few are free: a value by the node's level, times all the
    /// plots over those still for sale. Rent comes later.
    /// </summary>
    public sealed class LandState : IPersistentComponent
    {
        public readonly int[] Plots, Used, PlayerPlots;
        readonly NodeTable _nodes;
        readonly AssetMarkets _markets;
        readonly HouseholdState _households;

        public string Key => "agents.land";
        public int Version => 1;

        public LandState(NodeTable nodes, SimulationConfig config, FacilityTable facilities, AssetMarkets markets, HouseholdState households)
        {
            _nodes = nodes;
            _markets = markets;
            _households = households;
            Plots = new int[nodes.Count];
            Used = new int[nodes.Count];
            PlayerPlots = new int[nodes.Count];
            for (int f = 0; f < facilities.Count; f++) Used[facilities.Node[f]]++;
            for (int n = 0; n < nodes.Count; n++) Plots[n] = Math.Max(config.LandPlots[nodes.Level[n]], Used[n]);
        }

        /// <summary>Plots the node's households hold, for sale.</summary>
        public int Free(int n) => Math.Max(0, Plots[n] - Used[n] - PlayerPlots[n]);

        /// <summary>The fundamental price: by scarcity, and some years of the plot's rent (DEC-134).</summary>
        public long Fundamental(int n, SimulationConfig config) =>
            config.LandValue[_nodes.Level[n]] * Plots[n] / Math.Max(1, Free(n)) + config.LandRentYears * YearRent(n, config);

        /// <summary>
        /// DEC-134: [Draft] a plot not under a facility, under houses, earns a share of the node's households' income, split
        /// over those plots, so rent per plot rises as facilities take land and as the town earns more.
        /// </summary>
        public long YearRent(int n, SimulationConfig config)
        {
            long weekly = Fx.MulIntRaw(_households.Population[n], _households.IncomePerHead[n]);
            return weekly * SimClock.WeeksPerYear / 1_000_000 * config.LandRentPpm / Math.Max(1, Plots[n] - Used[n]);
        }

        /// <summary>The market price: the fundamental times the premium of the node's property market (DEC-131).</summary>
        public long Price(int n, SimulationConfig config) => Math.Max(1, _markets.PropertyPremium(n).MulInt(Fundamental(n, config)));

        /// <summary>A facility takes a plot from the households, who are paid its price; false when none is free.</summary>
        public bool TryBuild(WorldState world, SimulationConfig config, int n, out long price)
        {
            price = 0;
            if (Free(n) <= 0) return false;
            price = Price(n, config);
            Used[n]++;
            return true;
        }

        /// <summary>The bank sells the player's plots at the node to its households, one at a time at the price, as far as they can pay (DEC-130). Returns the coins raised.</summary>
        public long SellForPlayer(WorldState world, SimulationConfig config, int n, int count)
        {
            long raised = 0;
            var cash = world.Get<HouseholdCash>().Cash;
            int households = _nodes.Absorbed ? _nodes.Core[n] : n;
            for (; count > 0 && PlayerPlots[n] > 0; count--)
            {
                long price = Price(n, config);
                if (cash[households] < price) break;
                world.Get<PurseState>().PayHouseholds(n, -price);
                PlayerPlots[n]--;
                raised += price;
            }
            return raised;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Plots.Length);
            for (int n = 0; n < Plots.Length; n++)
            {
                writer.Write(Plots[n]);
                writer.Write(Used[n]);
                writer.Write(PlayerPlots[n]);
            }
        }

        public void Read(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count != Plots.Length) throw new InvalidDataException($"agents.land: {count} nodes, the map has {Plots.Length}.");
            for (int n = 0; n < count; n++)
            {
                Plots[n] = reader.ReadInt32();
                Used[n] = reader.ReadInt32();
                PlayerPlots[n] = reader.ReadInt32();
            }
        }
    }

    /// <summary>The player buys a plot from the households where they stand (DEC-128).</summary>
    public sealed class BuyPlotCommand : ICommand
    {
        public CommandResult Validate(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("The player is travelling.");
            var land = ctx.World.Get<LandState>();
            if (land.Free(player.Node) <= 0) return CommandResult.Fail("No land is for sale here.");
            long price = land.Price(player.Node, ctx.Services.Get<GameDatabase>().Config);
            return player.CoinsAt(player.Node) >= price ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            var land = ctx.World.Get<LandState>();
            int n = player.Node;
            long price = land.Price(n, ctx.Services.Get<GameDatabase>().Config);
            player.CoinsAt(n) -= price;
            ctx.World.Get<PurseState>().PayHouseholds(n, price);
            land.PlayerPlots[n]++;
        }
    }

    /// <summary>The player sells a plot back to the households where they stand, as far as they can pay (DEC-128).</summary>
    public sealed class SellPlotCommand : ICommand
    {
        public CommandResult Validate(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("The player is travelling.");
            var land = ctx.World.Get<LandState>();
            if (land.PlayerPlots[player.Node] <= 0) return CommandResult.Fail("The player holds no land here.");
            long price = land.Price(player.Node, ctx.Services.Get<GameDatabase>().Config);
            return ctx.World.Get<HouseholdCash>().Cash[HouseholdsOf(ctx, player.Node)] >= price ? CommandResult.Success : CommandResult.Fail("No one here can pay for it.");
        }

        public void Apply(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            var land = ctx.World.Get<LandState>();
            int n = player.Node;
            long price = land.Price(n, ctx.Services.Get<GameDatabase>().Config);
            ctx.World.Get<PurseState>().PayHouseholds(n, -price);
            player.CoinsAt(n) += price;
            land.PlayerPlots[n]--;
        }

        static int HouseholdsOf(SimContext ctx, int node)
        {
            var nodes = ctx.World.Get<NodeTable>();
            return nodes.Absorbed ? nodes.Core[node] : node;
        }
    }
}
