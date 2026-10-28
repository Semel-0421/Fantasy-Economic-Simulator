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

        public string Key => "economy.households";
        public int Version => 1;

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
                IncomePerHead[n] = Fx.FromInt(config.DefaultIncomePerHead[level]).Raw;
            }
            foreach (var def in map.Nodes)
                if (def.Population >= 0 && nodes.TryFind(def.Key, out var node)) Population[node.Value] = def.Population;
        }

        /// <summary>Necessity demand relative to a default node of the level: population over the default.</summary>
        public Fx NecessityWeight(int node, int level, SimulationConfig config) =>
            Fx.FromRatio(Population[node], config.DefaultPopulation[level]);

        /// <summary>Luxury demand relative to a default node of the level: total income over the default.</summary>
        public Fx LuxuryWeight(int node, int level, SimulationConfig config) =>
            Fx.FromRatio(Population[node] * IncomePerHead[node],
                config.DefaultPopulation[level] * Fx.FromInt(config.DefaultIncomePerHead[level]).Raw);

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
        /// <summary>Per cell: household units consumed since the market last repriced, not yet paid for.</summary>
        public NativeArray<int> PendingUnits;

        public string Key => "economy.household_cash";
        public int Version => 1;

        public HouseholdCash(int nodeCount, int cellCount)
        {
            Cash = new NativeArray<long>(nodeCount, Allocator.Persistent);
            Spent = new NativeArray<long>(nodeCount, Allocator.Persistent);
            Desired = new NativeArray<long>(nodeCount, Allocator.Persistent);
            PendingUnits = new NativeArray<int>(cellCount, Allocator.Persistent);
        }

        /// <summary>
        /// The share (raw Q16, at most 1) of their wished consumption a node's households can pay for this week:
        /// they spend at most their cash over <paramref name="budgetWeeks"/> a week.
        /// </summary>
        public long BudgetFactor(int node, int budgetWeeks)
        {
            long budget = Math.Max(0, Cash[node]) / Math.Max(1, budgetWeeks);
            if (budget <= 0) return 0;
            long desired = Desired[node];
            if (desired <= budget) return Fx.OneRaw;
            return (budget << Fx.FractionBits) / desired;
        }

        public void Write(BinaryWriter writer)
        {
            NativeIO.WriteArray(writer, Cash);
            NativeIO.WriteArray(writer, Spent);
            NativeIO.WriteArray(writer, Desired);
            NativeIO.WriteArray(writer, PendingUnits);
        }

        public void Read(BinaryReader reader)
        {
            NativeIO.ReadArray(reader, Cash);
            NativeIO.ReadArray(reader, Spent);
            NativeIO.ReadArray(reader, Desired);
            NativeIO.ReadArray(reader, PendingUnits);
        }

        public void Dispose()
        {
            Cash.DisposeIfCreated();
            Spent.DisposeIfCreated();
            Desired.DisposeIfCreated();
            PendingUnits.DisposeIfCreated();
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

        /// <param name="luxuryShare">How far the good is bought like a luxury (raw Q16, 0..1).</param>
        /// <param name="necessityWeight">The market households' necessity weight (raw Q16), see <see cref="HouseholdState"/>.</param>
        /// <param name="wanted">Necessity units wanted at the anchor price.</param>
        /// <param name="met">Necessity units demanded at this price.</param>
        public static long Weekly(in DemandParams p, long weeklyAtAnchor, int price, long anchor,
            long luxuryShare, long necessityWeight, long luxuryWeight, out long wanted, out long met)
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
            wanted = Fx.RoundShift(weeklyAtAnchor * necessityAtAnchor);
            met = Fx.RoundShift(weeklyAtAnchor * necessityPart);
            return Fx.RoundShift(weeklyAtAnchor * (necessityPart + luxuryPart));
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
