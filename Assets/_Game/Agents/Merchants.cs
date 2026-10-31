using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    /// <summary>How merchant groups trade. Replace the service to plug in other merchant AI.</summary>
    public interface IMerchantBehavior
    {
        void Initialize(SimContext ctx);

        /// <summary>Every merchant group of a detailed node, every one based at one, and every one on a trip, decides, buys, travels and sells.</summary>
        void RunDetailed(SimContext ctx);

        /// <summary>The merchants of every aggregate node at once, summarized as node-to-node trade flows.</summary>
        void RunAggregate(SimContext ctx);
    }

    /// <summary>Merchants exist but do not trade (tests, or a world without trade).</summary>
    public sealed class IdleMerchantBehavior : IMerchantBehavior
    {
        public void Initialize(SimContext ctx) { }
        public void RunDetailed(SimContext ctx) { }
        public void RunAggregate(SimContext ctx) { }
    }

    public sealed class MerchantBankrupt : ISimEvent
    {
        public readonly AgentId Merchant;
        public readonly int Turn;

        public MerchantBankrupt(AgentId merchant, int turn)
        {
            Merchant = merchant;
            Turn = turn;
        }
    }

    /// <summary>
    /// Arbitrage merchants (SYS-MERCHANTS, DEC-003). A detailed group at a node picks the single most
    /// profitable load for one route out of it: buy at the current price, pay the trip's fee and toll, travel
    /// the route's turns, sell everything at the destination's price on arrival (<see cref="DetailedMerchantJob"/>).
    /// It pays a weekly upkeep and goes bankrupt after weeks below zero, replaced by a new group at its home
    /// (DEC-011). Merchants of
    /// aggregate nodes move goods along routes as flows proportional to the price gap after unit costs.
    /// Merchants stay on their home continent, since they hold one continent's coins ([Draft]).
    /// </summary>
    public sealed class ArbitrageMerchantBehavior : IMerchantBehavior, IDisposable
    {
        GameDatabase _db;
        NodeTable _nodes;
        RouteTable _routes;
        NodeTolls _tolls;
        AgentTable _agents;
        CargoTable _cargo;
        MarketStore _markets;
        MarketFlows _flows;
        LodState _lod;
        TravelCosts _costs;
        static readonly uint BankruptStream = DetHash.Of("agents.merchant_bankrupt");

        MerchantTraitTable _traits;
        NativeArray<long> _nodeSpend;
        NativeArray<long> _nodeMinMargin;
        NativeArray<long> _tollsByNode;
        NativeArray<long> _continentIndex;
        NativeArray<long> _routeDanger;
        NativeArray<MerchantLevelLimits> _levels;
        NativeArray<int> _firstRow;
        NativeArray<NodeMemory> _board;
        NativeArray<int> _stockedStart;
        NativeArray<int> _stockedCount;
        NativeList<int> _stockedCells;
        NativeList<int> _nextRow;
        NativeList<int> _rows;
        NativeList<int> _bankrupt;

        NativeArray<long> _nodeCapacity;
        NativeArray<long> _routeBudget;
        NativeArray<long> _routeExpectedLoss;
        NativeArray<long> _routeCostPerUnit;
        NativeArray<byte> _nodeCategorySpecialty;
        /// <summary>Per node: its row in <see cref="PolicyState.Licensed"/>.</summary>
        NativeArray<int> _nodeLicenseRow;
        PolicyState _policy;
        PurseState _purses;
        WorldState _world;

        /// <summary>When true the aggregate job runs as plain managed code (parity tests).</summary>
        public bool ForceManaged { get; set; }

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _routes = ctx.World.Get<RouteTable>();
            _tolls = ctx.World.Get<NodeTolls>();
            _agents = ctx.World.Get<AgentTable>();
            _cargo = ctx.World.Get<CargoTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _lod = ctx.World.Get<LodState>();
            _costs = new TravelCosts(ctx.World, _db);

            int categories = _flows.CategoryCount;
            _traits = new MerchantTraitTable(_db.Config);
            _nodeSpend = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _nodeMinMargin = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _tollsByNode = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _continentIndex = new NativeArray<long>(_nodes.ContinentCount, Allocator.Persistent);
            _routeDanger = new NativeArray<long>(_routes.Count, Allocator.Persistent);
            _levels = new NativeArray<MerchantLevelLimits>(WorldLevels.Count, Allocator.Persistent);
            for (int l = 0; l < WorldLevels.Count; l++)
                _levels[l] = new MerchantLevelLimits
                {
                    MaxWeight = _db.Config.MerchantMaxWeight[l],
                    MaxSize = _db.Config.MerchantMaxSize[l],
                    Upkeep = _db.Config.MerchantUpkeep[l],
                };
            _board = new NativeArray<NodeMemory>(_nodes.Count, Allocator.Persistent);
            _stockedStart = new NativeArray<int>(_nodes.Count, Allocator.Persistent);
            _stockedCount = new NativeArray<int>(_nodes.Count, Allocator.Persistent);
            _stockedCells = new NativeList<int>(Allocator.Persistent);
            _nextRow = new NativeList<int>(Allocator.Persistent);
            _rows = new NativeList<int>(Allocator.Persistent);
            _bankrupt = new NativeList<int>(Allocator.Persistent);
            _nodeCapacity = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _routeBudget = new NativeArray<long>(_routes.Count, Allocator.Persistent);
            _routeExpectedLoss = new NativeArray<long>(_routes.Count, Allocator.Persistent);
            _routeCostPerUnit = new NativeArray<long>(_routes.Count, Allocator.Persistent);
            _nodeCategorySpecialty = new NativeArray<byte>(_nodes.Count * categories, Allocator.Persistent);
            var specialties = ctx.World.Get<NodeSpecialties>();
            for (int n = 0; n < _nodes.Count; n++)
                for (int k = specialties.Start[n]; k < specialties.Start[n] + specialties.Count[n]; k++)
                    _nodeCategorySpecialty[n * categories + _db.Good(GoodId.From(specialties.BaseGood[k], QualityGrade.Low)).CategoryIndex] = 1;
            _policy = ctx.World.Get<PolicyState>();
            _purses = ctx.World.Get<PurseState>();
            _world = ctx.World;
            var banks = ctx.World.Get<NodeBanks>();
            _nodeLicenseRow = new NativeArray<int>(_nodes.Count, Allocator.Persistent);
            for (int n = 0; n < _nodes.Count; n++) _nodeLicenseRow[n] = _policy.Row(PolicyRules.NationOf(_nodes, banks, n));
        }

        public void RunDetailed(SimContext ctx)
        {
            PrepareRoutes();
            if (_firstRow.Length != _agents.Count)
            {
                _firstRow.DisposeIfCreated();
                _firstRow = new NativeArray<int>(_agents.Count, Allocator.Persistent);
            }
            for (int c = 0; c < _continentIndex.Length; c++) _continentIndex[c] = _world.Get<CurrencyState>().PriceIndex[c].Raw;
            _bankrupt.Clear();

            var job = new DetailedMerchantJob
            {
                Seed = ctx.Seed,
                Turn = ctx.Turn,
                EncounterStream = Encounters.Stream,
                LicenseStream = LicenseRules.MerchantStream,
                LicensedPermille = _db.Config.LicensedMerchantPermille,
                BaseGoodCount = _policy.BaseGoodCount,
                CategoryCount = _flows.CategoryCount,
                BankruptWeeks = _db.Config.MerchantBankruptWeeks,
                MerchantHolderBase = CargoHolders.MerchantBase,
                CashLossRaw = _db.Config.EncounterCashLoss.Raw,
                CargoLossRaw = _db.Config.EncounterCargoLoss.Raw,
                DangerValueScale = _db.Config.DangerValueScale,
                Rumors = _db.Config.MerchantRumorsEnabled,
                NodeDetail = _lod.Detail,
                NodeContinent = _nodes.Continent,
                NodeParent = _nodes.Parent,
                NodeLevel = _nodes.Level,
                NodeCategorySpecialty = _nodeCategorySpecialty,
                Tariff = _tolls.Tariff,
                SpecialtySurcharge = _tolls.SpecialtySurcharge,
                NodeLicenseRow = _nodeLicenseRow,
                Licensed = _policy.Licensed,
                NodeFloat = _purses.NodeFloat,
                ContinentPriceIndex = _continentIndex,
                OutStart = _routes.OutStart,
                OutCount = _routes.OutCount,
                RouteIndex = _routes.RouteIndex,
                RouteTo = _routes.To,
                RouteTurns = _routes.TravelTurns,
                RouteBaseFee = _routes.BaseFee,
                RouteCostPerUnitRaw = _routeCostPerUnit,
                RouteDangerRaw = _routeDanger,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                CellGood = _markets.CellGood,
                GoodBasePrice = _markets.GoodBasePrice,
                GoodWeight = _markets.GoodWeight,
                GoodSize = _markets.GoodSize,
                GoodCategory = _markets.GoodCategory,
                Price = _markets.Price,
                Levels = _levels,
                TraitSpendRaw = _traits.SpendRaw,
                TraitMinMarginRaw = _traits.MinMarginRaw,
                TraitCapacityRaw = _traits.CapacityRaw,
                TraitUpkeepRaw = _traits.UpkeepRaw,
                TraitDistancePower = _traits.DistancePower,
                TraitRangeLevel = _traits.RangeLevel,
                AgentHome = _agents.Home.AsArray(),
                AgentMode = _agents.Mode.AsArray(),
                AgentScale = _agents.Scale.AsArray(),
                AgentTrait = _agents.Trait.AsArray(),
                AgentMemory = _agents.Memory.AsArray(),
                AgentNode = _agents.Node.AsArray(),
                AgentDestination = _agents.Destination.AsArray(),
                AgentArrivalTurn = _agents.ArrivalTurn.AsArray(),
                AgentNegativeWeeks = _agents.NegativeCashWeeks.AsArray(),
                AgentCash = _agents.Cash.AsArray(),
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
                Float = _purses.Float,
                HouseholdCash = _world.Get<HouseholdCash>().Cash,
                TollsByNode = _tollsByNode,
                Bankrupt = _bankrupt,
                CargoHolder = _cargo.Holder.List,
                CargoGood = _cargo.Good.List,
                CargoQuantity = _cargo.Quantity.List,
                FirstRow = _firstRow,
                NextRow = _nextRow,
                Board = _board,
                Rows = _rows,
                StockedStart = _stockedStart,
                StockedCount = _stockedCount,
                StockedCells = _stockedCells,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();

            for (int n = 0; n < _tollsByNode.Length; n++)
            {
                if (_tollsByNode[n] == 0) continue;
                MoneyLedger.ToState(_world, n, _tollsByNode[n]); // same continent: one coin (DEC-015)
                _tollsByNode[n] = 0;
            }
            foreach (int a in _bankrupt) GoBankrupt(ctx, a);
        }

        void GoBankrupt(SimContext ctx, int a)
        {
            ctx.World.Get<NpcLoanTable>().WriteOff(NpcBorrowers.Merchant(a)); // the bank loses what is still owed
            _cargo.Clear(CargoHolders.Merchant(new AgentId(a)));
            _agents.Node[a] = _agents.Home[a];
            _agents.Destination[a] = -1;
            _agents.ArrivalTurn[a] = -1;
            _agents.Cash[a] = MoneyLedger.FundNewOperator(ctx.World, _agents.Group[a], _agents.Home[a], _agents.Cash[a],
                _db.Config.MerchantStartCash[_agents.Scale[a]]);
            ctx.World.Get<IncomeTaxState>().RestartMerchant(a, _agents.Cash[a]);
            _agents.NegativeCashWeeks[a] = 0;
            _agents.Memory[a] = NodeMemory.Empty;
            if (_db.Config.MerchantTraitsEnabled)
            {
                // The replacement is a new generation: its trait follows what has been working (reference, [Draft]).
                int continent = _nodes.Continent[_agents.Home[a]];
                var genetics = ctx.World.Get<MerchantGenetics>();
                genetics.RecordBankruptcy(continent, _agents.Trait[a]);
                var rng = DetRandom.For(ctx.Seed, ctx.Turn, BankruptStream, a);
                _agents.Trait[a] = genetics.Draw(ref rng, continent, _db.Config.MerchantMutationShare);
            }
            ctx.Events.Publish(new MerchantBankrupt(new AgentId(a), ctx.Turn));
        }

        // Roads change week to week (paving, decay).
        void PrepareRoutes()
        {
            for (int r = 0; r < _routes.Count; r++)
            {
                _routeCostPerUnit[r] = _costs.PerUnitCharge(r).Raw;
                _routeDanger[r] = _costs.RouteDanger(r).Raw;
                // Far regions apply the expected loss (DEC-026): the route's danger (without the value term), half
                // of which hits cargo, times the cargo loss share.
                _routeExpectedLoss[r] = (_costs.RouteDanger(r) * _db.Config.EncounterCargoLoss).Raw / 2;
            }
        }

        public void RunAggregate(SimContext ctx)
        {
            // Idle automated groups of aggregate nodes pool their cargo capacity; their traits average out.
            for (int n = 0; n < _nodeCapacity.Length; n++)
            {
                _nodeCapacity[n] = 0;
                _nodeSpend[n] = 0;
                _nodeMinMargin[n] = 0;
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                int n = _agents.Node[a];
                if (_lod.IsDetailed(n) || _lod.IsDetailed(_agents.Home[a]) || _agents.IsTravelling(a)
                    || _agents.Mode[a] != (byte)ControlMode.Automated) continue;
                int trait = _agents.Trait[a];
                long capacity = Fx.MulIntRaw(_db.Config.MerchantMaxWeight[_agents.Scale[a]], _traits.CapacityRaw[trait]);
                _nodeCapacity[n] += capacity;
                _nodeSpend[n] += capacity * _traits.SpendRaw[trait];
                _nodeMinMargin[n] += capacity * _traits.MinMarginRaw[trait];
            }
            for (int n = 0; n < _nodeCapacity.Length; n++)
            {
                if (_nodeCapacity[n] <= 0) continue;
                _nodeSpend[n] /= _nodeCapacity[n];
                _nodeMinMargin[n] /= _nodeCapacity[n];
            }

            var job = new MerchantFlowJob
            {
                CategoryCount = _flows.CategoryCount,
                FlowShareRaw = _db.Config.MerchantFlowShare.Raw,
                LicensedShareRaw = Fx.FromPermille(_db.Config.LicensedMerchantPermille).Raw,
                NodeDetail = _lod.Detail,
                NodeContinent = _nodes.Continent,
                NodeCapacity = _nodeCapacity,
                NodeSpendRaw = _nodeSpend,
                NodeMinMarginRaw = _nodeMinMargin,
                NodeCategorySpecialty = _nodeCategorySpecialty,
                Tariff = _tolls.Tariff,
                SpecialtySurcharge = _tolls.SpecialtySurcharge,
                OutStart = _routes.OutStart,
                OutCount = _routes.OutCount,
                RouteIndex = _routes.RouteIndex,
                RouteTo = _routes.To,
                RouteTurns = _routes.TravelTurns,
                RouteCostPerUnitRaw = _routeCostPerUnit,
                RouteBudget = _routeBudget,
                RouteExpectedLoss = _routeExpectedLoss,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                CellGood = _markets.CellGood,
                GoodCategory = _markets.GoodCategory,
                GoodWeight = _markets.GoodWeight,
                NodeLicenseRow = _nodeLicenseRow,
                Licensed = _policy.Licensed,
                BaseGoodCount = _policy.BaseGoodCount,
                Price = _markets.Price,
                NodeFloat = _purses.NodeFloat,
                Float = _purses.Float,
                Imports = _world.Get<DevelopmentState>().Imports,
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();
        }

        public void Dispose()
        {
            _traits?.Dispose();
            _nodeSpend.DisposeIfCreated();
            _nodeMinMargin.DisposeIfCreated();
            _tollsByNode.DisposeIfCreated();
            _continentIndex.DisposeIfCreated();
            _routeDanger.DisposeIfCreated();
            _levels.DisposeIfCreated();
            _firstRow.DisposeIfCreated();
            _board.DisposeIfCreated();
            _stockedStart.DisposeIfCreated();
            _stockedCount.DisposeIfCreated();
            if (_stockedCells.IsCreated) _stockedCells.Dispose();
            if (_nextRow.IsCreated) _nextRow.Dispose();
            if (_rows.IsCreated) _rows.Dispose();
            if (_bankrupt.IsCreated) _bankrupt.Dispose();
            _nodeCapacity.DisposeIfCreated();
            _routeBudget.DisposeIfCreated();
            _routeExpectedLoss.DisposeIfCreated();
            _routeCostPerUnit.DisposeIfCreated();
            _nodeCategorySpecialty.DisposeIfCreated();
            _nodeLicenseRow.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Aggregate merchant trade (DEC-003): for every aggregate node, along every route within its continent,
    /// each good both markets trade moves when its destination price beats the origin price plus the
    /// per-unit charge and toll. The amount is a share of the origin's stock proportional to the margin,
    /// licensed goods scaled by the licensed share, all within the node's pooled cargo capacity spread over
    /// its routes and their travel turns, goods taken in cell order. The units leave the origin's stock (through its supply into its
    /// demand, like a purchase) and reach the destination's supply the same week, less the route's expected
    /// encounter loss ([Draft]: no transit time, no base fee). Between nations, the destination's traders pay the
    /// origin's for the units at the origin price, and move no more than their float pays for (DEC-061).
    /// Nodes run in order, so results are independent of Burst.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct MerchantFlowJob : IJob
    {
        public int CategoryCount;
        public long FlowShareRaw;
        public long LicensedShareRaw;
        public int BaseGoodCount;

        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> NodeContinent;
        [ReadOnly] public NativeArray<long> NodeCapacity;
        /// <summary>Per node: its groups' average share of stock they dare move, and least margin over the buying price (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> NodeSpendRaw;
        [ReadOnly] public NativeArray<long> NodeMinMarginRaw;
        [ReadOnly] public NativeArray<byte> NodeCategorySpecialty;
        [ReadOnly] public NativeArray<long> Tariff;
        [ReadOnly] public NativeArray<long> SpecialtySurcharge;
        [ReadOnly] public NativeArray<int> OutStart;
        [ReadOnly] public NativeArray<int> OutCount;
        [ReadOnly] public NativeArray<int> RouteIndex;
        [ReadOnly] public NativeArray<int> RouteTo;
        [ReadOnly] public NativeArray<int> RouteTurns;
        /// <summary>Per route: charge per cargo unit after paving (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> RouteCostPerUnitRaw;
        /// <summary>Scratch, per route: weight the node's merchants can still move along it this week.</summary>
        public NativeArray<long> RouteBudget;
        /// <summary>Per route: expected share of the goods lost on the way (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> RouteExpectedLoss;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<int> GoodCategory;
        [ReadOnly] public NativeArray<int> GoodWeight;
        /// <summary>Per node: its licensing row; per (row, base good): 1 when selling needs a license (DEC-058).</summary>
        [ReadOnly] public NativeArray<int> NodeLicenseRow;
        [ReadOnly] public NativeArray<byte> Licensed;
        [ReadOnly] public NativeArray<int> Price;
        [ReadOnly] public NativeArray<int> NodeFloat;
        public NativeArray<long> Float;
        /// <summary>Per float: value paid to other floats for goods flowing in, for state planning.</summary>
        public NativeArray<long> Imports;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        public void Execute()
        {
            for (int n = 0; n < NodeDetail.Length; n++)
            {
                if (NodeDetail[n] == (byte)SimDetail.Detailed || NodeCapacity[n] <= 0) continue;
                int first = OutStart[n], last = first + OutCount[n];
                int routes = 0;
                for (int k = first; k < last; k++)
                    if (NodeContinent[RouteTo[RouteIndex[k]]] == NodeContinent[n]) routes++;
                if (routes == 0) continue;
                for (int k = first; k < last; k++)
                {
                    int r = RouteIndex[k];
                    RouteBudget[r] = NodeContinent[RouteTo[r]] != NodeContinent[n]
                        ? 0
                        : NodeCapacity[n] / ((long)routes * Math.Max(1, RouteTurns[r]));
                }

                // Most cells hold no stock: scan the node's cells once and look up each destination's cell.
                int cellEnd = MarketCellStart[n] + MarketCellCount[n];
                for (int c = MarketCellStart[n]; c < cellEnd; c++)
                {
                    for (int k = first; k < last && Stock[c] > 0; k++)
                    {
                        int r = RouteIndex[k];
                        if (RouteBudget[r] <= 0) continue;
                        int to = RouteTo[r];
                        int toCell = FindCell(to, CellGood[c]);
                        if (toCell < 0) continue;
                        long budget = RouteBudget[r];
                        Move(n, c, toCell, CellGood[c], to, RouteCostPerUnitRaw[r], RouteExpectedLoss[r], ref budget);
                        RouteBudget[r] = budget;
                    }
                }
            }
        }

        int FindCell(int market, int good)
        {
            int lo = MarketCellStart[market], hi = lo + MarketCellCount[market] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = CellGood[mid];
                if (g == good) return mid;
                if (g < good) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        void Move(int from, int fromCell, int toCell, int good, int to, long perUnitRaw, long expectedLossRaw, ref long budget)
        {
            long sell = Price[toCell];
            long rate = Tariff[to];
            if (NodeCategorySpecialty[to * CategoryCount + GoodCategory[good]] != 0) rate += SpecialtySurcharge[to];
            long marginRaw = ((sell - Price[fromCell] - Fx.MulIntRaw(sell, rate)) << Fx.FractionBits) - perUnitRaw;
            if (marginRaw <= 0 || Stock[fromCell] <= 0 || marginRaw < NodeMinMarginRaw[from] * Price[fromCell]) return;

            long quantity = Fx.MulIntRaw(Stock[fromCell], Fx.RoundShift(FlowShareRaw * NodeSpendRaw[from])) * marginRaw / (sell << Fx.FractionBits);
            if (Licensed[NodeLicenseRow[to] * BaseGoodCount + good / QualityGrades.Count] != 0) quantity = Fx.MulIntRaw(quantity, LicensedShareRaw);
            if (GoodWeight[good] > 0) quantity = Math.Min(quantity, budget / GoodWeight[good]);
            int payer = NodeFloat[to], payee = NodeFloat[from];
            long buy = Price[fromCell];
            if (payer != payee && buy > 0) quantity = Math.Min(quantity, Math.Max(0, Float[payer]) / buy);
            if (quantity <= 0) return;
            budget -= quantity * GoodWeight[good];
            if (payer != payee)
            {
                Float[payer] -= quantity * buy;
                Float[payee] += quantity * buy;
                Imports[payer] += quantity * buy;
            }

            Stock[fromCell] -= (int)quantity;
            Supply[fromCell] = MarketStore.ClampToNonNegativeInt(Supply[fromCell] + quantity);
            Demand[fromCell] = MarketStore.ClampToNonNegativeInt(Demand[fromCell] + quantity);
            Supply[toCell] = MarketStore.ClampToNonNegativeInt(Supply[toCell] + quantity - Fx.MulIntRaw(quantity, expectedLossRaw));
        }
    }

    /// <summary>
    /// Runs every merchant group of a detailed node, and every group on a trip (so it arrives), then the aggregate
    /// merchants of all other nodes in one pass.
    /// </summary>
    public sealed class MerchantSystem : ISimSystem, IDisposable
    {
        IMerchantBehavior _behavior;

        public string Id => "agents.merchants";
        public SimPhase Phase => SimPhase.Agents;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _behavior = ctx.Services.Get<IMerchantBehavior>();
            _behavior.Initialize(ctx);
        }

        public void Execute(SimContext ctx)
        {
            _behavior.RunDetailed(ctx);
            _behavior.RunAggregate(ctx);
        }

        public void Dispose() => (_behavior as IDisposable)?.Dispose();
    }
}
