using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Once a year each nation spends a share of its reserve above target on land development, choosing by what holds
    /// it back: unsupplied necessities call for farmland, dear mined goods for mines, import bills for harbors and
    /// highways, and unpaved roads between its towns for paving. The year's plan is a queue: one project at a time,
    /// builders' wages paid to the site's households as it starts, and whatever has not started when the next plan is
    /// made gives way to it. Farmland and mines open a facility of the nation's state enterprise and
    /// raise primary output on that land; roads are paved and kept by the nation (DEC-072).
    /// </summary>
    public sealed class StateProjectSystem : ISimSystem
    {
        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        RouteTable _routes;
        RoadState _roads;
        BankState _bank;
        PurseState _purses;
        MarketStore _markets;
        HouseholdState _population;
        HouseholdCash _households;
        NodePorts _ports;
        NodeSpecialties _specialties;
        FacilityTable _facilities;
        DevelopmentState _development;
        GroupDirectory _groups;
        TravelCosts _costs;
        WorldState _world;

        public string Id => "agents.state_projects";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.StateProjects;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _routes = ctx.World.Get<RouteTable>();
            _roads = ctx.World.Get<RoadState>();
            _bank = ctx.World.Get<BankState>();
            _purses = ctx.World.Get<PurseState>();
            _markets = ctx.World.Get<MarketStore>();
            _population = ctx.World.Get<HouseholdState>();
            _households = ctx.World.Get<HouseholdCash>();
            _ports = ctx.World.Get<NodePorts>();
            _specialties = ctx.World.Get<NodeSpecialties>();
            _facilities = ctx.World.Get<FacilityTable>();
            _development = ctx.World.Get<DevelopmentState>();
            _groups = new GroupDirectory(ctx.World.Get<GroupTable>(), _nodes);
            _costs = new TravelCosts(ctx.World, _db);
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            Finish(ctx);
            if (ctx.Clock.IsNewYear && ctx.Turn > 0)
            {
                for (int n = 0; n < _banks.NationCount; n++) Plan(n);
                for (int p = 0; p < _development.Imports.Length; p++) _development.Imports[p] = 0;
            }
            StartNext(ctx);
        }

        // In each nation with nothing under way, its next reserved project, as soon as the reserve can pay for it.
        void StartNext(SimContext ctx)
        {
            var projects = _development.Projects;
            for (int n = 0; n < _banks.NationCount; n++)
            {
                int next = -1;
                bool working = false;
                for (int i = 0; i < projects.Count && !working; i++)
                {
                    if (projects[i].Nation != n) continue;
                    if (projects[i].DoneTurn >= 0) working = true;
                    else if (next < 0) next = i;
                }
                if (working || next < 0) continue;
                var project = projects[next];
                long cost = CostOf(project);
                if (_bank.Reserve[n] - _bank.PlayerDeposit[n] < cost) continue;
                _bank.Reserve[n] -= cost;
                _purses.PayHouseholds(project.Route >= 0 ? _routes.From[project.Route] : project.Node, cost);
                project.DoneTurn = ctx.Turn + _db.Config.StateProjectWeeks;
                projects[next] = project;
            }
        }

        void Plan(int nation)
        {
            var config = _db.Config;
            _development.Projects.RemoveAll(p => p.Nation == nation && p.DoneTurn < 0);
            long budget = config.StateProjectShare.MulInt(Math.Max(0, MoneyLedger.StateExcess(_world, config, nation)));
            var scores = new long[5];
            scores[(int)StateProjectKind.Farmland] = NecessityGapPermille(nation);
            scores[(int)StateProjectKind.Mine] = MinedGoodsDearnessPermille(nation);
            long imports = ImportsPermille(nation);
            scores[(int)StateProjectKind.Harbor] = imports + config.StateTradeScore;
            scores[(int)StateProjectKind.Highway] = imports + config.StateTradeScore;
            scores[(int)StateProjectKind.Road] = config.StateRoadScore;
            for (int k = 0; k < config.StateProjectsPerYear; k++)
            {
                int best = -1;
                for (int kind = 0; kind < scores.Length; kind++)
                    if (scores[kind] > 0 && (best < 0 || scores[kind] > scores[best])) best = kind;
                if (best < 0) return;
                var kindOf = (StateProjectKind)best;
                var project = Site(kindOf, nation);
                scores[best] = 0; // the next project of the year looks at what else holds the nation back
                if (project.Node < 0 && project.Route < 0) continue;
                long cost = CostOf(project);
                if (cost > budget) continue;
                budget -= cost;
                project.Nation = nation;
                project.DoneTurn = -1;
                _development.Projects.Add(project);
            }
        }

        long NecessityGapPermille(int nation)
        {
            long wanted = 0, met = 0;
            foreach (int node in _purses.NationNodes[nation])
            {
                wanted += _population.NecessityWanted[node];
                met += _population.NecessityMet[node];
            }
            if (wanted <= 0 || met >= wanted) return 0;
            return (wanted - met) * 1000 / wanted;
        }

        long MinedGoodsDearnessPermille(int nation)
        {
            long price = 0, anchor = 0;
            foreach (var def in _db.Facilities)
            {
                if (!IsMine(def)) continue;
                foreach (var line in def.Recipe.Outputs)
                    NationMarkets.AddPrices(_markets, _purses.NationNodes[nation], line.Good, ref price, ref anchor);
            }
            return anchor > 0 && price > anchor ? (price - anchor) * 1000 / anchor : 0;
        }

        long ImportsPermille(int nation)
        {
            long spent = 0;
            foreach (int node in _purses.NationNodes[nation]) spent += _households.SpentYear[node];
            return _development.Imports[nation] * 1000 / Math.Max(1, spent);
        }

        // In the data, state-owned primary producers are the mines; other primary producers work farmland.
        static bool IsMine(FacilityDef def) => def.Tier == IndustryTier.Primary && def.StateOwned;

        static bool IsFarm(FacilityDef def) => def.Tier == IndustryTier.Primary && !def.StateOwned;

        StateProject Site(StateProjectKind kind, int nation)
        {
            var project = new StateProject { Kind = kind, Node = -1, Route = -1, DefIndex = -1 };
            switch (kind)
            {
                case StateProjectKind.Farmland:
                case StateProjectKind.Mine:
                    SiteOnLand(ref project, nation);
                    break;
                case StateProjectKind.Harbor:
                    foreach (int node in _purses.NationNodes[nation])
                        if (_ports.Contains(node) && _development.HarborLevel[node] < _db.Config.StateHarborMax && !Underway(kind, node, -1))
                        {
                            project.Node = node;
                            break;
                        }
                    break;
                case StateProjectKind.Highway:
                    project.Route = HighwaySite(nation);
                    break;
                case StateProjectKind.Road:
                    project.Route = RoadSite(nation);
                    break;
            }
            return project;
        }

        // The undeveloped node whose households went shortest of necessities, for a type whose output is dearest there,
        // preferring land with a specialty in it.
        void SiteOnLand(ref StateProject project, int nation)
        {
            long lowest = long.MaxValue;
            foreach (int node in _purses.NationNodes[nation])
            {
                if (_development.LandLevel[node] >= _db.Config.StateLandMax || _nodes.Level[node] <= (int)WorldLevel.Nation) continue;
                if (Underway(project.Kind, node, -1)) continue;
                long supplied = _population.NecessitySuppliedPermille(node);
                if (supplied >= lowest) continue;
                lowest = supplied;
                project.Node = node;
            }
            if (project.Node < 0) return;
            long bestRatio = long.MinValue;
            foreach (var def in _db.Facilities)
            {
                if (project.Kind == StateProjectKind.Farmland ? !IsFarm(def) : !IsMine(def)) continue;
                if (!FacilityPlaces.Allowed(def, _nodes, project.Node)) continue;
                long ratio = 0;
                foreach (var line in def.Recipe.Outputs)
                {
                    if (_specialties.IsSpecialty(project.Node, line.Good.BaseIndex)) ratio += 1000;
                    if (_markets.TryFindCell(project.Node, line.Good, out int cell))
                        ratio += _markets.Price[cell] * 1000L / Math.Max(1, _markets.GoodBasePrice[line.Good.Value]);
                }
                if (ratio <= bestRatio) continue;
                bestRatio = ratio;
                project.DefIndex = def.Index;
            }
            if (project.DefIndex < 0) project.Node = -1;
        }

        // A land route out of a port or the end of a highway, so highways grow inland from the coast.
        int HighwaySite(int nation)
        {
            for (int r = 0; r < _routes.Count; r++)
            {
                if (!OpenForWork(r, nation) || _development.RouteGrade[r] > 0) continue;
                int from = _routes.From[r];
                if (_ports.Contains(from) || EndsHighway(from)) return Underway(StateProjectKind.Highway, -1, r) ? -1 : r;
            }
            return -1;
        }

        bool EndsHighway(int node)
        {
            for (int r = 0; r < _routes.Count; r++)
                if (_routes.To[r] == node && _development.RouteGrade[r] > 0) return true;
            return false;
        }

        // An unpaved land route between two of the nation's towns.
        int RoadSite(int nation)
        {
            for (int r = 0; r < _routes.Count; r++)
            {
                if (!OpenForWork(r, nation) || _roads.IsPaved(r)) continue;
                if (_nodes.Level[_routes.From[r]] > (int)WorldLevel.City || _nodes.Level[_routes.To[r]] > (int)WorldLevel.City) continue;
                if (!Underway(StateProjectKind.Road, -1, r)) return r;
            }
            return -1;
        }

        bool OpenForWork(int r, int nation) =>
            !_costs.IsWater(r) && _roads.BuildDoneTurn[r] < 0
            && _purses.NodeFloat[_routes.From[r]] == nation && _purses.NodeFloat[_routes.To[r]] == nation;

        bool Underway(StateProjectKind kind, int node, int route)
        {
            foreach (var p in _development.Projects)
                if (p.Kind == kind && p.Node == node && p.Route == route) return true;
            return false;
        }

        long CostOf(in StateProject project)
        {
            var config = _db.Config;
            switch (project.Kind)
            {
                case StateProjectKind.Farmland: return config.StateFarmlandCost;
                case StateProjectKind.Mine: return config.StateMineCost;
                case StateProjectKind.Harbor: return config.StateHarborCost;
                case StateProjectKind.Highway: return config.StateHighwayCostPerTurn * _routes.TravelTurns[project.Route];
                default: return config.RoadBuildCostPerTurn * _routes.TravelTurns[project.Route];
            }
        }

        void Finish(SimContext ctx)
        {
            var projects = _development.Projects;
            for (int i = 0; i < projects.Count; i++)
            {
                var p = projects[i];
                if (p.DoneTurn < 0 || ctx.Turn < p.DoneTurn) continue;
                projects.RemoveAt(i--);
                switch (p.Kind)
                {
                    case StateProjectKind.Farmland:
                    case StateProjectKind.Mine:
                        OpenFacility(p);
                        break;
                    case StateProjectKind.Harbor:
                        _development.HarborLevel[p.Node]++;
                        break;
                    case StateProjectKind.Highway:
                        Pave(p, 1);
                        break;
                    case StateProjectKind.Road:
                        Pave(p, 0);
                        break;
                }
                ctx.Events.Publish(new StateProjectFinished(p.Kind, new NodeId(p.Route >= 0 ? _routes.From[p.Route] : p.Node), ctx.Turn));
            }
        }

        // The state enterprise opens it, with its starting cash from the reserve.
        void OpenFacility(in StateProject p)
        {
            _development.LandLevel[p.Node]++;
            var def = _db.Facilities[p.DefIndex];
            // A family must exist to buy it at auction; the state stakes it, as nothing else made its coins.
            var groups = _world.Get<GroupTable>();
            int before = groups.Count;
            int family = _groups.PrivateAt(p.Node, _db.Config);
            if (groups.Count > before) _bank.Reserve[p.Nation] -= groups.Cash[family];
            long cash = Math.Min(_db.Config.FacilityStartCash[(int)def.Tier], Math.Max(0, _bank.Reserve[p.Nation]));
            _bank.Reserve[p.Nation] -= cash;
            var id = _facilities.Add(p.Node, p.DefIndex, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, cash);
            _facilities.Group[id.Value] = _groups.StateEnterpriseOf(_banks.NationNode(p.Nation));
        }

        void Pave(in StateProject p, int grade)
        {
            foreach (int r in new[] { p.Route, _costs.FindRoute(_routes.To[p.Route], _routes.From[p.Route]) })
            {
                if (r < 0) continue;
                _roads.Condition[r] = RoadState.MaxCondition;
                _roads.Owner[r] = p.Nation;
                if (grade > 0) _development.RouteGrade[r] = grade;
            }
        }
    }
}
