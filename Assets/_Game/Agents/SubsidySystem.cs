using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// With money above its reserve target, a nation enacts a subsidy on the dearest good its markets carry and some
    /// facility type makes, for a few years. Producers are paid by what they make (<see cref="ProductionJob"/>). Each year
    /// of a policy one more producer opens at the node shortest of necessities where the good can be made, with a free plot
    /// and room under the subsidies' cap a node (DEC-135): run by a group already making the good, or else by a new
    /// company founded for it, and the trade's guild is founded where a city has none (DEC-074).
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
        int[] _count = System.Array.Empty<int>();

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
            CountFacilities();
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
            if (def == null) return;
            long facilityCash = config.FacilityStartCash[(int)def.Tier];
            long mostGroupCash = config.GroupStartCash[(int)GroupKind.Company] + config.GroupStartCash[(int)GroupKind.Guild]
                + config.GroupStartCash[(int)GroupKind.PrivateEnterprise];
            var land = _world.Get<LandState>();
            int site = Site(policy.Nation, def, land, facilityCash + mostGroupCash);
            if (site < 0) return;
            long plot = land.Price(site, config);

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
            land.TryBuild(_world, config, site, out _);
            _bank.Reserve[policy.Nation] -= plot; // the state buys the plot from the households there
            _world.Get<PurseState>().PayHouseholds(site, plot);
            var id = _facilities.Add(site, def.Index, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, facilityCash);
            _facilities.Group[id.Value] = group;
            _count[site]++;
        }

        void CountFacilities()
        {
            if (_count.Length != _nodes.Count) _count = new int[_nodes.Count];
            System.Array.Clear(_count, 0, _count.Length);
            for (int f = 0; f < _facilities.Count; f++)
                if (_facilities.Level[f] > 0) _count[_facilities.Node[f]]++;
        }

        // A free plot to build on (DEC-128) and the money for it all above the reserve's target.
        bool CanBuild(LandState land, int site, int nation, long cost) =>
            land.Free(site) > 0 && MoneyLedger.StateExcess(_world, _db.Config, nation) >= cost;

        int ExistingProducer(int nation, int defIndex)
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.DefIndex[f] != defIndex || _facilities.Group[f] < 0 || _purses.NodeFloat[_facilities.Node[f]] != nation) continue;
                if (_facilities.Owner[f] == OwnerIds.Npc) return _facilities.Group[f];
            }
            return -1;
        }

        // DEC-135: the node shortest of necessities where the good can be made (a fine workshop only at a core town, DEC-104),
        // with a free plot, room under the cap and the money for it all; -1 for none.
        int Site(int nation, FacilityDef def, LandState land, long cost)
        {
            var config = _db.Config;
            int site = -1;
            long lowest = long.MaxValue;
            foreach (int node in _purses.NationNodes[nation])
            {
                if (_nodes.Level[node] <= (int)WorldLevel.Nation || !FacilityPlaces.IsLand(_nodes, node) || !FacilityPlaces.Allowed(def, _nodes, node)
                    || _count[node] + _development.LandProjectsAt(node) >= config.StateMaxPerNode) continue;
                long supplied = _population.NecessitySuppliedPermille(node);
                if (supplied >= lowest || !CanBuild(land, node, nation, cost + land.Price(node, config))) continue;
                lowest = supplied;
                site = node;
            }
            return site;
        }
    }
}
