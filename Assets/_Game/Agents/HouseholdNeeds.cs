using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>
    /// DEC-124: per market cell, the necessities its households wish to buy this week at the price that nothing at hand
    /// covers: not their own stores, the stock or this week's supply. Arriving merchants sell into it first.
    /// [Draft] Market modifiers are left out of the wish.
    /// </summary>
    public sealed class HouseholdNeeds : IDisposable
    {
        public NativeArray<long> Units;
        readonly GameDatabase _db;
        readonly MarketStore _markets;
        readonly NodeTable _nodes;
        readonly HouseholdState _population;
        readonly HouseholdCash _cash;
        readonly CurrencyState _currency;
        readonly DemandParams _demand;
        readonly long[] _luxuryShare;

        public HouseholdNeeds(WorldState world, GameDatabase db)
        {
            _db = db;
            _markets = world.Get<MarketStore>();
            _nodes = world.Get<NodeTable>();
            _population = world.Get<HouseholdState>();
            _cash = world.Get<HouseholdCash>();
            _currency = world.Get<CurrencyState>();
            _demand = DemandParams.From(db.Config);
            _luxuryShare = new long[db.GoodCount];
            for (int g = 0; g < _luxuryShare.Length; g++) _luxuryShare[g] = db.Config.LuxuryShare(db.Goods[g]).Raw;
            Units = new NativeArray<long>(_markets.CellCount, Allocator.Persistent);
        }

        public void Refresh()
        {
            var config = _db.Config;
            for (int n = 0; n < _markets.MarketCount; n++)
            {
                int level = _nodes.Level[n], end = _markets.MarketCellStart[n] + _markets.MarketCellCount[n];
                long index = _currency.PriceIndex[_nodes.Continent[n]].Raw;
                long necessity = _population.NecessityWeight(n, level, config).Raw, luxury = _population.LuxuryWeight(n, level, config).Raw;
                for (int c = _markets.MarketCellStart[n]; c < end; c++)
                    Units[c] = config.MerchantSellToHouseholds ? Need(c, level, index, necessity, luxury) : 0;
            }
        }

        long Need(int c, int level, long index, long necessity, long luxury)
        {
            int good = _markets.CellGood[c];
            if (_luxuryShare[good] >= Fx.OneRaw) return 0;
            var def = _db.Goods[good];
            long weekly = HouseholdDemand.CellBase(def.BaseDemand, def.BasePrice, _db.Config.LevelDemandWeight[level].Raw, Fx.OneRaw, index, out long anchor);
            long wished = HouseholdDemand.Weekly(_demand, weekly, _markets.Price[c], anchor, _luxuryShare[good], necessity, luxury).Met;
            return Math.Max(0, wished - _cash.HomeGrown[c] - _markets.Stock[c] - _markets.Supply[c]);
        }

        public void Dispose() => Units.DisposeIfCreated();
    }
}
