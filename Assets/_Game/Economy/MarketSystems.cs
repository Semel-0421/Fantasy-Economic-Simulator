using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Economy
{
    /// <summary>
    /// Reprices every market whose level is due this turn (see <see cref="TierSchedule"/>), consuming the
    /// supply and demand accumulated since its previous update, households' consumption included. Rates scale
    /// with the elapsed turns.
    /// Runs Late in the Markets phase so every flow of the turn is in.
    /// </summary>
    public sealed class PricingSystem : ISimSystem, IDisposable
    {
        NodeTable _nodes;
        MarketStore _markets;
        MarketFlows _flows;
        CurrencyState _currency;
        HouseholdState _households;
        HouseholdCash _cash;
        SimulationConfig _config;
        PriceParams _params;
        DemandParams _demand;
        NativeArray<long> _anchorMul;
        NativeArray<int> _levelElapsed;
        NativeArray<long> _levelWeight;
        NativeArray<long> _necessityWeight;
        NativeArray<long> _luxuryWeight;
        NativeArray<long> _goodLuxuryShare;
        NativeArray<long> _budget;
        long _specialtyAnchorMulRaw;

        public string Id => "economy.pricing";
        public SimPhase Phase => SimPhase.Markets;
        public int Order => SimOrder.Late;

        /// <summary>When true the job runs as plain managed code (parity tests).</summary>
        public bool ForceManaged { get; set; }

        public void Initialize(SimContext ctx)
        {
            _nodes = ctx.World.Get<NodeTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _currency = ctx.World.Get<CurrencyState>();
            _households = ctx.World.Get<HouseholdState>();
            _cash = ctx.World.Get<HouseholdCash>();
            var db = ctx.Services.Get<GameDatabase>();
            _config = db.Config;
            _params = PriceParams.From(_config);
            _demand = DemandParams.From(_config);
            _specialtyAnchorMulRaw = _config.SpecialtyPriceMultiplier.Raw;
            _anchorMul = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _levelElapsed = new NativeArray<int>(WorldLevels.Count, Allocator.Persistent);
            _levelWeight = new NativeArray<long>(WorldLevels.Count, Allocator.Persistent);
            for (int l = 0; l < WorldLevels.Count; l++) _levelWeight[l] = _config.LevelDemandWeight[l].Raw;
            _necessityWeight = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _luxuryWeight = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _budget = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _goodLuxuryShare = new NativeArray<long>(db.GoodCount, Allocator.Persistent);
            for (int g = 0; g < db.GoodCount; g++)
                _goodLuxuryShare[g] = db.Goods[g].IsLuxury ? Fx.One.Raw : _config.GradeLuxuryShare[(int)db.Goods[g].Grade].Raw;
        }

        public void Execute(SimContext ctx)
        {
            // Population and income change yearly; refresh their weights here so loads need no hook.
            for (int n = 0; n < _nodes.Count; n++)
            {
                int level = _nodes.Level[n];
                _anchorMul[n] = _currency.PriceIndex[_nodes.Continent[n]].Raw;
                _necessityWeight[n] = _households.NecessityWeight(n, level, _config).Raw;
                _luxuryWeight[n] = _households.LuxuryWeight(n, level, _config).Raw;
                _budget[n] = _cash.BudgetFactor(n, _config.HouseholdBudgetWeeks);
            }

            int inlineMask = 0;
            for (int l = 0; l < WorldLevels.Count; l++)
                if (ctx.Schedule[(WorldLevel)l].Period == 1) inlineMask |= 1 << l;
            int slowMask = ((1 << WorldLevels.Count) - 1) & ~inlineMask;
            if (slowMask != 0) Consume(slowMask, ctx.Turn);

            int dueMask = 0;
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                var level = (WorldLevel)l;
                if (!ctx.IsDue(level)) continue;
                dueMask |= 1 << l;
                _levelElapsed[l] = ctx.Schedule.ElapsedTurns(level, ctx.Turn);
            }
            if (dueMask == 0) return;

            var job = new PriceUpdateJob
            {
                Params = _params,
                Consumption = _demand,
                CategoryCount = _flows.CategoryCount,
                DueLevelMask = dueMask,
                InlineLevelMask = inlineMask,
                SpecialtyAnchorMulRaw = _specialtyAnchorMulRaw,
                Turn = ctx.Turn,
                NeedMemoryWeeks = _config.NeedMemoryWeeks,
                LastOffered = _cash.LastOffered,
                LevelElapsed = _levelElapsed,
                MarketLevel = _nodes.Level,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                MarketAnchorMul = _anchorMul,
                CellGood = _markets.CellGood,
                CellSpecialty = _markets.CellSpecialty,
                GoodBasePrice = _markets.GoodBasePrice,
                GoodCategory = _markets.GoodCategory,
                ModifierMul = _flows.Multipliers,
                GoodBaseDemand = _markets.GoodBaseDemand,
                GoodLuxuryShare = _goodLuxuryShare,
                LevelWeight = _levelWeight,
                MarketNecessityWeight = _necessityWeight,
                MarketLuxuryWeight = _luxuryWeight,
                NecessityWanted = _households.NecessityWanted,
                NecessityMet = _households.NecessityMet,
                MarketBudget = _budget,
                Cash = _cash.Cash,
                Spent = _cash.Spent,
                Desired = _cash.Desired,
                PendingUnits = _cash.PendingUnits,
                Price = _markets.Price,
                PrevPrice = _markets.PrevPrice,
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
                InputBid = _markets.InputBid,
            };
            if (ForceManaged)
            {
                for (int m = 0; m < _markets.MarketCount; m++) job.Execute(m);
                return;
            }
            job.Schedule(_markets.MarketCount, 4).Complete();
        }

        // Household consumption for markets that reprice less often than every turn.
        void Consume(int levelMask, int turn)
        {
            var job = new ConsumptionJob
            {
                Consumption = _demand,
                CategoryCount = _flows.CategoryCount,
                LevelMask = levelMask,
                SpecialtyAnchorMulRaw = _specialtyAnchorMulRaw,
                Turn = turn,
                NeedMemoryWeeks = _config.NeedMemoryWeeks,
                LastOffered = _cash.LastOffered,
                MarketLevel = _nodes.Level,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                MarketAnchorMul = _anchorMul,
                MarketNecessityWeight = _necessityWeight,
                MarketLuxuryWeight = _luxuryWeight,
                CellGood = _markets.CellGood,
                CellSpecialty = _markets.CellSpecialty,
                Price = _markets.Price,
                GoodBaseDemand = _markets.GoodBaseDemand,
                GoodBasePrice = _markets.GoodBasePrice,
                GoodCategory = _markets.GoodCategory,
                GoodLuxuryShare = _goodLuxuryShare,
                LevelWeight = _levelWeight,
                ModifierMul = _flows.Multipliers,
                MarketBudget = _budget,
                Supply = _markets.Supply,
                Stock = _markets.Stock,
                Demand = _markets.Demand,
                NecessityWanted = _households.NecessityWanted,
                NecessityMet = _households.NecessityMet,
                Desired = _cash.Desired,
                PendingUnits = _cash.PendingUnits,
            };
            if (ForceManaged)
            {
                for (int m = 0; m < _markets.MarketCount; m++) job.Execute(m);
                return;
            }
            job.Schedule(_markets.MarketCount, 4).Complete();
        }

        public void Dispose()
        {
            _anchorMul.DisposeIfCreated();
            _levelElapsed.DisposeIfCreated();
            _levelWeight.DisposeIfCreated();
            _necessityWeight.DisposeIfCreated();
            _luxuryWeight.DisposeIfCreated();
            _goodLuxuryShare.DisposeIfCreated();
            _budget.DisposeIfCreated();
        }
    }

    /// <summary>Registers markets, modifiers, flows, currency and their systems. Requires the world module.</summary>
    public sealed class EconomyModule : ISimModule
    {
        static readonly string[] RequiredModules = { "world" };

        public string Id => "economy";
        public IReadOnlyList<string> Requires => RequiredModules;

        public void Register(SimBuilder builder)
        {
            var db = builder.Services.Get<GameDatabase>();
            var nodes = builder.World.Get<NodeTable>();
            var clock = builder.World.Get<SimClock>();

            var markets = builder.World.Add(MarketLayoutBuilder.Build(nodes, db, builder.World.Get<NodeSpecialties>()));
            builder.World.Add(new MarketModifierTable());
            builder.World.Add(new MarketFlows(markets, nodes, db.Categories.Count));

            builder.Services.SetDefault<ICurrencyModel>(new GoldStandardCurrencyModel());
            var currency = builder.World.Add(new CurrencyState(nodes.ContinentCount));
            builder.Services.Get<ICurrencyModel>().Initialize(currency, db.Config, clock.Seed);
            builder.World.Add(new HouseholdState(nodes, builder.Services.Get<MapDef>(), db.Config));
            builder.World.Add(new HouseholdCash(nodes.Count, markets.CellCount));

            builder.AddSystem(new WorldEventSystem());
            builder.AddSystem(new ModifierFoldSystem());
            builder.AddSystem(new PricingSystem());
            builder.AddSystem(new CurrencySystem());
            builder.AddMigration(new MarketStoreV1Migration());
            builder.AddMigration(new CurrencyStateV1Migration());
            builder.AddMigration(new CurrencyStateV2Migration());
            builder.AddMigration(new HouseholdCashV1Migration());
            builder.AddMigration(new HouseholdCashV2Migration());
        }
    }
}
