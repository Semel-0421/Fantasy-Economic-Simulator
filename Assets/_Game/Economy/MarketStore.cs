using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;

namespace FES.Economy
{
    /// <summary>
    /// Sparse SoA market state. Each market (= node) carries the goods its level's profile allows plus its
    /// node's specialties;
    /// its cells are the contiguous range [MarketCellStart[m], MarketCellStart[m] + MarketCellCount[m])
    /// sorted by good id. Hot loops iterate cells linearly; lookups by (market, good) binary-search.
    /// Supply and Demand are accumulators since the market's last price update: producers, consumers and
    /// merchants add to them every turn (through <see cref="MarketFlows"/>, which applies event modifiers),
    /// and the pricing system consumes and resets them when the market's tier is due.
    /// </summary>
    public sealed class MarketStore : IPersistentComponent, IDisposable
    {
        public readonly int MarketCount;
        public readonly int CellCount;

        // Layout (static, rebuilt from data).
        public NativeArray<int> MarketCellStart;
        public NativeArray<int> MarketCellCount;
        public NativeArray<int> CellGood;
        /// <summary>Per cell: 1 when the good is a specialty of the market's node (anchored lower), else 0.</summary>
        public NativeArray<byte> CellSpecialty;

        // Per-good static data copied from definitions so jobs can read it.
        public NativeArray<int> GoodBasePrice;
        public NativeArray<int> GoodBaseDemand;
        public NativeArray<int> GoodCategory;
        public NativeArray<int> GoodWeight;
        public NativeArray<int> GoodSize;

        // Dynamic state (saved).
        public NativeArray<int> Price;
        public NativeArray<int> PrevPrice;
        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;
        /// <summary>Per cell: inputs facilities could not buy since the last price update. It moves the price but trades nothing (DEC-078).</summary>
        public NativeArray<int> InputBid;

        public string Key => "economy.markets";
        public int Version => 2;

        internal MarketStore(int marketCount, int cellCount, int goodCount)
        {
            MarketCount = marketCount;
            CellCount = cellCount;
            MarketCellStart = new NativeArray<int>(marketCount, Allocator.Persistent);
            MarketCellCount = new NativeArray<int>(marketCount, Allocator.Persistent);
            CellGood = new NativeArray<int>(cellCount, Allocator.Persistent);
            CellSpecialty = new NativeArray<byte>(cellCount, Allocator.Persistent);
            GoodBasePrice = new NativeArray<int>(goodCount, Allocator.Persistent);
            GoodBaseDemand = new NativeArray<int>(goodCount, Allocator.Persistent);
            GoodCategory = new NativeArray<int>(goodCount, Allocator.Persistent);
            GoodWeight = new NativeArray<int>(goodCount, Allocator.Persistent);
            GoodSize = new NativeArray<int>(goodCount, Allocator.Persistent);
            Price = new NativeArray<int>(cellCount, Allocator.Persistent);
            PrevPrice = new NativeArray<int>(cellCount, Allocator.Persistent);
            Stock = new NativeArray<int>(cellCount, Allocator.Persistent);
            Supply = new NativeArray<int>(cellCount, Allocator.Persistent);
            Demand = new NativeArray<int>(cellCount, Allocator.Persistent);
            InputBid = new NativeArray<int>(cellCount, Allocator.Persistent);
        }

        public bool Carries(int market, GoodId good) => TryFindCell(market, good, out _);

        public bool TryFindCell(int market, GoodId good, out int cell)
        {
            int lo = MarketCellStart[market];
            int hi = lo + MarketCellCount[market] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = CellGood[mid];
                if (g == good.Value)
                {
                    cell = mid;
                    return true;
                }
                if (g < good.Value) lo = mid + 1;
                else hi = mid - 1;
            }
            cell = -1;
            return false;
        }

        /// <summary>Raw accumulation that bypasses event modifiers. Gameplay code uses <see cref="MarketFlows"/>.</summary>
        internal void AddSupplyRaw(int cell, long quantity) => Supply[cell] = ClampToNonNegativeInt(Supply[cell] + quantity);
        internal void AddDemandRaw(int cell, long quantity) => Demand[cell] = ClampToNonNegativeInt(Demand[cell] + quantity);

        public static int ClampToNonNegativeInt(long value) => value > int.MaxValue ? int.MaxValue : value < 0 ? 0 : (int)value;

        public void Write(BinaryWriter writer)
        {
            NativeIO.WriteArray(writer, Price);
            NativeIO.WriteArray(writer, PrevPrice);
            NativeIO.WriteArray(writer, Stock);
            NativeIO.WriteArray(writer, Supply);
            NativeIO.WriteArray(writer, Demand);
            NativeIO.WriteArray(writer, InputBid);
        }

        public void Read(BinaryReader reader)
        {
            NativeIO.ReadArray(reader, Price);
            NativeIO.ReadArray(reader, PrevPrice);
            NativeIO.ReadArray(reader, Stock);
            NativeIO.ReadArray(reader, Supply);
            NativeIO.ReadArray(reader, Demand);
            NativeIO.ReadArray(reader, InputBid);
        }

        /// <summary>Approximate native memory in bytes, for budgeting.</summary>
        public long NativeBytes =>
            (long)MarketCount * 2 * sizeof(int) + (long)CellCount * (6 * sizeof(int) + sizeof(byte)) + (long)GoodBasePrice.Length * 5 * sizeof(int);

        public void Dispose()
        {
            MarketCellStart.DisposeIfCreated();
            MarketCellCount.DisposeIfCreated();
            CellGood.DisposeIfCreated();
            CellSpecialty.DisposeIfCreated();
            GoodBasePrice.DisposeIfCreated();
            GoodBaseDemand.DisposeIfCreated();
            GoodCategory.DisposeIfCreated();
            GoodWeight.DisposeIfCreated();
            GoodSize.DisposeIfCreated();
            Price.DisposeIfCreated();
            PrevPrice.DisposeIfCreated();
            Stock.DisposeIfCreated();
            Supply.DisposeIfCreated();
            Demand.DisposeIfCreated();
            InputBid.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Lays out the sparse market cells from the per-level market profiles. A node's specialties are always
    /// carried (DEC-016), in every grade its level carries for any good, and anchored lower.
    /// </summary>
    public static class MarketLayoutBuilder
    {
        public static MarketStore Build(NodeTable nodes, GameDatabase db, NodeSpecialties specialties)
        {
            var goodsPerLevel = GoodsPerLevel(db);

            var gradeMaskPerLevel = new int[WorldLevels.Count];
            foreach (var rule in db.MarketProfiles) gradeMaskPerLevel[(int)rule.Level] |= rule.GradeMask;

            // Most nodes have no specialties and share their level's list.
            var goodsPerNode = new List<int>[nodes.Count];
            long cells = 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                var goods = goodsPerLevel[nodes.Level[n]];
                if (specialties.Count[n] > 0) goods = WithSpecialties(goods, specialties, n, gradeMaskPerLevel[nodes.Level[n]]);
                goodsPerNode[n] = goods;
                cells += goods.Count;
            }
            if (cells > int.MaxValue) throw new InvalidOperationException("Market cell count exceeds int range.");

            var store = new MarketStore(nodes.Count, (int)cells, db.GoodCount);
            FillGoods(store, db);
            int cell = 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                store.MarketCellStart[n] = cell;
                store.MarketCellCount[n] = goodsPerNode[n].Count;
                foreach (int g in goodsPerNode[n])
                    FillCell(store, cell++, g, db, specialties.IsSpecialty(n, new GoodId(g).BaseIndex));
            }
            return store;
        }

        // Every market of a level carries the same goods, so resolve the profile once per level.
        static List<int>[] GoodsPerLevel(GameDatabase db)
        {
            var goodsPerLevel = new List<int>[WorldLevels.Count];
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                goodsPerLevel[l] = new List<int>();
                foreach (var good in db.Goods)
                    if (CarriedAt(db, good, l)) goodsPerLevel[l].Add(good.Id.Value);
            }
            return goodsPerLevel;
        }

        static bool CarriedAt(GameDatabase db, GoodDef good, int level)
        {
            foreach (var rule in db.MarketProfiles)
                if ((int)rule.Level == level && rule.Matches(good)) return true;
            return false;
        }

        static List<int> WithSpecialties(List<int> goods, NodeSpecialties specialties, int node, int gradeMask)
        {
            var merged = new SortedSet<int>(goods);
            for (int k = specialties.Start[node]; k < specialties.Start[node] + specialties.Count[node]; k++)
                for (int g = 0; g < QualityGrades.Count; g++)
                    if ((gradeMask & (1 << g)) != 0) merged.Add(GoodId.From(specialties.BaseGood[k], (QualityGrade)g).Value);
            return new List<int>(merged);
        }

        static void FillGoods(MarketStore store, GameDatabase db)
        {
            for (int g = 0; g < db.GoodCount; g++)
            {
                store.GoodBasePrice[g] = db.Goods[g].BasePrice;
                store.GoodBaseDemand[g] = db.Goods[g].MadeToOrder ? 0 : db.Goods[g].BaseDemand; // ordered instead (DEC-085)
                store.GoodCategory[g] = db.Goods[g].CategoryIndex;
                store.GoodWeight[g] = db.Goods[g].Weight;
                store.GoodSize[g] = db.Goods[g].Size;
            }
        }

        static void FillCell(MarketStore store, int cell, int good, GameDatabase db, bool specialty)
        {
            var anchor = specialty ? db.Config.SpecialtyPriceMultiplier : Fx.One;
            int price = (int)Math.Max(1, anchor.MulInt(db.Goods[good].BasePrice));
            store.CellGood[cell] = good;
            store.CellSpecialty[cell] = (byte)(specialty ? 1 : 0);
            store.Price[cell] = price;
            store.PrevPrice[cell] = price;
        }
    }
}
