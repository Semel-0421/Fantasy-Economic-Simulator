using System;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Economy
{
    /// <summary>Adds every market's weekly base consumption to demand (all levels, every turn).</summary>
    public sealed class ConsumptionSystem : ISimSystem, IDisposable
    {
        NodeTable _nodes;
        MarketStore _markets;
        MarketFlows _flows;
        NativeArray<long> _levelWeight;

        public string Id => "economy.consumption";
        public SimPhase Phase => SimPhase.Production;
        public int Order => SimOrder.Early;

        public void Initialize(SimContext ctx)
        {
            _nodes = ctx.World.Get<NodeTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            var config = ctx.Services.Get<GameDatabase>().Config;
            _levelWeight = new NativeArray<long>(WorldLevels.Count, Allocator.Persistent);
            for (int l = 0; l < WorldLevels.Count; l++) _levelWeight[l] = config.LevelDemandWeight[l].Raw;
        }

        public void Execute(SimContext ctx)
        {
            new ConsumptionJob
            {
                CategoryCount = _flows.CategoryCount,
                MarketLevel = _nodes.Level,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                CellGood = _markets.CellGood,
                GoodBaseDemand = _markets.GoodBaseDemand,
                GoodCategory = _markets.GoodCategory,
                LevelWeight = _levelWeight,
                ModifierMul = _flows.Multipliers,
                Demand = _markets.Demand,
            }.Schedule(_markets.MarketCount, 8).Complete();
        }

        public void Dispose() => _levelWeight.DisposeIfCreated();
    }

    /// <summary>
    /// Reprices every market whose level is due this turn (see <see cref="TierSchedule"/>), consuming the
    /// supply and demand accumulated since its previous update. Rates scale with the elapsed turns.
    /// Runs Late in the Markets phase so every flow of the turn is in.
    /// </summary>
    public sealed class PricingSystem : ISimSystem, IDisposable
    {
        NodeTable _nodes;
        MarketStore _markets;
        MarketFlows _flows;
        CurrencyState _currency;
        PriceParams _params;
        NativeArray<long> _anchorMul;
        NativeArray<int> _levelElapsed;

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
            _params = PriceParams.From(ctx.Services.Get<GameDatabase>().Config);
            _anchorMul = new NativeArray<long>(_nodes.Count, Allocator.Persistent);
            _levelElapsed = new NativeArray<int>(WorldLevels.Count, Allocator.Persistent);
        }

        public void Execute(SimContext ctx)
        {
            int dueMask = 0;
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                var level = (WorldLevel)l;
                if (!ctx.IsDue(level)) continue;
                dueMask |= 1 << l;
                _levelElapsed[l] = ctx.Schedule.ElapsedTurns(level, ctx.Turn);
            }
            if (dueMask == 0) return;

            for (int n = 0; n < _nodes.Count; n++) _anchorMul[n] = _currency.PriceIndex[_nodes.Continent[n]].Raw;

            var job = new PriceUpdateJob
            {
                Params = _params,
                CategoryCount = _flows.CategoryCount,
                DueLevelMask = dueMask,
                LevelElapsed = _levelElapsed,
                MarketLevel = _nodes.Level,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                MarketAnchorMul = _anchorMul,
                CellGood = _markets.CellGood,
                GoodBasePrice = _markets.GoodBasePrice,
                GoodCategory = _markets.GoodCategory,
                ModifierMul = _flows.Multipliers,
                Price = _markets.Price,
                PrevPrice = _markets.PrevPrice,
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
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
        }
    }

    /// <summary>Registers markets, modifiers, flows, currency and their systems. Requires the world module.</summary>
    public sealed class EconomyModule : ISimModule
    {
        public string Id => "economy";

        public void Register(SimBuilder builder)
        {
            var db = builder.Services.Get<GameDatabase>();
            var nodes = builder.World.Get<NodeTable>();
            var clock = builder.World.Get<SimClock>();

            var markets = builder.World.Add(MarketLayoutBuilder.Build(nodes, db));
            builder.World.Add(new MarketModifierTable());
            builder.World.Add(new MarketFlows(markets, nodes, db.Categories.Count));

            builder.Services.SetDefault<ICurrencyModel>(new GoldStandardCurrencyModel());
            var currency = builder.World.Add(new CurrencyState(nodes.ContinentCount));
            builder.Services.Get<ICurrencyModel>().Initialize(currency, db.Config, clock.Seed);

            builder.AddSystem(new WorldEventSystem());
            builder.AddSystem(new ModifierFoldSystem());
            builder.AddSystem(new ConsumptionSystem());
            builder.AddSystem(new PricingSystem());
            builder.AddSystem(new CurrencySystem());
        }
    }
}
