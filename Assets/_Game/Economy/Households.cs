using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;

namespace FES.Economy
{
    /// <summary>
    /// The households behind each market's consumption (SYS-MARKETS, DEC-049, DEC-050): every node's
    /// population and weekly income per head, split over five income quintiles by the config's Pareto shares.
    /// Necessities are bought per head, luxuries in proportion to income. Also accumulates, per node, how much
    /// of their necessity needs households could afford since the last yearly update.
    /// </summary>
    public sealed class HouseholdState : IPersistentComponent, IDisposable
    {
        public readonly int NodeCount;
        public readonly long[] Population;
        /// <summary>Raw Fx: coins per head and week.</summary>
        public readonly long[] IncomePerHead;
        /// <summary>Per node: necessity units wanted at the anchor price since the last yearly update.</summary>
        public NativeArray<long> NecessityWanted;
        /// <summary>Per node: necessity units demanded at the prices households met.</summary>
        public NativeArray<long> NecessityMet;

        /// <summary>The share of its necessities a node's households could buy, in thousandths; full when they wanted none.</summary>
        public long NecessitySuppliedPermille(int node) =>
            NecessityWanted[node] > 0 ? NecessityMet[node] * 1000 / NecessityWanted[node] : 1000;

        public string Key => "economy.households";
        public int Version => 1;

        readonly Fx[] _demandScale;

        public HouseholdState(NodeTable nodes, MapDef map, SimulationConfig config)
        {
            NodeCount = nodes.Count;
            Population = new long[NodeCount];
            IncomePerHead = new long[NodeCount];
            NecessityWanted = new NativeArray<long>(NodeCount, Allocator.Persistent);
            NecessityMet = new NativeArray<long>(NodeCount, Allocator.Persistent);
            for (int n = 0; n < NodeCount; n++)
            {
                int level = nodes.Level[n];
                Population[n] = config.DefaultPopulation[level];
                IncomePerHead[n] = config.DefaultIncomePerHead[level].Raw;
            }
            foreach (var def in map.Nodes)
                if (def.Population >= 0 && nodes.TryFind(def.Key, out var node)) Population[node.Value] = def.Population;
            _demandScale = new Fx[NodeCount];
            for (int n = 0; n < NodeCount; n++) _demandScale[n] = Fx.One;
            if (nodes.Absorbed) AbsorbClasses(nodes, config);
        }

        // A core town takes the people of its classes, and their weight of demand, on top of what being a core draws (DEC-104).
        void AbsorbClasses(NodeTable nodes, SimulationConfig config)
        {
            int city = (int)WorldLevel.City;
            var weight = new long[NodeCount];
            var people = new long[NodeCount];
            for (int n = 0; n < NodeCount; n++)
            {
                if (nodes.Level[n] >= city) continue;
                int core = nodes.Core[n];
                if (nodes.Level[n] != (byte)WorldLevel.Continent && config.CoreDemandWeighted) weight[core] += config.LevelDemandWeight[nodes.Level[n]].Raw; // a continent has hardly any people
                if (nodes.Level[n] != (byte)WorldLevel.Continent && config.CoreDemandWeighted) people[core] += config.DefaultPopulation[nodes.Level[n]];
                if (core == n) continue;
                Population[core] += Population[n];
                Population[n] = 0;
            }
            for (int n = 0; n < NodeCount; n++)
            {
                if (!nodes.IsSeat(n)) continue;
                int level = nodes.Level[n];
                var byWeight = Fx.FromRatio(weight[n] + config.LevelDemandWeight[level].Raw, config.LevelDemandWeight[level].Raw);
                var byPeople = Fx.FromRatio(config.DefaultPopulation[level], people[n] + config.DefaultPopulation[level]);
                _demandScale[n] = byWeight * byPeople * (Fx.One + Fx.FromInt(nodes.CoreStrength[n]) * config.CoreDemandPerPoint);
            }
        }

        /// <summary>Necessity demand relative to a default node of the level: population over the default.</summary>
        public Fx NecessityWeight(int node, int level, SimulationConfig config) =>
            Fx.FromRatio(Population[node], config.DefaultPopulation[level]) * _demandScale[node];

        /// <summary>Luxury demand relative to a default node of the level: total income over the default.</summary>
        public Fx LuxuryWeight(int node, int level, SimulationConfig config) =>
            Fx.FromRatio(Population[node] * IncomePerHead[node],
                config.DefaultPopulation[level] * config.DefaultIncomePerHead[level].Raw) * _demandScale[node];

        public void Write(BinaryWriter writer)
        {
            writer.Write(NodeCount);
            for (int n = 0; n < NodeCount; n++)
            {
                writer.Write(Population[n]);
                writer.Write(IncomePerHead[n]);
                writer.Write(NecessityWanted[n]);
                writer.Write(NecessityMet[n]);
            }
        }

        public void Read(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count != NodeCount) throw new InvalidDataException($"Saved node count {count} != {NodeCount}.");
            for (int n = 0; n < count; n++)
            {
                Population[n] = reader.ReadInt64();
                IncomePerHead[n] = reader.ReadInt64();
                NecessityWanted[n] = reader.ReadInt64();
                NecessityMet[n] = reader.ReadInt64();
            }
        }

        public void Dispose()
        {
            NecessityWanted.DisposeIfCreated();
            NecessityMet.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Households' money (DEC-061, P13b): cash per node, spent on their consumption. A cell's household units
    /// wait for its next repricing and are paid for at its price as far as the market delivers them; what a
    /// market's households paid this turn waits in <see cref="Spent"/> until the agents module hands it to the
    /// traders' float of their nation.
    /// </summary>
    public sealed class HouseholdCash : IPersistentComponent, IDisposable
    {
        /// <summary>Per node: the households' coins.</summary>
        public NativeArray<long> Cash;
        /// <summary>Per node: paid for consumption this turn, not yet handed to the traders.</summary>
        public NativeArray<long> Spent;
        /// <summary>
        /// Per node: last turn's wished consumption the markets could deliver, at their prices, before the
        /// budget; sets this turn's budget share.
        /// </summary>
        public NativeArray<long> Desired;
        /// <summary>Per node: the part of <see cref="Desired"/> that is necessities, bought first (DEC-083).</summary>
        public NativeArray<long> DesiredNecessity;
        /// <summary>Per cell: household units consumed since the market last repriced, not yet paid for.</summary>
        public NativeArray<int> PendingUnits;
        /// <summary>Per cell: the last turn the market offered the good (supply or stock), for population needs (P13d).</summary>
        public NativeArray<int> LastOffered;
        /// <summary>Per node: paid for consumption since the year began (P13c).</summary>
        public NativeArray<long> SpentYear;
        /// <summary>Per node: cash when the year began; with <see cref="SpentYear"/> it gives the year's income.</summary>
        public NativeArray<long> CashYearStart;
        /// <summary>Per node: lent out since the year began, less what came back, with the interest earned: what income should not count as gone.</summary>
        public NativeArray<long> SavedYear;

        public const string SectionKey = "economy.household_cash";
        public string Key => SectionKey;
        public int Version => 5;

        /// <summary>A good the market has never offered: older than any memory of it.</summary>
        public const int NeverOffered = -1000000;

        public HouseholdCash(int nodeCount, int cellCount)
        {
            Cash = new NativeArray<long>(nodeCount, Allocator.Persistent);
            Spent = new NativeArray<long>(nodeCount, Allocator.Persistent);
            Desired = new NativeArray<long>(nodeCount, Allocator.Persistent);
            DesiredNecessity = new NativeArray<long>(nodeCount, Allocator.Persistent);
            PendingUnits = new NativeArray<int>(cellCount, Allocator.Persistent);
            LastOffered = new NativeArray<int>(cellCount, Allocator.Persistent);
            for (int c = 0; c < cellCount; c++) LastOffered[c] = NeverOffered;
            SpentYear = new NativeArray<long>(nodeCount, Allocator.Persistent);
            CashYearStart = new NativeArray<long>(nodeCount, Allocator.Persistent);
            SavedYear = new NativeArray<long>(nodeCount, Allocator.Persistent);
        }

        /// <summary>What a node's households received since the year began: wages, dividends, public wages and the like.</summary>
        public long YearIncome(int node) => Cash[node] + Spent[node] - CashYearStart[node] + SpentYear[node] + SavedYear[node];

        /// <summary>Starts measuring a new year's income.</summary>
        public void StartYear()
        {
            for (int n = 0; n < Cash.Length; n++)
            {
                CashYearStart[n] = Cash[n] + Spent[n];
                SpentYear[n] = 0;
                SavedYear[n] = 0;
            }
        }

        /// <summary>
        /// The share (raw Q16, at most 1) of their wished necessities a node's households can pay for this week:
        /// they spend at most their cash over <paramref name="budgetWeeks"/> a week, necessities first (DEC-083).
        /// </summary>
        public long NecessityBudgetFactor(int node, int budgetWeeks) =>
            Share(Math.Max(0, Cash[node]) / Math.Max(1, budgetWeeks), DesiredNecessity[node]);

        /// <summary>The share of their wished luxuries that what is left after necessities pays for, so luxuries grow in as money does.</summary>
        public long LuxuryBudgetFactor(int node, int budgetWeeks) =>
            Share(Math.Max(0, Cash[node]) / Math.Max(1, budgetWeeks) - DesiredNecessity[node], Desired[node] - DesiredNecessity[node]);

        static long Share(long budget, long desired)
        {
            if (desired <= 0) return Fx.OneRaw;
            if (budget <= 0) return 0;
            if (desired <= budget) return Fx.OneRaw;
            return (budget << Fx.FractionBits) / desired;
        }

        public void Write(BinaryWriter writer)
        {
            NativeIO.WriteArray(writer, Cash);
            NativeIO.WriteArray(writer, Spent);
            NativeIO.WriteArray(writer, Desired);
            NativeIO.WriteArray(writer, PendingUnits);
            NativeIO.WriteArray(writer, SpentYear);
            NativeIO.WriteArray(writer, CashYearStart);
            NativeIO.WriteArray(writer, LastOffered);
            NativeIO.WriteArray(writer, DesiredNecessity);
            NativeIO.WriteArray(writer, SavedYear);
        }

        public void Read(BinaryReader reader)
        {
            NativeIO.ReadArray(reader, Cash);
            NativeIO.ReadArray(reader, Spent);
            NativeIO.ReadArray(reader, Desired);
            NativeIO.ReadArray(reader, PendingUnits);
            NativeIO.ReadArray(reader, SpentYear);
            NativeIO.ReadArray(reader, CashYearStart);
            NativeIO.ReadArray(reader, LastOffered);
            NativeIO.ReadArray(reader, DesiredNecessity);
            NativeIO.ReadArray(reader, SavedYear);
        }

        public void Dispose()
        {
            Cash.DisposeIfCreated();
            Spent.DisposeIfCreated();
            Desired.DisposeIfCreated();
            DesiredNecessity.DisposeIfCreated();
            PendingUnits.DisposeIfCreated();
            SpentYear.DisposeIfCreated();
            CashYearStart.DisposeIfCreated();
            SavedYear.DisposeIfCreated();
            LastOffered.DisposeIfCreated();
        }
    }

    /// <summary>Version 4 did not count lending: nothing was saved this year.</summary>
    public sealed class HouseholdCashV4Migration : ISaveMigration
    {
        public string SectionKey => HouseholdCash.SectionKey;
        public int FromVersion => 4;

        public byte[] Migrate(byte[] section)
        {
            int nodes = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(nodes);
                for (int n = 0; n < nodes; n++) writer.Write(0L);
            }
            return output.ToArray();
        }
    }

    /// <summary>Version 3 did not split what households wish for: necessities count as none until the next week measures them.</summary>
    public sealed class HouseholdCashV3Migration : ISaveMigration
    {
        public string SectionKey => HouseholdCash.SectionKey;
        public int FromVersion => 3;

        public byte[] Migrate(byte[] section)
        {
            int nodes = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(nodes);
                for (int n = 0; n < nodes; n++) writer.Write(0L);
            }
            return output.ToArray();
        }
    }

    /// <summary>Version 2 did not remember what markets offered: every good starts as never offered.</summary>
    public sealed class HouseholdCashV2Migration : ISaveMigration
    {
        public string SectionKey => HouseholdCash.SectionKey;
        public int FromVersion => 2;

        public byte[] Migrate(byte[] section)
        {
            var reader = new BinaryReader(new MemoryStream(section));
            for (int skipped = 0; skipped < 3; skipped++) reader.BaseStream.Seek(reader.ReadInt32() * (long)sizeof(long), SeekOrigin.Current);
            int cells = reader.ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(cells);
                for (int c = 0; c < cells; c++) writer.Write(HouseholdCash.NeverOffered);
            }
            return output.ToArray();
        }
    }

    /// <summary>Version 1 (P13b) did not measure income: the year starts at the saved cash.</summary>
    public sealed class HouseholdCashV1Migration : ISaveMigration
    {
        public string SectionKey => HouseholdCash.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            var reader = new BinaryReader(new MemoryStream(section));
            int nodes = reader.ReadInt32();
            var cash = reader.ReadBytes(nodes * sizeof(long));
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(nodes);
                writer.Write(new byte[nodes * sizeof(long)]);
                writer.Write(nodes);
                writer.Write(cash);
            }
            return output.ToArray();
        }
    }

    /// <summary>Household demand constants in raw Q16, passed by value into jobs.</summary>
    public struct DemandParams
    {
        public long LuxuryElasticityRaw;
        public long MaxCheapRaw;
        /// <summary>Per income quintile, poorest first: the necessity elasticity times the quintile's sensitivity.</summary>
        public long Quintile1Raw, Quintile2Raw, Quintile3Raw, Quintile4Raw, Quintile5Raw;

        public static DemandParams From(SimulationConfig c) => new DemandParams
        {
            LuxuryElasticityRaw = c.LuxuryElasticity.Raw,
            MaxCheapRaw = c.MaxCheapDemand.Raw,
            Quintile1Raw = QuintileElasticity(c, 0),
            Quintile2Raw = QuintileElasticity(c, 1),
            Quintile3Raw = QuintileElasticity(c, 2),
            Quintile4Raw = QuintileElasticity(c, 3),
            Quintile5Raw = QuintileElasticity(c, 4),
        };

        static long QuintileElasticity(SimulationConfig c, int q) =>
            (c.NecessityElasticity * DemandCurve.QuintileSensitivity(c.QuintileIncomeShare[q])).Raw;
    }

    /// <summary>What a cell's households want in a week: all units, necessity units at the anchor price, necessity units at this price.</summary>
    public struct WeeklyWish
    {
        public long Units, Wanted, Met;
    }

    /// <summary>
    /// One market cell's weekly household consumption (SYS-MARKETS, DEC-048 to DEC-050): the base demand
    /// (already weighted by level and modifiers) split into a necessity part, bought per head with each income
    /// quintile reacting to the price on its own, and a luxury part, bought in proportion to income. Both fall
    /// as the price rises above the anchor and vanish at the choke price, so a good nobody supplies cannot
    /// climb without end.
    /// </summary>
    public static class HouseholdDemand
    {
        public const int Quintiles = 5;

        /// <summary>
        /// The base weekly demand of a cell (level weight and demand modifier applied) and its anchor price
        /// (value modifier, price index and specialty applied), as the pricing job computes them.
        /// </summary>
        public static long CellBase(int baseDemand, int basePrice, long levelWeight, long demandMul, long valueMul, out long anchor)
        {
            anchor = Fx.RoundShift(basePrice * valueMul);
            return Fx.RoundShift(Fx.RoundShift(baseDemand * levelWeight) * demandMul);
        }

        /// <summary>What a node's households would take of a good in a week at its anchor price, whatever the budget.</summary>
        public static long AtAnchor(in DemandParams p, SimulationConfig config, HouseholdState households, int node, int level, GoodDef good, long priceIndex)
        {
            if (good.BaseDemand <= 0) return 0;
            long weekly = CellBase(good.BaseDemand, good.BasePrice, config.LevelDemandWeight[level].Raw, Fx.OneRaw, priceIndex, out long anchor);
            return Weekly(p, weekly, (int)Math.Min(anchor, int.MaxValue), anchor, config.LuxuryShare(good).Raw,
                households.NecessityWeight(node, level, config).Raw, households.LuxuryWeight(node, level, config).Raw).Units;
        }

        /// <param name="luxuryShare">How far the good is bought like a luxury (raw Q16, 0..1).</param>
        /// <param name="necessityWeight">The market households' necessity weight (raw Q16), see <see cref="HouseholdState"/>.</param>
        public static WeeklyWish Weekly(in DemandParams p, long weeklyAtAnchor, int price, long anchor,
            long luxuryShare, long necessityWeight, long luxuryWeight)
        {
            if (anchor < 1) anchor = 1;
            long excess = ((price - anchor) << Fx.FractionBits) / anchor;
            long necessityShare = Fx.OneRaw - luxuryShare;
            long necessity = 0;
            if (necessityShare > 0)
            {
                necessity = (DemandCurve.Factor(p.Quintile1Raw, excess, p.MaxCheapRaw)
                    + DemandCurve.Factor(p.Quintile2Raw, excess, p.MaxCheapRaw)
                    + DemandCurve.Factor(p.Quintile3Raw, excess, p.MaxCheapRaw)
                    + DemandCurve.Factor(p.Quintile4Raw, excess, p.MaxCheapRaw)
                    + DemandCurve.Factor(p.Quintile5Raw, excess, p.MaxCheapRaw)) / Quintiles;
            }
            long luxury = luxuryShare > 0 ? DemandCurve.Factor(p.LuxuryElasticityRaw, excess, p.MaxCheapRaw) : 0;

            long necessityAtAnchor = Fx.RoundShift(necessityShare * necessityWeight);
            long necessityPart = Fx.RoundShift(necessityAtAnchor * necessity);
            long luxuryPart = Fx.RoundShift(Fx.RoundShift(luxuryShare * luxuryWeight) * luxury);
            return new WeeklyWish
            {
                Wanted = Fx.RoundShift(weeklyAtAnchor * necessityAtAnchor),
                Met = Fx.RoundShift(weeklyAtAnchor * necessityPart),
                Units = Fx.RoundShift(weeklyAtAnchor * (necessityPart + luxuryPart)),
            };
        }
    }

    public static class DemandCurve
    {
        /// <summary>
        /// Quantity demanded relative to demand at the anchor price ([Draft] linear): falls by
        /// <paramref name="elasticityRaw"/> per 100% above the anchor, to nothing at the choke price, and rises
        /// the same way below it, up to <paramref name="maxRaw"/>. <paramref name="excessRaw"/> is
        /// price / anchor - 1, raw Fx.
        /// </summary>
        public static long Factor(long elasticityRaw, long excessRaw, long maxRaw)
        {
            long factor = Fx.OneRaw - Fx.RoundShift(elasticityRaw * excessRaw);
            if (factor < 0) return 0;
            return factor > maxRaw ? maxRaw : factor;
        }

        /// <summary>
        /// How much more price-sensitive a quintile is than the average household: the square root of the
        /// average share (one fifth) over its income share ([Draft]), so the poor give up necessities first.
        /// </summary>
        public static Fx QuintileSensitivity(Fx incomeShare) =>
            Sqrt(Fx.FromRaw(Fx.FromPermille(200).Raw * Fx.One.Raw / Math.Max(1, incomeShare.Raw)));

        static Fx Sqrt(Fx value)
        {
            ulong target = (ulong)Math.Max(0, value.Raw) << Fx.FractionBits;
            ulong root = 0, bit = 1UL << 62;
            while (bit > target) bit >>= 2;
            while (bit != 0)
            {
                if (target >= root + bit)
                {
                    target -= root + bit;
                    root = (root >> 1) + bit;
                }
                else root >>= 1;
                bit >>= 2;
            }
            return Fx.FromRaw((long)root);
        }
    }
}
