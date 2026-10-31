using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>Policies interest groups move (DEC-058).</summary>
    public enum PolicyKind : byte
    {
        /// <summary>Every toll and specialty surcharge rate in the nation.</summary>
        Tolls = 0,
        /// <summary>The price of the nation's licenses.</summary>
        LicensePrice = 1,
        /// <summary>Which goods need a license in the nation: one good added or dropped per review.</summary>
        LicensedGoods = 2,
        /// <summary>The continent's minting rate.</summary>
        Minting = 3,
    }

    /// <summary>Money the player spent lobbying this year in the company's name (DEC-058).</summary>
    public struct LobbyEntry
    {
        public int Group;
        public int Nation;
        public PolicyKind Policy;
        /// <summary>For <see cref="PolicyKind.LicensedGoods"/>: the base good; otherwise -1.</summary>
        public int BaseGood;
        /// <summary>+1 pushes the policy up (higher tolls and prices, a good licensed, more minting), -1 down.</summary>
        public int Direction;
        public long Amount;
    }

    /// <summary>
    /// Policy as the yearly reviews left it (SYS-INTEREST-GROUPS, DEC-058): per nation the toll and license
    /// price steps and the licensed goods, per continent the minting step, plus what the review needs from
    /// one year to the next (each group's worth, this year's lobbying).
    /// </summary>
    public sealed class PolicyState : IPersistentComponent, IDisposable
    {
        public readonly int NationCount;
        public readonly int ContinentCount;
        public readonly int BaseGoodCount;
        public readonly int[] TollStep;
        public readonly int[] LicensePriceStep;
        public readonly int[] MintStep;
        /// <summary>
        /// Per (nation row, base good): 1 when selling it needs a license. Row <see cref="NationCount"/> holds
        /// goods.csv's flags for nodes outside any nation; it never changes.
        /// </summary>
        public NativeArray<byte> Licensed;
        /// <summary>Per group: cash of the group and everything it owns at the last review, for the loss tilt.</summary>
        public readonly List<long> LastWorth = new List<long>();
        public readonly List<LobbyEntry> Lobbying = new List<LobbyEntry>();
        /// <summary>Per nation: total influence at the last review.</summary>
        public readonly long[] LastInfluence;

        public string Key => "agents.policy";
        public int Version => 1;

        public PolicyState(int nationCount, int continentCount, GameDatabase db)
        {
            NationCount = nationCount;
            ContinentCount = continentCount;
            BaseGoodCount = db.BaseGoodCount;
            TollStep = new int[nationCount];
            LicensePriceStep = new int[nationCount];
            MintStep = new int[continentCount];
            LastInfluence = new long[nationCount];
            Licensed = new NativeArray<byte>((nationCount + 1) * BaseGoodCount, Allocator.Persistent);
            for (int row = 0; row <= nationCount; row++)
                for (int b = 0; b < BaseGoodCount; b++)
                    Licensed[row * BaseGoodCount + b] = (byte)(db.Good(GoodId.From(b, QualityGrade.Mid)).RequiresLicense ? 1 : 0);
        }

        /// <summary>The licensing row of a nation index, or of nodes outside any nation for -1.</summary>
        public int Row(int nation) => nation < 0 ? NationCount : nation;

        public bool Requires(int nation, int baseGood) => Licensed[Row(nation) * BaseGoodCount + baseGood] != 0;

        public void Write(BinaryWriter writer)
        {
            Write(writer, TollStep);
            Write(writer, LicensePriceStep);
            Write(writer, MintStep);
            NativeIO.WriteArray(writer, Licensed);
            writer.Write(LastWorth.Count);
            foreach (long worth in LastWorth) writer.Write(worth);
            writer.Write(Lobbying.Count);
            foreach (var e in Lobbying)
            {
                writer.Write(e.Group);
                writer.Write(e.Nation);
                writer.Write((byte)e.Policy);
                writer.Write(e.BaseGood);
                writer.Write(e.Direction);
                writer.Write(e.Amount);
            }
            writer.Write(LastInfluence.Length);
            foreach (long v in LastInfluence) writer.Write(v);
        }

        public void Read(BinaryReader reader)
        {
            Read(reader, TollStep);
            Read(reader, LicensePriceStep);
            Read(reader, MintStep);
            NativeIO.ReadArray(reader, Licensed);
            int groups = reader.ReadInt32();
            LastWorth.Clear();
            for (int g = 0; g < groups; g++) LastWorth.Add(reader.ReadInt64());
            int entries = reader.ReadInt32();
            Lobbying.Clear();
            for (int i = 0; i < entries; i++)
                Lobbying.Add(new LobbyEntry
                {
                    Group = reader.ReadInt32(),
                    Nation = reader.ReadInt32(),
                    Policy = (PolicyKind)reader.ReadByte(),
                    BaseGood = reader.ReadInt32(),
                    Direction = reader.ReadInt32(),
                    Amount = reader.ReadInt64(),
                });
            int nations = reader.ReadInt32();
            if (nations != LastInfluence.Length) throw new InvalidDataException($"Saved nation count {nations} != {LastInfluence.Length}.");
            for (int n = 0; n < nations; n++) LastInfluence[n] = reader.ReadInt64();
        }

        static void Write(BinaryWriter writer, int[] values)
        {
            writer.Write(values.Length);
            foreach (int v in values) writer.Write(v);
        }

        static void Read(BinaryReader reader, int[] values)
        {
            int count = reader.ReadInt32();
            if (count != values.Length) throw new InvalidDataException($"Saved policy count {count} != {values.Length}.");
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt32();
        }

        public void Dispose() => Licensed.DisposeIfCreated();
    }

    public static class PolicyRules
    {
        /// <summary>The multiplier a policy step gives tolls, license prices or minting: one step is the config share.</summary>
        public static Fx Scale(SimulationConfig config, int step) => Fx.One + config.PolicyStep * Fx.FromInt(step);

        /// <summary>Whether selling <paramref name="good"/> at <paramref name="node"/> needs a license there.</summary>
        public static bool NeedsLicense(WorldState world, GoodId good, int node) =>
            world.Get<PolicyState>().Requires(world.Get<NodeBanks>().NationOf(node), good.BaseIndex);
    }
}
