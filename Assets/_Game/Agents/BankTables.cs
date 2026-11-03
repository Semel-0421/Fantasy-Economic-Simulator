using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Money held by the state banks (SYS-BANKING): each nation's bank reserve and the player's deposit with
    /// it, in its continent's coin, plus each continent's international bank. Branches of one nation share
    /// one account and one reserve (DEC-032).
    /// </summary>
    public sealed class BankState : IPersistentComponent
    {
        /// <summary>Per nation index: the bank's own coins, deposits included. Loans are paid out of it (DEC-046).</summary>
        public readonly long[] Reserve;
        /// <summary>Per nation index: the player's deposit.</summary>
        public readonly long[] PlayerDeposit;
        /// <summary>Per continent: coins the international bank holds (minting remainders, exchange fees).</summary>
        public readonly long[] InternationalReserve;
        /// <summary>Per continent: coin supply already handed to the banks, so only newly minted coins are distributed.</summary>
        public readonly long[] DistributedSupply;

        /// <summary>A nation's reserve without the player's deposits, never below zero.</summary>
        public long Spendable(int nation) => Math.Max(0, Reserve[nation] - PlayerDeposit[nation]);

        /// <summary>What of the player's deposit the bank can pay out now: it may have lent the rest (DEC-107).</summary>
        public long PayableDeposit(int nation) => Math.Min(PlayerDeposit[nation], Math.Max(0, Reserve[nation]));

        public string Key => "agents.banks";
        public int Version => 1;

        public BankState(int nationCount, int continentCount)
        {
            Reserve = new long[nationCount];
            PlayerDeposit = new long[nationCount];
            InternationalReserve = new long[continentCount];
            DistributedSupply = new long[continentCount];
        }

        public void Write(BinaryWriter writer)
        {
            Write(writer, Reserve);
            Write(writer, PlayerDeposit);
            Write(writer, InternationalReserve);
            Write(writer, DistributedSupply);
        }

        public void Read(BinaryReader reader)
        {
            Read(reader, Reserve);
            Read(reader, PlayerDeposit);
            Read(reader, InternationalReserve);
            Read(reader, DistributedSupply);
        }

        static void Write(BinaryWriter writer, long[] values)
        {
            writer.Write(values.Length);
            foreach (long v in values) writer.Write(v);
        }

        static void Read(BinaryReader reader, long[] values)
        {
            int count = reader.ReadInt32();
            if (count != values.Length) throw new InvalidDataException($"Saved bank count {count} != {values.Length}.");
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt64();
        }
    }

    /// <summary>DEC-130: the turn the bank called for margin on the player's loans, or -1.</summary>
    public sealed class MarginState : IPersistentComponent
    {
        public int CalledTurn = -1;

        public string Key => "agents.margin";
        public int Version => 1;

        public void Write(BinaryWriter writer) => writer.Write(CalledTurn);

        public void Read(BinaryReader reader) => CalledTurn = reader.ReadInt32();
    }

    /// <summary>The player's loans (SoA), at most one per nation's bank, in that nation's continent coin.</summary>
    public sealed class LoanTable : NativeTable
    {
        public readonly NativeColumn<int> Nation;
        /// <summary>Everything still owed, interest included.</summary>
        public readonly NativeColumn<long> Balance;
        public readonly NativeColumn<long> WeeklyPayment;
        /// <summary>Owed now: this week's instalment plus arrears.</summary>
        public readonly NativeColumn<long> Due;
        /// <summary>Consecutive weeks that ended with something still due.</summary>
        public readonly NativeColumn<int> OverdueWeeks;

        public override string Key => "agents.loans";
        public override int Version => 1;

        public LoanTable()
        {
            Nation = Column<int>();
            Balance = Column<long>();
            WeeklyPayment = Column<long>();
            Due = Column<long>();
            OverdueWeeks = Column<int>();
        }

        public int Find(int nation)
        {
            for (int r = 0; r < Count; r++)
                if (Nation[r] == nation) return r;
            return -1;
        }

        public void Add(int nation, long balance, long weekly)
        {
            Nation.Add(nation);
            Balance.Add(balance);
            WeeklyPayment.Add(weekly);
            Due.Add(0);
            OverdueWeeks.Add(0);
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>Seized facilities up for auction (SoA, DEC-039). The facility belongs to the bank meanwhile.</summary>
    public sealed class AuctionTable : NativeTable
    {
        public readonly NativeColumn<int> Facility;
        /// <summary>Nation index of the bank selling it.</summary>
        public readonly NativeColumn<int> Nation;
        /// <summary>The auction closes at the end of this turn.</summary>
        public readonly NativeColumn<int> ClosesTurn;
        /// <summary>In the facility's continent coin; an NPC buys at this price when no bid wins.</summary>
        public readonly NativeColumn<long> ReservePrice;
        /// <summary>The player's bid, or 0.</summary>
        public readonly NativeColumn<long> PlayerBid;
        /// <summary>1 when the sale repays the player's loan; 0 for a facility seized from an NPC, whose loan is already closed.</summary>
        public readonly NativeColumn<byte> ForPlayerLoan;

        public const string SectionKey = "agents.auctions";
        public override string Key => SectionKey;
        public override int Version => 2;

        public AuctionTable()
        {
            Facility = Column<int>();
            Nation = Column<int>();
            ClosesTurn = Column<int>();
            ReservePrice = Column<long>();
            PlayerBid = Column<long>();
            ForPlayerLoan = Column<byte>();
        }

        public int Find(FacilityId facility)
        {
            for (int r = 0; r < Count; r++)
                if (Facility[r] == facility.Value) return r;
            return -1;
        }

        public void Add(int facility, int nation, int closesTurn, long reservePrice, bool forPlayerLoan = true)
        {
            Facility.Add(facility);
            Nation.Add(nation);
            ClosesTurn.Add(closesTurn);
            ReservePrice.Add(reservePrice);
            PlayerBid.Add(0);
            ForPlayerLoan.Add((byte)(forPlayerLoan ? 1 : 0));
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>Version 1 auctioned only the player's facilities.</summary>
    public sealed class AuctionTableV1Migration : ISaveMigration
    {
        public string SectionKey => AuctionTable.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write((byte)1);
            }
            return output.ToArray();
        }
    }

    public readonly struct LoanInfo
    {
        public readonly NodeId Nation;
        public readonly long Balance;
        public readonly long WeeklyPayment;
        public readonly long Due;
        public readonly int OverdueWeeks;

        public LoanInfo(NodeId nation, long balance, long weeklyPayment, long due, int overdueWeeks)
        {
            Nation = nation;
            Balance = balance;
            WeeklyPayment = weeklyPayment;
            Due = due;
            OverdueWeeks = overdueWeeks;
        }
    }

    public readonly struct AuctionInfo
    {
        public readonly FacilityId Facility;
        public readonly NodeId Nation;
        public readonly int ClosesTurn;
        public readonly long ReservePrice;
        public readonly long PlayerBid;

        public AuctionInfo(FacilityId facility, NodeId nation, int closesTurn, long reservePrice, long playerBid)
        {
            Facility = facility;
            Nation = nation;
            ClosesTurn = closesTurn;
            ReservePrice = reservePrice;
            PlayerBid = playerBid;
        }
    }
}
