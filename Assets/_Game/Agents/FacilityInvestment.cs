using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>Where a facility type may stand: a continent's own goods are made nowhere else (DEC-096).</summary>
    public static class FacilityPlaces
    {
        public static bool Allowed(FacilityDef def, NodeTable nodes, int node) =>
            def.Continent == null || def.Continent == nodes.KeyOf(nodes.AncestorAt(node, WorldLevel.Continent));
    }

    public sealed class FacilityExpanded : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly int Level;

        public FacilityExpanded(FacilityId facility, int level)
        {
            Facility = facility;
            Level = level;
        }
    }

    public sealed class FacilityFounded : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly int Group;

        public FacilityFounded(FacilityId facility, int group)
        {
            Facility = facility;
            Group = group;
        }
    }

    /// <summary>
    /// Paid expansion (DEC-080): workshops and factories grow by level. Upkeep and inputs follow the output, and upkeep
    /// grows with the level alone, so each level makes much more for little more upkeep. Primary producers are bound by
    /// their land and do not expand. The price goes to the builders, the households of the node.
    /// </summary>
    public static class FacilityExpansion
    {
        public static bool Possible(FacilityDef def, int level, SimulationConfig config) =>
            def.Tier != IndustryTier.Primary && level < config.FacilityMaxLevel;

        public static long Cost(FacilityDef def, int level, SimulationConfig config) =>
            config.FacilityValue[(int)def.Tier] * level;

        /// <summary>Output at a level, as a multiple of level one (as <see cref="ProductionJob"/> makes it).</summary>
        public static Fx OutputScale(int level, SimulationConfig config) =>
            Fx.FromInt(level) * (Fx.One + config.FacilityExpansionBonus * Fx.FromInt(level - 1));
    }

    /// <summary>Pays for one more level of a player-owned workshop or factory at the player's node.</summary>
    public sealed class ExpandFacilityCommand : ICommand
    {
        public readonly FacilityId Facility;

        public ExpandFacilityCommand(FacilityId facility) => Facility = facility;

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerSite.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            var config = ctx.Services.Get<GameDatabase>().Config;
            var facilities = ctx.World.Get<FacilityTable>();
            var def = ctx.Services.Get<GameDatabase>().Facilities[facilities.DefIndex[Facility.Value]];
            if (!FacilityExpansion.Possible(def, facilities.Level[Facility.Value], config)) return CommandResult.Fail("The facility cannot grow further.");
            return ctx.World.Get<PlayerState>().CoinsAt(facilities.Node[Facility.Value]) >= FacilityExpansion.Cost(def, facilities.Level[Facility.Value], config)
                ? CommandResult.Success
                : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var db = ctx.Services.Get<GameDatabase>();
            var facilities = ctx.World.Get<FacilityTable>();
            int f = Facility.Value, node = facilities.Node[f];
            long cost = FacilityExpansion.Cost(db.Facilities[facilities.DefIndex[f]], facilities.Level[f], db.Config);
            ctx.World.Get<PlayerState>().CoinsAt(node) -= cost;
            ctx.World.Get<PurseState>().PayHouseholds(node, cost);
            facilities.Level[f]++;
            ctx.Events.Publish(new FacilityExpanded(Facility, facilities.Level[f]));
        }
    }

    /// <summary>
    /// NPC investment, once a year on every node (DEC-080). The richest group that may act there, the node's private
    /// enterprise or one of the nation's companies or its state enterprise, takes the projects paying back fastest within the limit: another
    /// level of a facility it owns there, or a new facility. A good only draws
    /// investment while it is at or above its anchor price, its stock is short and its inputs are at hand. The group
    /// pays the builders and the new facility's cash, and owns it, so its dividends come back to the group. Off unless
    /// investment.payback_years is set.
    /// </summary>
    public sealed class InvestmentSystem : ISimSystem
    {
        const int StockWeeksEnough = 26, InputWeeksAtHand = 4;

        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        PurseState _purses;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        GroupTable _groups;
        GroupDirectory _directory;
        DemandForecast _forecast;

        public string Id => "agents.investment";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Investment;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _purses = ctx.World.Get<PurseState>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _facilities = ctx.World.Get<FacilityTable>();
            _groups = ctx.World.Get<GroupTable>();
            _directory = new GroupDirectory(_groups, _nodes);
            _forecast = ctx.World.Get<DemandForecast>();
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0 || _db.Config.InvestmentPaybackYears <= 0) return;
            for (int n = 0; n < _banks.NationCount; n++)
                foreach (int node in _purses.NationNodes[n])
                    for (int k = 0; k < _db.Config.InvestmentProjectsPerNode; k++)
                        if (!Invest(ctx, node, _banks.NationNode(n))) break;
        }

        bool Invest(SimContext ctx, int node, int nationNode)
        {
            int group = RichestAt(node, nationNode);
            if (group < 0) return false;
            var config = _db.Config;
            // Keeps back what staking the node's family could take; corporate bonds may add to it (DEC-097).
            long own = _groups.Cash[group] - config.GroupStartCash[(int)GroupKind.PrivateEnterprise];
            int nation = _purses.NodeFloat[_groups.Node[group]];
            long cash = own;
            if (config.CompanyDebtShare.Raw > 0 && nation < _purses.NationCount)
                cash += Math.Min(BondDesk.CompanyRoom(ctx.World, _db, group), BondDesk.Offered(ctx.World, config, ctx.Turn, nation, group));
            int level = _nodes.Level[node], count = 0;
            int bestFacility = -1, bestDef = -1;
            long bestCost = 0, bestGain = 0;

            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Node[f] != node) continue;
                count++;
                if (_facilities.Group[f] != group || _facilities.Owner[f] != OwnerIds.Npc) continue;
                var def = _db.Facilities[_facilities.DefIndex[f]];
                int at = _facilities.Level[f];
                if (!FacilityExpansion.Possible(def, at, config) || !Wanted(node, def, FacilityExpansion.OutputScale(at + 1, config))) continue;
                long cost = FacilityExpansion.Cost(def, at, config);
                long gain = (FacilityExpansion.OutputScale(at + 1, config) - FacilityExpansion.OutputScale(at, config)).MulInt(UnitMargin(node, def))
                    - config.FacilityUpkeep[(int)def.Tier];
                if (Better(gain, cost, bestGain, bestCost, cash))
                {
                    bestFacility = f;
                    bestDef = def.Index;
                    bestCost = cost;
                    bestGain = gain;
                }
            }
            if (count < config.InvestmentMaxPerNode)
                foreach (var def in _db.Facilities)
                {
                    if (def.StateOwned || !Allowed(def, level) || !FacilityPlaces.Allowed(def, _nodes, node) || !Wanted(node, def, Fx.One)) continue;
                    long cost = config.FacilityValue[(int)def.Tier] + config.FacilityStartCash[(int)def.Tier];
                    long gain = UnitMargin(node, def) - config.FacilityUpkeep[(int)def.Tier];
                    if (Better(gain, cost, bestGain, bestCost, cash))
                    {
                        bestFacility = -1;
                        bestDef = def.Index;
                        bestCost = cost;
                        bestGain = gain;
                    }
                }
            if (bestDef < 0) return false;

            if (bestCost > own)
            {
                long raised = BondDesk.Raise(ctx.World, config, ctx.Turn, group, nation, bestCost - own);
                _groups.Cash[group] += raised;
                ctx.World.Get<CreditStats>().CompanyRaised += raised;
                if (raised < bestCost - own) return false;
            }
            _groups.Cash[group] -= bestCost;
            if (bestFacility >= 0)
            {
                _purses.PayHouseholds(node, bestCost);
                _facilities.Level[bestFacility]++;
                ctx.Events.Publish(new FacilityExpanded(new FacilityId(bestFacility), _facilities.Level[bestFacility]));
                return true;
            }
            // A family must exist to buy it at auction; the investor stakes it, so no coins appear (DEC-072).
            int groupsBefore = _groups.Count;
            _directory.PrivateAt(node, config);
            for (int g = groupsBefore; g < _groups.Count; g++) _groups.Cash[group] -= _groups.Cash[g];
            var chosen = _db.Facilities[bestDef];
            long start = config.FacilityStartCash[(int)chosen.Tier];
            _purses.PayHouseholds(node, bestCost - start);
            var id = _facilities.Add(node, bestDef, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, start);
            _facilities.Group[id.Value] = group;
            ctx.Events.Publish(new FacilityFounded(id, group));
            return true;
        }

        // Any node may work goods; only villages have the land for primary producers.
        static bool Allowed(FacilityDef def, int level) => def.Tier != IndustryTier.Primary || level == (int)WorldLevel.Village;

        int RichestAt(int node, int nationNode)
        {
            int best = -1;
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups.IsPlayer[g] != 0) continue;
                var kind = _groups.KindOf(g);
                bool may = (kind == GroupKind.PrivateEnterprise && _groups.Node[g] == node) || ((kind == GroupKind.Company || kind == GroupKind.StateEnterprise) && _groups.Node[g] == nationNode);
                if (may && (best < 0 || _groups.Cash[g] > _groups.Cash[best])) best = g;
            }
            return best;
        }

        // Within the group's cash and the payback limit, and a greater gain per coin than the best so far.
        bool Better(long gain, long cost, long bestGain, long bestCost, long cash)
        {
            if (gain <= 0 || cost > cash) return false;
            if (gain * SimClock.WeeksPerYear * _db.Config.InvestmentPaybackYears < cost) return false;
            return bestCost == 0 || gain * bestCost > bestGain * cost;
        }

        // A week's output of one level one facility at the nearest prices, less its inputs and tools.
        long UnitMargin(int node, FacilityDef def)
        {
            long margin = 0;
            foreach (var line in def.Recipe.Outputs)
            {
                margin += line.Quantity * PriceOf(node, line.Good);
                margin -= _db.Config.FacilityOutputWage.MulInt(line.Quantity * _markets.GoodBasePrice[line.Good.Value]);
            }
            foreach (var line in def.Recipe.Inputs) margin -= line.Quantity * PriceOf(node, line.Good);
            foreach (var line in def.Recipe.Aids) margin -= line.Quantity * PriceOf(node, line.Good);
            return margin;
        }

        long PriceOf(int node, GoodId good) =>
            _flows.TryFindNearestMarket(node, good, out _, out int cell) ? _markets.Price[cell] : 0;

        bool Wanted(int node, FacilityDef def, Fx scale)
        {
            if (def.Recipe.Outputs.Length > 0 && _flows.TryFindNearestMarket(node, def.Recipe.Outputs[0].Good, out _, out int planned)
                && _forecast.Plan[planned] < _db.Config.InvestmentMinPlan.Raw) return false;
            foreach (var line in def.Recipe.Outputs)
            {
                if (!_flows.TryFindNearestMarket(node, line.Good, out _, out int cell)) return false;
                if (_markets.Price[cell] < _markets.GoodBasePrice[line.Good.Value]) return false;
                if (_markets.Stock[cell] > scale.MulInt(line.Quantity) * StockWeeksEnough) return false;
            }
            foreach (var line in def.Recipe.Inputs)
            {
                if (!_flows.TryFindNearestMarket(node, line.Good, out _, out int cell)) return false;
                if (_markets.Stock[cell] < scale.MulInt(line.Quantity) * InputWeeksAtHand) return false;
            }
            return true;
        }
    }
}
