using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// With money above its reserve target, a nation enacts a subsidy on the dearest good its markets carry and some
    /// facility type makes, for a few years. Producers are paid by what they make (<see cref="ProductionJob"/>). Each year
    /// of a policy one more producer opens at the node shortest of necessities: run by a group already making the good,
    /// or else by a new company founded for it, and the trade's guild is founded where a city has none (DEC-074).
    /// The state stakes the new firms from its reserve, so no coins appear.
    /// </summary>
    public sealed class SubsidySystem : ISimSystem, ILoadListener
    {
        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        PurseState _purses;
        MarketStore _markets;
        HouseholdState _population;
        FacilityTable _facilities;
        GroupTable _groupTable;
        GroupDirectory _groups;
        DevelopmentState _development;
        WorldState _world;

        public string Id => "agents.subsidies";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Subsidies;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _purses = ctx.World.Get<PurseState>();
            _markets = ctx.World.Get<MarketStore>();
            _population = ctx.World.Get<HouseholdState>();
            _facilities = ctx.World.Get<FacilityTable>();
            _groupTable = ctx.World.Get<GroupTable>();
            _groups = new GroupDirectory(_groupTable, _nodes);
            _development = ctx.World.Get<DevelopmentState>();
            _world = ctx.World;
        }

        public void OnLoaded(SimContext ctx) => _development.RefreshSubsidyRates(_db.Config.SubsidyRate);

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;
            var config = _db.Config;
            var policies = _development.Subsidies;
            policies.RemoveAll(p => p.EndYear <= ctx.Clock.Year);
            for (int n = 0; n < _banks.NationCount; n++)
            {
                if (MoneyLedger.StateExcess(_world, config, n) <= 0) continue;
                int active = 0;
                foreach (var p in policies)
                    if (p.Nation == n) active++;
                if (active >= config.SubsidyMaxPerNation) continue;
                int good = DearestGood(n);
                if (good < 0) continue;
                policies.Add(new SubsidyPolicy { Nation = n, Good = good, EndYear = ctx.Clock.Year + config.SubsidyYears });
                ctx.Events.Publish(new SubsidyEnacted(new NodeId(_banks.NationNode(n)), new GoodId(good), ctx.Turn));
            }
            _development.RefreshSubsidyRates(config.SubsidyRate);
            foreach (var p in policies) OpenProducer(p);
        }

        bool Subsidized(int nation, int good)
        {
            foreach (var p in _development.Subsidies)
                if (p.Nation == nation && p.Good == good) return true;
            return false;
        }

        // Only a good dearer than its anchor across the nation calls for a subsidy.
        int DearestGood(int nation)
        {
            int best = -1;
            long bestRatio = 1000;
            foreach (var def in _db.Facilities)
                foreach (var line in def.Recipe.Outputs)
                {
                    int good = line.Good.Value;
                    if (Subsidized(nation, good) || !FacilityPlaces.Allowed(def, _nodes, _banks.NationNode(nation))) continue;
                    long price = 0, anchor = 0;
                    NationMarkets.AddPrices(_markets, _purses.NationNodes[nation], line.Good, ref price, ref anchor);
                    if (anchor <= 0) continue;
                    long ratio = price * 1000 / anchor;
                    if (ratio <= bestRatio) continue;
                    bestRatio = ratio;
                    best = good;
                }
            return best;
        }

        void OpenProducer(in SubsidyPolicy policy)
        {
            var config = _db.Config;
            FacilityDef def = null;
            foreach (var d in _db.Facilities)
                foreach (var line in d.Recipe.Outputs)
                    if (line.Good.Value == policy.Good && def == null) def = d;
            int site = ShortestOfNecessities(policy.Nation);
            if (def == null || site < 0) return;
            long facilityCash = config.FacilityStartCash[(int)def.Tier];
            long mostGroupCash = config.GroupStartCash[(int)GroupKind.Company] + config.GroupStartCash[(int)GroupKind.Guild]
                + config.GroupStartCash[(int)GroupKind.PrivateEnterprise];
            if (MoneyLedger.StateExcess(_world, config, policy.Nation) < facilityCash + mostGroupCash) return;

            int group = ExistingProducer(policy.Nation, def.Index);
            int groupsBefore = _groupTable.Count;
            if (group < 0) group = _groupTable.Add(GroupKind.Company, _banks.NationNode(policy.Nation), -1, config.GroupStartCash[(int)GroupKind.Company]);
            int city = _groups.CityOf(site), category = _markets.GoodCategory[policy.Good];
            if (city >= 0 && _groups.GuildOf(city, category) < 0)
                _groupTable.Add(GroupKind.Guild, city, category, config.GroupStartCash[(int)GroupKind.Guild]);
            // A family must exist to buy the facility at auction, as for the state's own (DEC-072).
            _groups.PrivateAt(site, config);
            for (int g = groupsBefore; g < _groupTable.Count; g++) _bank.Reserve[policy.Nation] -= _groupTable.Cash[g];
            _bank.Reserve[policy.Nation] -= facilityCash;
            var id = _facilities.Add(site, def.Index, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, facilityCash);
            _facilities.Group[id.Value] = group;
        }

        int ExistingProducer(int nation, int defIndex)
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.DefIndex[f] != defIndex || _facilities.Group[f] < 0 || _purses.NodeFloat[_facilities.Node[f]] != nation) continue;
                if (_facilities.Owner[f] == OwnerIds.Npc) return _facilities.Group[f];
            }
            return -1;
        }

        int ShortestOfNecessities(int nation)
        {
            int site = -1;
            long lowest = long.MaxValue;
            foreach (int node in _purses.NationNodes[nation])
            {
                if (_nodes.Level[node] <= (int)WorldLevel.Nation) continue;
                long supplied = _population.NecessitySuppliedPermille(node);
                if (supplied >= lowest) continue;
                lowest = supplied;
                site = node;
            }
            return site;
        }
    }
}
