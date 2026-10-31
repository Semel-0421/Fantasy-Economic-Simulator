using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    public enum MerchantPersonality : byte
    {
        Cautious = 0,
        Normal = 1,
        Aggressive = 2,
    }

    public enum MerchantOccupation : byte
    {
        Local = 0,
        Expedition = 1,
        Peddler = 2,
    }

    /// <summary>A group's trait packs its personality and occupation: nine combinations (reference specification).</summary>
    public static class MerchantTraits
    {
        public const int Count = 9;
        public const int Personalities = 3;
        /// <summary>Behaves as groups did before traits; a group from an older save has it.</summary>
        public const byte Neutral = (byte)MerchantPersonality.Normal * Personalities + (byte)MerchantOccupation.Expedition;

        public static byte Of(MerchantPersonality personality, MerchantOccupation occupation) =>
            (byte)((int)personality * Personalities + (int)occupation);

        public static MerchantPersonality PersonalityOf(int trait) => (MerchantPersonality)(trait / Personalities);
        public static MerchantOccupation OccupationOf(int trait) => (MerchantOccupation)(trait % Personalities);
    }

    /// <summary>The nodes a group has seen or heard about, at most four, oldest replaced first (reference specification).</summary>
    public struct NodeMemory
    {
        public const int Slots = 4;
        public int A, B, C, D;
        public int Next;

        public static NodeMemory Empty => new NodeMemory { A = -1, B = -1, C = -1, D = -1 };

        public int this[int slot] => slot == 0 ? A : slot == 1 ? B : slot == 2 ? C : D;

        public bool Knows(int node) => A == node || B == node || C == node || D == node;

        public void Learn(int node)
        {
            if (node < 0 || Knows(node)) return;
            if (Next == 0) A = node;
            else if (Next == 1) B = node;
            else if (Next == 2) C = node;
            else D = node;
            Next = (Next + 1) % Slots;
        }
    }

    /// <summary>Per-trait behaviour constants, in raw Q16, for jobs. All neutral when traits are off.</summary>
    public sealed class MerchantTraitTable : IDisposable
    {
        /// <summary>Share of its cash a group risks on one load.</summary>
        public NativeArray<long> SpendRaw;
        /// <summary>Margin, as a share of the buying price, below which a group does not trade.</summary>
        public NativeArray<long> MinMarginRaw;
        public NativeArray<long> CapacityRaw;
        public NativeArray<long> UpkeepRaw;
        /// <summary>A load's profit is divided by the route's travel turns to this power when choosing.</summary>
        public NativeArray<int> DistancePower;
        /// <summary>The world level whose ancestor of the group's home bounds where it trades.</summary>
        public NativeArray<int> RangeLevel;

        public MerchantTraitTable(SimulationConfig config)
        {
            SpendRaw = new NativeArray<long>(MerchantTraits.Count, Allocator.Persistent);
            MinMarginRaw = new NativeArray<long>(MerchantTraits.Count, Allocator.Persistent);
            CapacityRaw = new NativeArray<long>(MerchantTraits.Count, Allocator.Persistent);
            UpkeepRaw = new NativeArray<long>(MerchantTraits.Count, Allocator.Persistent);
            DistancePower = new NativeArray<int>(MerchantTraits.Count, Allocator.Persistent);
            RangeLevel = new NativeArray<int>(MerchantTraits.Count, Allocator.Persistent);
            for (int t = 0; t < MerchantTraits.Count; t++)
            {
                int personality = (int)MerchantTraits.PersonalityOf(t), occupation = (int)MerchantTraits.OccupationOf(t);
                bool on = config.MerchantTraitsEnabled;
                SpendRaw[t] = on ? config.PersonalitySpend[personality].Raw : Fx.OneRaw;
                MinMarginRaw[t] = on ? config.PersonalityMinMargin[personality].Raw : 0;
                CapacityRaw[t] = on ? config.OccupationCapacity[occupation].Raw : Fx.OneRaw;
                UpkeepRaw[t] = on ? config.OccupationUpkeep[occupation].Raw : Fx.OneRaw;
                DistancePower[t] = on ? config.OccupationDistancePower[occupation] : 0;
                RangeLevel[t] = (int)(on ? config.PersonalityRange[personality] : WorldLevel.Continent);
            }
        }

        public void Dispose()
        {
            SpendRaw.DisposeIfCreated();
            MinMarginRaw.DisposeIfCreated();
            CapacityRaw.DisposeIfCreated();
            UpkeepRaw.DisposeIfCreated();
            DistancePower.DisposeIfCreated();
            RangeLevel.DisposeIfCreated();
        }
    }

    /// <summary>
    /// How likely each trait is in a continent's next generation of merchant groups, and the year's bankruptcies
    /// per trait that help set it (reference specification, [Draft]). Starts uniform.
    /// </summary>
    public sealed class MerchantGenetics : IPersistentComponent
    {
        public readonly int ContinentCount;
        /// <summary>Raw Fx, indexed continent * traits + trait; a continent's weights sum to about one.</summary>
        public readonly long[] Weight;
        public readonly int[] YearBankruptcies;

        public string Key => "agents.merchant_genetics";
        public int Version => 1;

        public MerchantGenetics(int continentCount)
        {
            ContinentCount = continentCount;
            Weight = new long[continentCount * MerchantTraits.Count];
            YearBankruptcies = new int[continentCount * MerchantTraits.Count];
            for (int i = 0; i < Weight.Length; i++) Weight[i] = Fx.OneRaw / MerchantTraits.Count;
        }

        public void RecordBankruptcy(int continent, int trait) => YearBankruptcies[continent * MerchantTraits.Count + trait]++;

        /// <summary>A new group's trait: any at random with the mutation chance, else by the continent's weights.</summary>
        public byte Draw(ref DetRandom rng, int continent, Fx mutation)
        {
            if (rng.NextFx01().Raw < mutation.Raw) return (byte)rng.NextInt(MerchantTraits.Count);
            long total = 0;
            int first = continent * MerchantTraits.Count;
            for (int t = 0; t < MerchantTraits.Count; t++) total += Weight[first + t];
            if (total <= 0) return (byte)rng.NextInt(MerchantTraits.Count);
            long pick = (long)(rng.NextUInt() % (ulong)total);
            for (int t = 0; t < MerchantTraits.Count; t++)
            {
                pick -= Weight[first + t];
                if (pick < 0) return (byte)t;
            }
            return MerchantTraits.Count - 1;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(ContinentCount);
            foreach (long w in Weight) writer.Write(w);
            foreach (int b in YearBankruptcies) writer.Write(b);
        }

        public void Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != ContinentCount) throw new InvalidDataException("Saved continent count differs.");
            for (int i = 0; i < Weight.Length; i++) Weight[i] = reader.ReadInt64();
            for (int i = 0; i < YearBankruptcies.Length; i++) YearBankruptcies[i] = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Once a year each continent scores the traits of its active groups and sets the next generation's weights,
    /// then a share of its groups hand over to a successor of a newly drawn trait, keeping cash, cargo and place
    /// (reference specification, [Draft]). Fitness: 70% capital relative to the continent's average (coins differ
    /// and inflate, so absolute capital cannot be compared; at most twice the average counts), 30% the year's
    /// survival. Only groups stepped one by one earn a score: an aggregate group's cash does not move.
    /// </summary>
    public sealed class MerchantGenerationSystem : ISimSystem
    {
        static readonly uint Stream = DetHash.Of("agents.merchant_generation");

        SimulationConfig _config;
        NodeTable _nodes;
        AgentTable _agents;
        LodState _lod;
        MerchantGenetics _genetics;
        readonly long[] _capital = new long[MerchantTraits.Count];
        readonly int[] _count = new int[MerchantTraits.Count];

        public string Id => "agents.merchant_generation";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 40;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _agents = ctx.World.Get<AgentTable>();
            _lod = ctx.World.Get<LodState>();
            _genetics = ctx.World.Get<MerchantGenetics>();
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0 || !_config.MerchantTraitsEnabled) return;
            for (int c = 0; c < _genetics.ContinentCount; c++) Score(c);
            if (_config.MerchantGenerationShare.Raw <= 0) return;
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Owner[a] != OwnerIds.Npc) continue;
                var rng = DetRandom.For(ctx.Seed, ctx.Turn, Stream, a);
                if (rng.NextFx01().Raw >= _config.MerchantGenerationShare.Raw) continue;
                _agents.Trait[a] = _genetics.Draw(ref rng, _nodes.Continent[_agents.Home[a]], _config.MerchantMutationShare);
                _agents.Memory[a] = NodeMemory.Empty;
            }
        }

        void Score(int continent)
        {
            Array.Clear(_capital, 0, _capital.Length);
            Array.Clear(_count, 0, _count.Length);
            long capital = 0;
            int count = 0;
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_nodes.Continent[_agents.Home[a]] != continent) continue;
                if (!_agents.IsTravelling(a) && !_lod.IsDetailed(_agents.Node[a]) && !_lod.IsDetailed(_agents.Home[a])) continue;
                long cash = Math.Max(0, _agents.Cash[a]);
                _capital[_agents.Trait[a]] += cash;
                _count[_agents.Trait[a]]++;
                capital += cash;
                count++;
            }
            int first = continent * MerchantTraits.Count;
            if (count > 0 && capital > 0)
            {
                long average = capital / count;
                long total = 0;
                var fitness = new long[MerchantTraits.Count];
                for (int t = 0; t < MerchantTraits.Count; t++)
                {
                    // A trait nobody active has keeps an average score, so it can come back.
                    long relative = Fx.OneRaw / 2, survival = Fx.OneRaw;
                    if (_count[t] > 0)
                    {
                        relative = Math.Min(Fx.OneRaw, (_capital[t] / _count[t] << Fx.FractionBits) / Math.Max(1, average) / 2);
                        survival = Math.Max(0, Fx.OneRaw - ((long)_genetics.YearBankruptcies[first + t] << Fx.FractionBits) / _count[t]);
                    }
                    fitness[t] = (_config.MerchantFitnessCapital.Raw * relative >> Fx.FractionBits)
                        + ((Fx.OneRaw - _config.MerchantFitnessCapital.Raw) * survival >> Fx.FractionBits);
                    total += fitness[t];
                }
                for (int t = 0; total > 0 && t < MerchantTraits.Count; t++)
                    _genetics.Weight[first + t] = (fitness[t] << Fx.FractionBits) / total;
            }
            for (int t = 0; t < MerchantTraits.Count; t++) _genetics.YearBankruptcies[first + t] = 0;
        }
    }

    /// <summary>Version 3 had no traits or memory: groups behave as before and know nothing yet.</summary>
    public sealed class AgentTableV3Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 3;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(MerchantTraits.Neutral);
                writer.Write(rows);
                for (int r = 0; r < rows; r++)
                {
                    for (int s = 0; s < NodeMemory.Slots; s++) writer.Write(-1);
                    writer.Write(0); // Next
                }
            }
            return output.ToArray();
        }
    }
}
