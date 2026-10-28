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

        /// <summary>One merchant group of a detailed node (or one on a trip) decides, buys, travels and sells.</summary>
        void RunDetailed(SimContext ctx, AgentId merchant);

        /// <summary>The merchants of every aggregate node at once, summarized as node-to-node trade flows.</summary>
        void RunAggregate(SimContext ctx);
    }

    /// <summary>Merchants exist but do not trade (tests, or a world without trade).</summary>
    public sealed class IdleMerchantBehavior : IMerchantBehavior
    {
        public void Initialize(SimContext ctx) { }
        public void RunDetailed(SimContext ctx, AgentId merchant) { }
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
    /// the route's turns, sell everything at the destination's price on arrival. It pays a weekly upkeep and
    /// goes bankrupt after weeks below zero, replaced by a new group at its home (DEC-011). Merchants of
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
        readonly List<CargoItem> _items = new List<CargoItem>();

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

        public void RunDetailed(SimContext ctx, AgentId merchant)
        {
            int a = merchant.Value;
            if (_agents.Mode[a] != (byte)ControlMode.Automated) return;
            if (_agents.IsTravelling(a) && ctx.Turn >= _agents.ArrivalTurn[a]) Arrive(ctx, a);
            if (!_agents.IsTravelling(a)) TryStartTrip(ctx, a);
            PayUpkeep(ctx, a);
        }

        bool HoldsLicenses(SimContext ctx, int a) =>
            LicenseRules.MerchantHoldsLicenses(ctx.Seed, new AgentId(a), _db.Config.LicensedMerchantPermille);

        // Sells the whole load at the destination's current price (DEC-006), as far as the traders there can pay
        // (DEC-061); unsellable goods stay on board.
        void Arrive(SimContext ctx, int a)
        {
            int node = _agents.Destination[a];
            _agents.Node[a] = node;
            _agents.Destination[a] = -1;
            _agents.ArrivalTurn[a] = -1;
            int holder = CargoHolders.Merchant(new AgentId(a));
            _cargo.Collect(holder, _items);
            foreach (var item in _items)
            {
                if (NeedsLicense(node, item.Good.Value) && !HoldsLicenses(ctx, a)) continue;
                if (!_markets.TryFindCell(node, item.Good, out int cell)) continue;
                int units = _purses.Sellable(node, item.Quantity, _markets.Price[cell]);
                if (units <= 0) continue;
                _flows.Sell(cell, units);
                _agents.Cash[a] += (long)units * _markets.Price[cell];
                _purses.ChangeFloat(node, -(long)units * _markets.Price[cell]);
                _cargo.Change(holder, item.Good, -units);
            }
        }

        void TryStartTrip(SimContext ctx, int a)
        {
            int node = _agents.Node[a];
            long cash = _agents.Cash[a];
            if (cash <= 0) return;
            int scale = _agents.Scale[a];
            var capacity = new CargoCapacity(_db.Config.MerchantMaxWeight[scale], _db.Config.MerchantMaxSize[scale]);
            int holder = CargoHolders.Merchant(new AgentId(a));
            var load = _cargo.LoadOf(holder, _markets.GoodWeight, _markets.GoodSize);
            bool licensed = HoldsLicenses(ctx, a);

            int bestRoute = -1, bestCell = -1;
            long bestQuantity = 0, bestProfit = 0;
            int firstRoute = _routes.OutStart[node], lastRoute = firstRoute + _routes.OutCount[node];
            int cellEnd = _markets.MarketCellStart[node] + _markets.MarketCellCount[node];
            // Only stocked goods can be bought, and most cells have no stock: scan those once, then look up
            // each destination's cell.
            for (int cell = _markets.MarketCellStart[node]; cell < cellEnd; cell++)
            {
                if (_markets.Stock[cell] <= 0) continue;
                var good = new GoodId(_markets.CellGood[cell]);
                long buy = _markets.Price[cell];
                long quantityCap = Math.Min(_markets.Stock[cell],
                    capacity.MaxAdditional(load, _markets.GoodWeight[good.Value], _markets.GoodSize[good.Value]));
                if (quantityCap <= 0) continue;
                for (int k = firstRoute; k < lastRoute; k++)
                {
                    int r = _routes.RouteIndex[k];
                    int to = _routes.To[r];
                    if (_nodes.Continent[to] != _nodes.Continent[node] || !_markets.TryFindCell(to, good, out int toCell)) continue;
                    if (!licensed && NeedsLicense(to, good.Value)) continue;
                    long baseFee = _routes.BaseFee[r];
                    var perUnit = _costs.PerUnitCharge(r);
                    long sell = _markets.Price[toCell];
                    long toll = Fx.FromRaw(TollRate(to, good.Value)).MulInt(sell);
                    var margin = Fx.FromInt(sell - buy - toll) - perUnit;
                    if (margin.Raw <= 0) continue;
                    long unitCost = Math.Max(1, buy + toll + perUnit.MulInt(1));
                    long quantity = Math.Min(quantityCap, (cash - baseFee) / unitCost);
                    long profit = margin.MulInt(quantity) - baseFee;
                    if (quantity <= 0 || profit <= bestProfit) continue;
                    bestRoute = r;
                    bestCell = cell;
                    bestQuantity = quantity;
                    bestProfit = profit;
                }
            }
            if (bestRoute < 0) return;

            var goodId = new GoodId(_markets.CellGood[bestCell]);
            _flows.Buy(bestCell, (int)bestQuantity);
            _agents.Cash[a] -= bestQuantity * _markets.Price[bestCell];
            _purses.ChangeFloat(node, bestQuantity * _markets.Price[bestCell]);
            _cargo.Change(holder, goodId, (int)bestQuantity);
            _cargo.Collect(holder, _items);
            var quote = _costs.Quote(bestRoute, _items, new TripOptions(false, false, true)); // merchants need no ship (DEC-034)
            _agents.Cash[a] -= quote.DepartureCost + quote.Toll; // same continent: one coin (DEC-015)
            _purses.PayHouseholds(node, quote.DepartureCost); // fees are wages at the origin (DEC-061)
            MoneyLedger.ToState(_world, _routes.To[bestRoute], quote.Toll);
            _agents.Destination[a] = _routes.To[bestRoute];
            _agents.ArrivalTurn[a] = ctx.Turn + _routes.TravelTurns[bestRoute];

            // Detailed merchants roll encounters like the player, without guards or insurance ([Draft]).
            var loss = Encounters.Roll(ctx.Seed, ctx.Turn, a, 1, quote.EncounterChance);
            if (loss == EncounterLoss.Cash)
            {
                // [Draft] Stolen coins end up spent where the trip began.
                long stolen = _db.Config.EncounterCashLoss.MulInt(Math.Max(0, _agents.Cash[a]));
                _agents.Cash[a] -= stolen;
                _purses.PayHouseholds(node, stolen);
            }
            else if (loss == EncounterLoss.Cargo) Encounters.TakeCargo(_cargo, holder, _db.Config.EncounterCargoLoss, _items);
        }

        bool NeedsLicense(int node, int good) =>
            _policy.Licensed[_nodeLicenseRow[node] * _policy.BaseGoodCount + good / QualityGrades.Count] != 0;

        long TollRate(int node, int good)
        {
            long rate = _tolls.Tariff[node];
            if (_nodeCategorySpecialty[node * _flows.CategoryCount + _markets.GoodCategory[good]] != 0) rate += _tolls.SpecialtySurcharge[node];
            return rate;
        }

        // Upkeep can push cash below zero; enough such weeks and a new group replaces this one (DEC-011).
        void PayUpkeep(SimContext ctx, int a)
        {
            _agents.Cash[a] -= _db.Config.MerchantUpkeep[_agents.Scale[a]];
            _purses.PayHouseholds(_agents.Node[a], _db.Config.MerchantUpkeep[_agents.Scale[a]]);
            if (_agents.Cash[a] >= 0)
            {
                _agents.NegativeCashWeeks[a] = 0;
                return;
            }
            if (++_agents.NegativeCashWeeks[a] < _db.Config.MerchantBankruptWeeks) return;
            ctx.World.Get<NpcLoanTable>().WriteOff(NpcBorrowers.Merchant(a)); // the bank loses what is still owed
            _cargo.Clear(CargoHolders.Merchant(new AgentId(a)));
            _agents.Node[a] = _agents.Home[a];
            _agents.Destination[a] = -1;
            _agents.ArrivalTurn[a] = -1;
            _agents.Cash[a] = MoneyLedger.FundNewOperator(ctx.World, _agents.Group[a], _agents.Home[a], _agents.Cash[a],
                _db.Config.MerchantStartCash[_agents.Scale[a]]);
            _agents.NegativeCashWeeks[a] = 0;
            ctx.Events.Publish(new MerchantBankrupt(new AgentId(a), ctx.Turn));
        }

        public void RunAggregate(SimContext ctx)
        {
            // Idle automated groups of aggregate nodes pool their cargo capacity.
            for (int n = 0; n < _nodeCapacity.Length; n++) _nodeCapacity[n] = 0;
            for (int a = 0; a < _agents.Count; a++)
            {
                int n = _agents.Node[a];
                if (_lod.IsDetailed(n) || _agents.IsTravelling(a) || _agents.Mode[a] != (byte)ControlMode.Automated) continue;
                _nodeCapacity[n] += _db.Config.MerchantMaxWeight[_agents.Scale[a]];
            }

            // Roads change week to week (paving, decay). Far regions apply the expected loss (DEC-026): the
            // route's danger (without the value term), half of which hits cargo, times the cargo loss share.
            for (int r = 0; r < _routes.Count; r++)
            {
                _routeCostPerUnit[r] = _costs.PerUnitCharge(r).Raw;
                _routeExpectedLoss[r] = (_costs.RouteDanger(r) * _db.Config.EncounterCargoLoss).Raw / 2;
            }

            var job = new MerchantFlowJob
            {
                CategoryCount = _flows.CategoryCount,
                FlowShareRaw = _db.Config.MerchantFlowShare.Raw,
                LicensedShareRaw = Fx.FromPermille(_db.Config.LicensedMerchantPermille).Raw,
                NodeDetail = _lod.Detail,
                NodeContinent = _nodes.Continent,
                NodeCapacity = _nodeCapacity,
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
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();
        }

        public void Dispose()
        {
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
            if (marginRaw <= 0 || Stock[fromCell] <= 0) return;

            long quantity = Fx.MulIntRaw(Stock[fromCell], FlowShareRaw) * marginRaw / (sell << Fx.FractionBits);
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
            }

            Stock[fromCell] -= (int)quantity;
            Supply[fromCell] = MarketStore.ClampToNonNegativeInt(Supply[fromCell] + quantity);
            Demand[fromCell] = MarketStore.ClampToNonNegativeInt(Demand[fromCell] + quantity);
            Supply[toCell] = MarketStore.ClampToNonNegativeInt(Supply[toCell] + quantity - Fx.MulIntRaw(quantity, expectedLossRaw));
        }
    }

    /// <summary>
    /// Steps every merchant group of a detailed node, and every group on a trip (so it arrives), then runs the
    /// aggregate merchants of all other nodes in one pass.
    /// </summary>
    public sealed class MerchantSystem : ISimSystem, IDisposable
    {
        AgentTable _agents;
        LodState _lod;
        IMerchantBehavior _behavior;

        public string Id => "agents.merchants";
        public SimPhase Phase => SimPhase.Agents;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _agents = ctx.World.Get<AgentTable>();
            _lod = ctx.World.Get<LodState>();
            _behavior = ctx.Services.Get<IMerchantBehavior>();
            _behavior.Initialize(ctx);
        }

        public void Execute(SimContext ctx)
        {
            for (int a = 0; a < _agents.Count; a++)
                if (_agents.IsTravelling(a) || _lod.IsDetailed(_agents.Node[a])) _behavior.RunDetailed(ctx, new AgentId(a));
            _behavior.RunAggregate(ctx);
        }

        public void Dispose() => (_behavior as IDisposable)?.Dispose();
    }
}
