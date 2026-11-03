using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public enum FinancialEventKind : byte
    {
        None = 0,
        /// <summary>The public withdraws part of the reserve; the player's withdrawals are limited.</summary>
        BankRun = 1,
        /// <summary>No new loans.</summary>
        CreditCrunch = 2,
        /// <summary>Loan rates are multiplied.</summary>
        RateSpike = 3,
    }

    /// <summary>One quarter's financial record (DEC-043). Arrays are per nation index or per continent.</summary>
    public sealed class FinanceRecord
    {
        public int Turn;
        public long[] NationTotal;
        public long[] NationCash;
        public long[] NationReserve;
        public long[] NationDeposits;
        /// <summary>Loan balances owed to the nation's bank, the player's and NPCs'.</summary>
        public long[] NationLent;
        public long[] ContinentTotal;
        /// <summary>Raw Fx.</summary>
        public long[] PriceIndex;

        public FinanceRecord(int nations, int continents)
        {
            NationTotal = new long[nations];
            NationCash = new long[nations];
            NationReserve = new long[nations];
            NationDeposits = new long[nations];
            NationLent = new long[nations];
            ContinentTotal = new long[continents];
            PriceIndex = new long[continents];
        }

        IEnumerable<long[]> Arrays()
        {
            yield return NationTotal;
            yield return NationCash;
            yield return NationReserve;
            yield return NationDeposits;
            yield return NationLent;
            yield return ContinentTotal;
            yield return PriceIndex;
        }

        public Fx ReserveRatio(int nation) => FinanceRules.ReserveRatio(NationReserve[nation], NationLent[nation]);

        public void Write(BinaryWriter writer)
        {
            writer.Write(Turn);
            foreach (var values in Arrays()) FinanceState.Write(writer, values);
        }

        public void Read(BinaryReader reader)
        {
            Turn = reader.ReadInt32();
            foreach (var values in Arrays()) FinanceState.Read(reader, values);
        }
    }

    /// <summary>
    /// The banks' financial standing (SYS-BANKING): each nation's loan rate and financial event, the quarterly
    /// records (DEC-043) and the statistics published each year, which the player and NPCs read alike.
    /// </summary>
    public sealed class FinanceState : IPersistentComponent
    {
        public readonly int NationCount;
        public readonly int ContinentCount;
        /// <summary>Per nation: the yearly loan rate, raw Fx. Set each quarter from the reserve ratio (DEC-042).</summary>
        public readonly long[] LoanRate;
        public readonly byte[] Event;
        public readonly int[] EventStart;
        /// <summary>Per nation: the turn of the player's last withdrawal, limited during a bank run.</summary>
        public readonly int[] LastWithdrawTurn;
        /// <summary>Oldest first, at most <see cref="SimulationConfig.FinanceHistoryQuarters"/>.</summary>
        public readonly List<FinanceRecord> Records = new List<FinanceRecord>();

        /// <summary>The year the published statistics cover, or -1 before the first publication.</summary>
        public int PublishedYear = -1;
        public readonly long[] PublishedContinentTotal;
        /// <summary>Raw Fx; 1.0 until the first publication, so the first inflation is measured from the start.</summary>
        public readonly long[] PublishedPriceIndex;
        /// <summary>Raw Fx: the price index's change over the published year.</summary>
        public readonly long[] PublishedInflation;
        public readonly long[] PublishedNationTotal;
        public readonly long[] PublishedReserveRatio;
        public readonly long[] PublishedLoanRate;

        public string Key => "agents.finance";
        public int Version => 1;

        public FinanceState(int nationCount, int continentCount, Fx baseRate)
        {
            NationCount = nationCount;
            ContinentCount = continentCount;
            LoanRate = new long[nationCount];
            Event = new byte[nationCount];
            EventStart = new int[nationCount];
            LastWithdrawTurn = new int[nationCount];
            for (int n = 0; n < nationCount; n++)
            {
                LoanRate[n] = baseRate.Raw;
                LastWithdrawTurn[n] = -1;
            }
            PublishedContinentTotal = new long[continentCount];
            PublishedPriceIndex = new long[continentCount];
            PublishedInflation = new long[continentCount];
            for (int c = 0; c < continentCount; c++) PublishedPriceIndex[c] = Fx.One.Raw;
            PublishedNationTotal = new long[nationCount];
            PublishedReserveRatio = new long[nationCount];
            PublishedLoanRate = new long[nationCount];
        }

        public FinancialEventKind EventOf(int nation) => (FinancialEventKind)Event[nation];

        public FinanceRecord Latest => Records.Count == 0 ? null : Records[Records.Count - 1];

        public void Write(BinaryWriter writer)
        {
            Write(writer, LoanRate);
            writer.Write(Event.Length);
            writer.Write(Event);
            Write(writer, EventStart);
            Write(writer, LastWithdrawTurn);
            writer.Write(Records.Count);
            foreach (var record in Records) record.Write(writer);
            writer.Write(PublishedYear);
            Write(writer, PublishedContinentTotal);
            Write(writer, PublishedPriceIndex);
            Write(writer, PublishedInflation);
            Write(writer, PublishedNationTotal);
            Write(writer, PublishedReserveRatio);
            Write(writer, PublishedLoanRate);
        }

        public void Read(BinaryReader reader)
        {
            Read(reader, LoanRate);
            int events = reader.ReadInt32();
            if (events != Event.Length) throw new InvalidDataException($"Saved nation count {events} != {Event.Length}.");
            reader.Read(Event, 0, events);
            Read(reader, EventStart);
            Read(reader, LastWithdrawTurn);
            int records = reader.ReadInt32();
            Records.Clear();
            for (int i = 0; i < records; i++)
            {
                var record = new FinanceRecord(NationCount, ContinentCount);
                record.Read(reader);
                Records.Add(record);
            }
            PublishedYear = reader.ReadInt32();
            Read(reader, PublishedContinentTotal);
            Read(reader, PublishedPriceIndex);
            Read(reader, PublishedInflation);
            Read(reader, PublishedNationTotal);
            Read(reader, PublishedReserveRatio);
            Read(reader, PublishedLoanRate);
        }

        internal static void Write(BinaryWriter writer, long[] values)
        {
            writer.Write(values.Length);
            foreach (long v in values) writer.Write(v);
        }

        internal static void Read(BinaryReader reader, long[] values)
        {
            int count = reader.ReadInt32();
            if (count != values.Length) throw new InvalidDataException($"Saved finance count {count} != {values.Length}.");
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt64();
        }

        static void Write(BinaryWriter writer, int[] values)
        {
            writer.Write(values.Length);
            foreach (int v in values) writer.Write(v);
        }

        static void Read(BinaryReader reader, int[] values)
        {
            int count = reader.ReadInt32();
            if (count != values.Length) throw new InvalidDataException($"Saved finance count {count} != {values.Length}.");
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt32();
        }
    }

    public static class FinanceRules
    {
        /// <summary>
        /// The share of a bank's funds still in its vault: reserve over reserve plus loans owed ([Draft]). 1.0
        /// for a bank that has lent nothing.
        /// </summary>
        public static Fx ReserveRatio(long reserve, long lent)
        {
            long funds = Math.Max(0, reserve) + Math.Max(0, lent);
            return funds == 0 ? Fx.One : Fx.FromRatio(Math.Max(0, reserve), funds);
        }

        /// <summary>
        /// The base rate, raised as the reserve ratio falls below the target (DEC-042) up to the config cap, and
        /// multiplied during a rate spike (DEC-044).
        /// </summary>
        public static Fx LoanRate(SimulationConfig config, Fx reserveRatio, FinancialEventKind ev)
        {
            var multiplier = Fx.One;
            if (reserveRatio < config.TargetReserveRatio)
                multiplier = reserveRatio.Raw <= 0 ? config.MaxLoanRateMultiplier : Fx.Min(config.TargetReserveRatio / reserveRatio, config.MaxLoanRateMultiplier);
            var rate = config.LoanInterestPerYear * multiplier;
            // A rate spike, and since DEC-134 a credit crunch, which follows a burst (DEC-131), lift the rate.
            bool lifted = ev == FinancialEventKind.RateSpike || (ev == FinancialEventKind.CreditCrunch && config.CrunchLiftsRates);
            return lifted ? rate * config.RateSpikeMultiplier : rate;
        }

        /// <summary>Interest for a whole term at a yearly rate, added up front (DEC-037).</summary>
        public static long Interest(Fx yearlyRate, long amount, int termWeeks) =>
            yearlyRate.MulInt(amount) * termWeeks / SimClock.WeeksPerYear;
    }
}
