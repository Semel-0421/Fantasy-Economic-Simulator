using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    /// <summary>
    /// Arbitrage merchants (SYS-MERCHANTS, DEC-003). A detailed group at a node picks the single most
    /// profitable load for one route out of it: buy at the current price, pay the trip's fee and toll, travel
    /// the route's turns, and on arrival sell at the destination's price unless the load is worth more elsewhere after
    /// the next trip's costs (DEC-093, <see cref="DetailedMerchantJob"/>).
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
        NativeArray<long> _coinRate;
        NativeArray<long> _international;
        NativeArray<int> _routeToward;
        NativeArray<byte> _nodePort;
        BankState _bank;
        CurrencyState _currency;
        BillTable _bills;
        BillRoom _billRoom;
        NativeArray<long> _billStats;
        NativeArray<long> _nodeSpend;
        NativeArray<long> _vehicleCapacity;
        NativeArray<long> _vehicleCost;
        NativeArray<long> _vehicleDanger;
        NativeArray<long> _vehiclePremium;
        NativeArray<long> _nodeVehicleCost;
        NativeArray<long> _nodeVehicleDanger;
        NativeArray<long> _tripStats;
        NativeArray<long> _hedgeRoom;
        NativeArray<long> _hedgeNet;
        long[] _nodeVehicles;
        int[] _nodeGroups;
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
        NativeArray<long> _bidBest;
        NativeArray<bool> _bidLinked;
        NativeArray<long> _bidBound;
        NativeArray<ulong> _cellBits;
        NativeArray<int> _cellsBefore;
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
            _vehicleCapacity = new NativeArray<long>(MerchantVehicles.Count, Allocator.Persistent);
            _vehicleCost = new NativeArray<long>(MerchantVehicles.Count, Allocator.Persistent);
            _vehicleDanger = new NativeArray<long>(MerchantVehicles.Count, Allocator.Persistent);
            _vehiclePremium = new NativeArray<long>(MerchantVehicles.Count, Allocator.Persistent);
            _nodeVehicleCost = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _nodeVehicleDanger = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _tripStats = new NativeArray<long>(21, Allocator.Persistent);
            _hedgeRoom = new NativeArray<long>(ctx.World.Get<PurseState>().Count, Allocator.Persistent);
            _hedgeNet = new NativeArray<long>(ctx.World.Get<PurseState>().Count, Allocator.Persistent);
            for (int k = 0; k < MerchantVehicles.Count; k++)
            {
                _vehicleCapacity[k] = _db.Config.MerchantVehicleCapacity[k].Raw;
                _vehicleCost[k] = _db.Config.MerchantVehicleCost[k].Raw;
                _vehicleDanger[k] = _db.Config.MerchantVehicleDanger[k].Raw;
                _vehiclePremium[k] = _db.Config.MerchantVehiclePremium[k].Raw;
            }
            _nodeVehicles = new long[_nodes.Count];
            _nodeGroups = new int[_nodes.Count];
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
            AllocateBidBounds();
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
            _bank = ctx.World.Get<BankState>();
            _currency = ctx.World.Get<CurrencyState>();
            _bills = ctx.World.Get<BillTable>();
            _billRoom = ctx.World.Get<BillRoom>();
            _billStats = new NativeArray<long>(2, Allocator.Persistent);
            int continents = _nodes.ContinentCount;
            _coinRate = new NativeArray<long>(continents * continents, Allocator.Persistent);
            _international = new NativeArray<long>(continents, Allocator.Persistent);
            _routeToward = new NativeArray<int>(_nodes.Count * continents, Allocator.Persistent);
            _nodePort = new NativeArray<byte>(_nodes.Count, Allocator.Persistent);
            for (int r = 0; r < _routes.Count; r++)
                if (_db.Config.MerchantPortCrossers && _nodes.Continent[_routes.From[r]] != _nodes.Continent[_routes.To[r]]) _nodePort[_routes.From[r]] = 1;
            PrepareWaysHome();
            var banks = ctx.World.Get<NodeBanks>();
            _nodeLicenseRow = new NativeArray<int>(_nodes.Count, Allocator.Persistent);
            for (int n = 0; n < _nodes.Count; n++) _nodeLicenseRow[n] = _policy.Row(banks.NationOf(n));
        }

        // Per node and continent: the first route of the fewest hops there, or -1.
        void AllocateBidBounds()
        {
            int continents = _nodes.ContinentCount;
            _bidBest = new NativeArray<long>(continents * _db.GoodCount, Allocator.Persistent);
            _bidLinked = new NativeArray<bool>(continents * continents, Allocator.Persistent);
            _bidBound = new NativeArray<long>(continents * continents * _db.GoodCount, Allocator.Persistent);
            int cellWords = _nodes.Count * ((_db.GoodCount + 63) / 64);
            _cellBits = new NativeArray<ulong>(cellWords, Allocator.Persistent);
            _cellsBefore = new NativeArray<int>(cellWords, Allocator.Persistent);
        }

        void PrepareWaysHome()
        {
            var frontier = new System.Collections.Generic.Queue<int>();
            for (int c = 0; c < _nodes.ContinentCount; c++) FindWaysToward(c, frontier);
        }

        void FindWaysToward(int c, System.Collections.Generic.Queue<int> frontier)
        {
            int continents = _nodes.ContinentCount;
            for (int n = 0; n < _nodes.Count; n++) _routeToward[n * continents + c] = -1;
            var seen = new bool[_nodes.Count];
            for (int n = 0; n < _nodes.Count; n++)
                if (_nodes.Continent[n] == c)
                {
                    seen[n] = true;
                    frontier.Enqueue(n);
                }
            while (frontier.Count > 0)
            {
                int at = frontier.Dequeue();
                for (int r = 0; r < _routes.Count; r++)
                {
                    if (_routes.To[r] != at || seen[_routes.From[r]]) continue;
                    seen[_routes.From[r]] = true;
                    _routeToward[_routes.From[r] * continents + c] = r;
                    frontier.Enqueue(_routes.From[r]);
                }
            }
        }

        // Coins of each continent in each other's, and what the international banks hold, for this step's job.
        void PrepareCoins()
        {
            int continents = _nodes.ContinentCount;
            for (int from = 0; from < continents; from++)
                for (int to = 0; to < continents; to++)
                    _coinRate[from * continents + to] = (_currency.ExchangeRate[from] / _currency.ExchangeRate[to]).Raw;
            for (int c = 0; c < continents; c++) _international[c] = _bank.InternationalReserve[c];
        }

        void SettleCoins()
        {
            for (int c = 0; c < _international.Length; c++) _bank.InternationalReserve[c] = _international[c];
        }

        public void RunDetailed(SimContext ctx)
        {
            PrepareRoutes();
            PrepareCoins();
            if (_firstRow.Length != _agents.Count)
            {
                _firstRow.DisposeIfCreated();
                _firstRow = new NativeArray<int>(_agents.Count, Allocator.Persistent);
            }
            for (int c = 0; c < _continentIndex.Length; c++) _continentIndex[c] = _world.Get<CurrencyState>().PriceIndex[c].Raw;
            _bankrupt.Clear();
            for (int p = 0; p < _hedgeRoom.Length; p++)
                _hedgeRoom[p] = _db.Config.HedgeFee.Raw > 0 && p < _purses.NationCount ? _db.Config.HedgeBankShare.MulInt(_bank.Spendable(p)) : 0;

            var job = new DetailedMerchantJob { Seed = ctx.Seed, Turn = ctx.Turn };
            SetRules(ref job);
            SetPlaces(ref job);
            SetTraits(ref job);
            SetGroups(ref job);
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();
            SettleDetailed();
            foreach (int a in _bankrupt) GoBankrupt(ctx, a);
            RecordTripStats();
        }

        void SettleDetailed()
        {
            SettleCoins();
            _billRoom.SettleBank(_bank);
            for (int p = 0; p < _hedgeNet.Length; p++)
            {
                if (_hedgeNet[p] != 0) _bank.Reserve[p] += _hedgeNet[p];
                _hedgeNet[p] = 0;
            }
            var credit = _world.Get<CreditStats>();
            credit.BillsIssued += _billStats[0];
            credit.BillsDiscounted += _billStats[1];
            _billStats[0] = 0;
            _billStats[1] = 0;

            for (int n = 0; n < _tollsByNode.Length; n++)
            {
                if (_tollsByNode[n] == 0) continue;
                MoneyLedger.ToState(_world, n, _tollsByNode[n]); // same continent: one coin (DEC-015)
                _tollsByNode[n] = 0;
            }
        }

        void RecordTripStats()
        {
            var stats = _world.Get<MerchantTripStats>();
            stats.Trips += _tripStats[0];
            stats.Guarded += _tripStats[1];
            stats.Insured += _tripStats[2];
            stats.Encounters += _tripStats[3];
            stats.LossSales += _tripStats[4];
            stats.LossAmount += _tripStats[5];
            stats.CarriedOn += _tripStats[6];
            stats.ForcedSales += _tripStats[7];
            stats.Sold += _tripStats[8];
            stats.SpentGoods += _tripStats[9];
            stats.SpentFees += _tripStats[10];
            stats.SpentTolls += _tripStats[11];
            stats.SpentGuards += _tripStats[12];
            stats.SpentCover += _tripStats[13];
            stats.SpentUpkeep += _tripStats[14];
            stats.SpentStolen += _tripStats[15];
            stats.SpentDiscount += _tripStats[16];
            stats.CoverRefunds += _tripStats[17];
            stats.SpentHedge += _tripStats[18];
            stats.HedgePaid += _tripStats[19];
            stats.ForcedTrips += _tripStats[20];
            for (int k = 0; k < _tripStats.Length; k++) _tripStats[k] = 0;
        }

        void SetRules(ref DetailedMerchantJob job)
        {
            job.EncounterStream = Encounters.Stream;
            job.LicenseStream = LicenseRules.MerchantStream;
            job.LicensedPermille = _db.Config.LicensedMerchantPermille;
            job.BaseGoodCount = _policy.BaseGoodCount;
            job.CategoryCount = _flows.CategoryCount;
            job.BankruptWeeks = _db.Config.MerchantBankruptWeeks;
            job.MerchantHolderBase = CargoHolders.MerchantBase;
            job.CashLossRaw = _db.Config.EncounterCashLoss.Raw;
            job.CargoLossRaw = _db.Config.EncounterCargoLoss.Raw;
            job.DangerValueScale = _db.Config.DangerValueScale;
            job.Rumors = _db.Config.MerchantRumorsEnabled;
            job.WeighSales = _db.Config.MerchantWeighSales;
            job.JudgeHops = _db.Config.MerchantJudgeHops;
            job.Bids = _db.Config.MerchantBids;
            job.BidsWhileLoaded = _db.Config.MerchantBidsWhileLoaded;
            job.ReturnHome = _db.Config.MerchantReturnHome;
            job.InputBid = _markets.InputBid;
            job.CrossLevel = _db.Config.MerchantCrossLevel;
            job.ContinentCount = _nodes.ContinentCount;
            job.CoinRateRaw = _coinRate;
            job.ExchangeFeeRaw = _db.Config.ExchangeFee.Raw;
            job.International = _international;
            job.RouteToward = _routeToward;
            job.SpreadRaw = _db.Config.TraderSpread.Raw;
            job.NodePort = _nodePort;
            job.JudgeCoinUnits = _db.Config.MerchantJudgeCoinUnits;
            job.BillRoom = _billRoom.Room;
            job.BillBankRoom = _billRoom.BankRoom;
            job.BillBankSpent = _billRoom.BankSpent;
            job.BillDiscountRaw = _db.Config.BillDiscount.Raw;
            job.BillWeeks = _db.Config.BillWeeks;
            job.BillHolder = _bills.Holder.List;
            job.BillPurse = _bills.Purse.List;
            job.BillFace = _bills.Face.List;
            job.BillDue = _bills.DueTurn.List;
            job.BillStats = _billStats;
        }

        void SetPlaces(ref DetailedMerchantJob job)
        {
            job.NodeDetail = _lod.Detail;
            job.NodeContinent = _nodes.Continent;
            job.NodeParent = _nodes.Parent;
            job.NodeLevel = _nodes.Level;
            job.NodeCategorySpecialty = _nodeCategorySpecialty;
            job.Tariff = _tolls.Tariff;
            job.SpecialtySurcharge = _tolls.SpecialtySurcharge;
            job.NodeLicenseRow = _nodeLicenseRow;
            job.Licensed = _policy.Licensed;
            job.NodeFloat = _purses.NodeFloat;
            job.ContinentPriceIndex = _continentIndex;
            job.OutStart = _routes.OutStart;
            job.OutCount = _routes.OutCount;
            job.RouteIndex = _routes.RouteIndex;
            job.RouteTo = _routes.To;
            job.RouteTurns = _routes.TravelTurns;
            job.RouteBaseFee = _routes.BaseFee;
            job.RouteCostPerUnitRaw = _routeCostPerUnit;
            job.RouteDangerRaw = _routeDanger;
            job.MarketCellStart = _markets.MarketCellStart;
            job.MarketCellCount = _markets.MarketCellCount;
            job.CellGood = _markets.CellGood;
            job.GoodBasePrice = _markets.GoodBasePrice;
            job.GoodWeight = _markets.GoodWeight;
            job.GoodSize = _markets.GoodSize;
            job.GoodCategory = _markets.GoodCategory;
            job.Price = _markets.Price;
        }

        void SetTraits(ref DetailedMerchantJob job)
        {
            job.Levels = _levels;
            job.TraitSpendRaw = _traits.SpendRaw;
            job.TraitMinMarginRaw = _traits.MinMarginRaw;
            job.TraitCapacityRaw = _traits.CapacityRaw;
            job.VehicleCapacityRaw = _vehicleCapacity;
            job.VehicleCostRaw = _vehicleCost;
            job.VehicleDangerRaw = _vehicleDanger;
            job.VehiclePremiumRaw = _vehiclePremium;
            job.GuardBaseCost = _db.Config.GuardBaseCost;
            job.MemberPremiumRaw = _db.Config.MerchantMemberPremium.Raw;
            job.AgentAssociation = _agents.Association.AsArray();
            job.TraitLossAversion = _traits.LossAversion;
            job.NodeCoreStrength = _nodes.CoreStrength;
            job.AgentOwner = _agents.Owner.AsArray();
            job.CountTripCosts = _db.Config.MerchantCountTripCosts;
            job.MustAct = _db.Config.MerchantMustAct;
            job.HedgeFloorRaw = _db.Config.HedgeFloor.Raw;
            job.HedgeFeeRaw = _db.Config.HedgeFee.Raw;
            job.HedgeFallRaw = _db.Config.HedgeFall.Raw;
            job.HedgeChanceRaw = _db.Config.HedgeFallChance.Raw;
            job.HedgeRoom = _hedgeRoom;
            job.HedgeNet = _hedgeNet;
            job.AgentHedge = _agents.HedgeFloor.AsArray();
            job.GuardCostShareRaw = _db.Config.GuardCostShare.Raw;
            job.GuardReductionRaw = _db.Config.GuardReduction.Raw;
            job.InsurancePremiumRaw = _db.Config.InsurancePremium.Raw;
            job.InsuranceRefundRaw = _db.Config.InsuranceRefund.Raw;
            job.TripStats = _tripStats;
            job.AgentVehicle = _agents.Vehicle.AsArray();
            job.TraitUpkeepRaw = _traits.UpkeepRaw;
            job.TraitDistancePower = _traits.DistancePower;
            job.TraitRangeLevel = _traits.RangeLevel;
        }

        void SetGroups(ref DetailedMerchantJob job)
        {
            job.AgentHome = _agents.Home.AsArray();
            job.AgentMode = _agents.Mode.AsArray();
            job.AgentScale = _agents.Scale.AsArray();
            job.AgentTrait = _agents.Trait.AsArray();
            job.AgentMemory = _agents.Memory.AsArray();
            job.AgentNode = _agents.Node.AsArray();
            job.AgentDestination = _agents.Destination.AsArray();
            job.AgentArrivalTurn = _agents.ArrivalTurn.AsArray();
            job.AgentNegativeWeeks = _agents.NegativeCashWeeks.AsArray();
            job.AgentCash = _agents.Cash.AsArray();
            job.AgentLoadCost = _agents.LoadCost.AsArray();
            job.Stock = _markets.Stock;
            job.Supply = _markets.Supply;
            job.Demand = _markets.Demand;
            job.Float = _purses.Float;
            job.HouseholdCash = _world.Get<HouseholdCash>().Cash;
            job.TollsByNode = _tollsByNode;
            job.Bankrupt = _bankrupt;
            job.CargoHolder = _cargo.Holder.List;
            job.CargoGood = _cargo.Good.List;
            job.CargoQuantity = _cargo.Quantity.List;
            job.FirstRow = _firstRow;
            job.NextRow = _nextRow;
            job.Board = _board;
            job.Rows = _rows;
            job.StockedStart = _stockedStart;
            job.StockedCount = _stockedCount;
            job.StockedCells = _stockedCells;
            job.BidBest = _bidBest;
            job.BidLinked = _bidLinked;
            job.BidBound = _bidBound;
            job.CellBits = _cellBits;
            job.CellsBefore = _cellsBefore;
        }

        void GoBankrupt(SimContext ctx, int a)
        {
            ctx.World.Get<NpcLoanTable>().WriteOff(NpcBorrowers.Merchant(a)); // the bank loses what is still owed
            _cargo.Clear(CargoHolders.Merchant(new AgentId(a)));
            for (int r = 0; r < _bills.Count; r++)
                if (_bills.Holder[r] == a) _bills.Holder[r] = BillHolders.Households; // its bills pass to the people it owed
            _world.Get<MerchantTripStats>().LostWithGroups += _agents.LoadCost[a];
            _agents.LoadCost[a] = 0;
            _agents.HedgeFloor[a] = 0;
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
            _agents.Vehicle[a] = MerchantVehicles.Pack; // sold off with the old group
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
            PoolIdleGroups();
            PrepareCoins();
            var job = new MerchantFlowJob
            {
                WageRaw = _db.Config.MerchantFlowWage.Raw,
                HouseholdCash = _world.Get<HouseholdCash>().Cash,
                CrossFlowRaw = _db.Config.MerchantCrossFlow.Raw,
                ContinentCount = _nodes.ContinentCount,
                CoinRateRaw = _coinRate,
                ExchangeFeeRaw = _db.Config.ExchangeFee.Raw,
                International = _international,
                CategoryCount = _flows.CategoryCount,
                FlowShareRaw = _db.Config.MerchantFlowShare.Raw,
                LicensedShareRaw = Fx.FromPermille(_db.Config.LicensedMerchantPermille).Raw,
                NodeDetail = _lod.Detail,
                NodeContinent = _nodes.Continent,
                NodeCapacity = _nodeCapacity,
                NodeSpendRaw = _nodeSpend,
                NodeMinMarginRaw = _nodeMinMargin,
                NodeVehicleCostRaw = _nodeVehicleCost,
                NodeVehicleDangerRaw = _nodeVehicleDanger,
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
            SettleCoins();
        }

        // Idle automated groups of aggregate nodes pool their cargo capacity; their traits average out.
        void PoolIdleGroups()
        {
            for (int n = 0; n < _nodeCapacity.Length; n++)
            {
                _nodeCapacity[n] = 0;
                _nodeSpend[n] = 0;
                _nodeMinMargin[n] = 0;
                _nodeVehicles[n] = 0;
                _nodeVehicleCost[n] = Fx.OneRaw;
                _nodeVehicleDanger[n] = Fx.OneRaw;
                _nodeGroups[n] = 0;
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                int n = _agents.Node[a];
                if (_lod.IsDetailed(n) || _lod.IsDetailed(_agents.Home[a]) || _agents.IsTravelling(a)
                    || _agents.Mode[a] != (byte)ControlMode.Automated) continue;
                int trait = _agents.Trait[a];
                long capacity = Fx.MulIntRaw(Fx.MulIntRaw(_db.Config.MerchantMaxWeight[_agents.Scale[a]], _traits.CapacityRaw[trait]), _vehicleCapacity[_agents.Vehicle[a]]);
                _nodeVehicles[n] += _vehicleCapacity[_agents.Vehicle[a]];
                if (_nodeGroups[n] == 0)
                {
                    _nodeVehicleCost[n] = 0;
                    _nodeVehicleDanger[n] = 0;
                }
                _nodeVehicleCost[n] += _vehicleCost[_agents.Vehicle[a]];
                _nodeVehicleDanger[n] += _vehicleDanger[_agents.Vehicle[a]];
                _nodeGroups[n]++;
                _nodeCapacity[n] += capacity;
                _nodeSpend[n] += capacity * _traits.SpendRaw[trait];
                _nodeMinMargin[n] += capacity * _traits.MinMarginRaw[trait];
            }
            for (int n = 0; n < _nodeCapacity.Length; n++)
            {
                if (_nodeCapacity[n] <= 0) continue;
                // Better vehicles dare move a larger share of the stock (DEC-084).
                _nodeSpend[n] = (Fx.FromRaw(_nodeSpend[n] / _nodeCapacity[n]) * Fx.FromRaw(_nodeVehicles[n] / _nodeGroups[n])).Raw;
                _nodeVehicleCost[n] /= _nodeGroups[n];
                _nodeVehicleDanger[n] /= _nodeGroups[n];
                _nodeMinMargin[n] /= _nodeCapacity[n];
            }
        }

        public void Dispose()
        {
            _traits?.Dispose();
            _coinRate.DisposeIfCreated();
            _billStats.DisposeIfCreated();
            _international.DisposeIfCreated();
            _routeToward.DisposeIfCreated();
            _nodePort.DisposeIfCreated();
            _nodeSpend.DisposeIfCreated();
            _vehicleCapacity.DisposeIfCreated();
            _vehicleCost.DisposeIfCreated();
            _vehicleDanger.DisposeIfCreated();
            _vehiclePremium.DisposeIfCreated();
            _nodeVehicleCost.DisposeIfCreated();
            _nodeVehicleDanger.DisposeIfCreated();
            _tripStats.DisposeIfCreated();
            _hedgeRoom.DisposeIfCreated();
            _hedgeNet.DisposeIfCreated();
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
            _bidBest.DisposeIfCreated();
            _bidLinked.DisposeIfCreated();
            _bidBound.DisposeIfCreated();
            _cellBits.DisposeIfCreated();
            _cellsBefore.DisposeIfCreated();
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
}
