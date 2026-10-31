using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    public enum StateProjectKind : byte
    {
        Farmland = 0,
        Mine = 1,
        Harbor = 2,
        Highway = 3,
        Road = 4,
    }

    /// <summary>A project under way: on a node, or on a route when <see cref="Route"/> is not -1.</summary>
    public struct StateProject
    {
        public StateProjectKind Kind;
        public int Nation;
        public int Node;
        public int Route;
        /// <summary>For farmland and mines: the facility type to open.</summary>
        public int DefIndex;
        /// <summary>-1 while reserved, waiting for the nation's work before it.</summary>
        public int DoneTurn;
    }

    /// <summary>A nation pays producers of a good by what they make, until <see cref="EndYear"/>.</summary>
    public struct SubsidyPolicy
    {
        public int Nation;
        public int Good;
        public int EndYear;
    }

    public sealed class SubsidyEnacted : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly GoodId Good;
        public readonly int Turn;

        public SubsidyEnacted(NodeId nation, GoodId good, int turn)
        {
            Nation = nation;
            Good = good;
            Turn = turn;
        }
    }

    public sealed class StateProjectFinished : ISimEvent
    {
        public readonly StateProjectKind Kind;
        public readonly NodeId Node;
        public readonly int Turn;

        public StateProjectFinished(StateProjectKind kind, NodeId node, int turn)
        {
            Kind = kind;
            Node = node;
            Turn = turn;
        }
    }

    /// <summary>
    /// What the states have built on the land around nodes and along routes (there is no tile map, so the land
    /// around a node stands for the tiles outside it), the year's defense budgets, what each area paid for imports
    /// this year, and the projects under way.
    /// </summary>
    public sealed class DevelopmentState : IPersistentComponent, IDisposable
    {
        /// <summary>Per node: farmland and mine development, each level raising primary output there.</summary>
        public readonly int[] LandLevel;
        /// <summary>Per node: harbor works, each level cheapening water routes from it.</summary>
        public readonly int[] HarborLevel;
        /// <summary>Per route: 1 when it is a highway.</summary>
        public readonly int[] RouteGrade;
        /// <summary>Per nation: the budget set at the year's start and what defense has cost this year.</summary>
        public readonly long[] YearBudget;
        public readonly long[] DefenseSpent;
        /// <summary>Raw Fx, per nation: how far its army cuts the danger of its routes this year.</summary>
        public readonly long[] Security;
        /// <summary>Per float: value paid this year to other floats for goods flowing in.</summary>
        public NativeArray<long> Imports;
        public readonly List<StateProject> Projects = new List<StateProject>();
        public readonly List<SubsidyPolicy> Subsidies = new List<SubsidyPolicy>();
        /// <summary>Raw Fx, indexed float * goods + good: the subsidy on each unit's value.</summary>
        public NativeArray<long> SubsidyRate;
        /// <summary>Per float: what this week's subsidies may still pay; set before production.</summary>
        public NativeArray<long> SubsidyLeft;
        readonly int _goods;

        public string Key => "agents.development";
        public int Version => 1;

        public DevelopmentState(int nodes, int routes, int nations, int floats, int goods)
        {
            _goods = goods;
            Security = new long[nations];
            SubsidyRate = new NativeArray<long>(floats * goods, Allocator.Persistent);
            SubsidyLeft = new NativeArray<long>(floats, Allocator.Persistent);
            LandLevel = new int[nodes];
            HarborLevel = new int[nodes];
            RouteGrade = new int[routes];
            YearBudget = new long[nations];
            DefenseSpent = new long[nations];
            Imports = new NativeArray<long>(floats, Allocator.Persistent);
        }

        public void Write(BinaryWriter writer)
        {
            foreach (int v in LandLevel) writer.Write(v);
            foreach (int v in HarborLevel) writer.Write(v);
            foreach (int v in RouteGrade) writer.Write(v);
            foreach (long v in YearBudget) writer.Write(v);
            foreach (long v in DefenseSpent) writer.Write(v);
            foreach (long v in Security) writer.Write(v);
            writer.Write(Subsidies.Count);
            foreach (var p in Subsidies)
            {
                writer.Write(p.Nation);
                writer.Write(p.Good);
                writer.Write(p.EndYear);
            }
            NativeIO.WriteArray(writer, Imports);
            writer.Write(Projects.Count);
            foreach (var p in Projects)
            {
                writer.Write((byte)p.Kind);
                writer.Write(p.Nation);
                writer.Write(p.Node);
                writer.Write(p.Route);
                writer.Write(p.DefIndex);
                writer.Write(p.DoneTurn);
            }
        }

        public void Read(BinaryReader reader)
        {
            for (int i = 0; i < LandLevel.Length; i++) LandLevel[i] = reader.ReadInt32();
            for (int i = 0; i < HarborLevel.Length; i++) HarborLevel[i] = reader.ReadInt32();
            for (int i = 0; i < RouteGrade.Length; i++) RouteGrade[i] = reader.ReadInt32();
            for (int i = 0; i < YearBudget.Length; i++) YearBudget[i] = reader.ReadInt64();
            for (int i = 0; i < DefenseSpent.Length; i++) DefenseSpent[i] = reader.ReadInt64();
            for (int i = 0; i < Security.Length; i++) Security[i] = reader.ReadInt64();
            Subsidies.Clear();
            int subsidies = reader.ReadInt32();
            for (int i = 0; i < subsidies; i++)
                Subsidies.Add(new SubsidyPolicy { Nation = reader.ReadInt32(), Good = reader.ReadInt32(), EndYear = reader.ReadInt32() });
            NativeIO.ReadArray(reader, Imports);
            Projects.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                Projects.Add(new StateProject
                {
                    Kind = (StateProjectKind)reader.ReadByte(),
                    Nation = reader.ReadInt32(),
                    Node = reader.ReadInt32(),
                    Route = reader.ReadInt32(),
                    DefIndex = reader.ReadInt32(),
                    DoneTurn = reader.ReadInt32(),
                });
        }

        /// <summary>The rates follow the policies in force; call after they change or a load.</summary>
        public void RefreshSubsidyRates(Fx rate)
        {
            for (int i = 0; i < SubsidyRate.Length; i++) SubsidyRate[i] = 0;
            foreach (var p in Subsidies) SubsidyRate[p.Nation * _goods + p.Good] = rate.Raw;
        }

        public void Dispose()
        {
            Imports.DisposeIfCreated();
            SubsidyRate.DisposeIfCreated();
            SubsidyLeft.DisposeIfCreated();
        }
    }

    /// <summary>
    /// A nation's defense (standing army, mercenaries and their upkeep) costs a fixed share of its yearly budget, the
    /// reserve at the year's start without the player's deposits, plus maintenance on top. It is bought every few weeks
    /// from the stock of the nation's markets at current prices, so the money reaches its traders' float at once.
    /// The army makes the nation's routes safer in proportion to what it costs per route, up to a limit.
    /// </summary>
    public sealed class DefenseSystem : ISimSystem, IDisposable
    {
        SimulationConfig _config;
        BankState _bank;
        PurseState _purses;
        MarketStore _markets;
        DevelopmentState _development;
        NativeArray<long> _spend;
        NativeArray<long> _paid;
        NativeArray<long> _value;
        int[] _nationRoutes;

        public string Id => "agents.defense";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 7;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _bank = ctx.World.Get<BankState>();
            _purses = ctx.World.Get<PurseState>();
            _markets = ctx.World.Get<MarketStore>();
            _development = ctx.World.Get<DevelopmentState>();
            int nations = _development.YearBudget.Length;
            _spend = new NativeArray<long>(nations, Allocator.Persistent);
            _paid = new NativeArray<long>(nations, Allocator.Persistent);
            _value = new NativeArray<long>(nations, Allocator.Persistent);
            _nationRoutes = new int[nations];
            var routes = ctx.World.Get<RouteTable>();
            for (int r = 0; r < routes.Count; r++)
                if (_purses.NodeFloat[routes.From[r]] < nations) _nationRoutes[_purses.NodeFloat[routes.From[r]]]++;
        }

        public void Execute(SimContext ctx)
        {
            for (int n = 0; n < _spend.Length; n++)
            {
                if (ctx.Clock.IsNewYear)
                {
                    _development.YearBudget[n] = Math.Max(0, _bank.Reserve[n] - _bank.PlayerDeposit[n]);
                    _development.DefenseSpent[n] = 0;
                }
                long yearly = _config.DefenseShare.MulInt(_development.YearBudget[n]);
                yearly += _config.DefenseMaintenance.MulInt(yearly);
                if (ctx.Clock.IsNewYear)
                    _development.Security[n] = Fx.Min(_config.DefenseMaxSafety,
                        Fx.FromRatio(yearly, Math.Max(1, _nationRoutes[n] * _config.DefenseSafetyCostPerRoute))).Raw;
                _spend[n] = Math.Min(yearly * _config.DefenseIntervalWeeks / SimClock.WeeksPerYear, Math.Max(0, _bank.Reserve[n] - _bank.PlayerDeposit[n]));
            }
            if (ctx.Turn % _config.DefenseIntervalWeeks != 0) return;
            new DefensePurchaseJob
            {
                NodeFloat = _purses.NodeFloat,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                Price = _markets.Price,
                Stock = _markets.Stock,
                Demand = _markets.Demand,
                Spend = _spend,
                Paid = _paid,
                Value = _value,
            }.Schedule().Complete();
            for (int n = 0; n < _paid.Length; n++)
            {
                _bank.Reserve[n] -= _paid[n];
                _purses.Float[n] += _paid[n];
                _development.DefenseSpent[n] += _paid[n];
            }
        }

        public void Dispose()
        {
            _spend.DisposeIfCreated();
            _paid.DisposeIfCreated();
            _value.DisposeIfCreated();
        }
    }

    /// <summary>Each nation's weekly purchases, across all its stock in proportion to value, as far as there is stock to buy.</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct DefensePurchaseJob : IJob
    {
        [ReadOnly] public NativeArray<int> NodeFloat;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> Price;
        [ReadOnly] public NativeArray<long> Spend;
        public NativeArray<int> Stock;
        public NativeArray<int> Demand;
        public NativeArray<long> Paid;
        /// <summary>Scratch, per nation: the value of its stock.</summary>
        public NativeArray<long> Value;

        public void Execute()
        {
            for (int n = 0; n < Value.Length; n++)
            {
                Value[n] = 0;
                Paid[n] = 0;
            }
            for (int node = 0; node < NodeFloat.Length; node++)
            {
                int nation = NodeFloat[node];
                if (nation >= Value.Length || Spend[nation] <= 0) continue;
                int end = MarketCellStart[node] + MarketCellCount[node];
                for (int c = MarketCellStart[node]; c < end; c++) Value[nation] += (long)Stock[c] * Price[c];
            }
            for (int node = 0; node < NodeFloat.Length; node++)
            {
                int nation = NodeFloat[node];
                if (nation >= Value.Length || Spend[nation] <= 0 || Value[nation] <= 0) continue;
                long spend = Spend[nation], value = Value[nation];
                int end = MarketCellStart[node] + MarketCellCount[node];
                for (int c = MarketCellStart[node]; c < end; c++)
                {
                    long units = value <= spend ? Stock[c] : Stock[c] * spend / value;
                    if (units <= 0) continue;
                    Stock[c] -= (int)units;
                    Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + units);
                    Paid[nation] += units * Price[c];
                }
            }
        }
    }

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
        public int Order => SimOrder.Late + 18;

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
                    foreach (int node in _purses.NationNodes[nation])
                        if (_markets.TryFindCell(node, line.Good, out int cell))
                        {
                            price += _markets.Price[cell];
                            anchor += _markets.GoodBasePrice[line.Good.Value];
                        }
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
                long wanted = _population.NecessityWanted[node], met = _population.NecessityMet[node];
                long supplied = wanted > 0 ? met * 1000 / wanted : 1000;
                if (supplied >= lowest) continue;
                lowest = supplied;
                project.Node = node;
            }
            if (project.Node < 0) return;
            long bestRatio = long.MinValue;
            foreach (var def in _db.Facilities)
            {
                if (project.Kind == StateProjectKind.Farmland ? !IsFarm(def) : !IsMine(def)) continue;
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
        public int Order => SimOrder.Late + 19;

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
                    if (Subsidized(nation, good)) continue;
                    long price = 0, anchor = 0;
                    foreach (int node in _purses.NationNodes[nation])
                        if (_markets.TryFindCell(node, line.Good, out int cell))
                        {
                            price += _markets.Price[cell];
                            anchor += _markets.GoodBasePrice[good];
                        }
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
                long wanted = _population.NecessityWanted[node];
                long supplied = wanted > 0 ? _population.NecessityMet[node] * 1000 / wanted : 1000;
                if (supplied >= lowest) continue;
                lowest = supplied;
                site = node;
            }
            return site;
        }
    }
}
