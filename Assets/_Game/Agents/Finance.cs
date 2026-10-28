using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Money held in each nation and continent (DEC-042): cash of the player, merchant groups, facilities
    /// and interest groups, counted where it is now, plus bank reserves. Deposits already sit in the
    /// reserves, so they are shown but not added again; loan balances are not money. Recomputed from the
    /// tables when a quarter is recorded or someone looks, so not saved.
    /// </summary>
    public sealed class MoneyTotals : IWorldComponent
    {
        /// <summary>Per nation index, in its continent's coin.</summary>
        public readonly long[] NationCash;
        public readonly long[] NationReserve;
        public readonly long[] NationDeposits;
        /// <summary>Per continent: every nation's total, the international bank, and coins held outside any nation.</summary>
        public readonly long[] ContinentTotal;
        /// <summary>Per continent: coins of that continent held outside its nations (carried abroad, or at nodes above nation level).</summary>
        public readonly long[] ContinentLoose;

        /// <summary>Per node: the nation index it lies in, or -1. Built on first use; the map is static.</summary>
        int[] _nationOfNode;
        int _computedTurn = -1;

        public MoneyTotals(int nationCount, int continentCount)
        {
            NationCash = new long[nationCount];
            NationReserve = new long[nationCount];
            NationDeposits = new long[nationCount];
            ContinentTotal = new long[continentCount];
            ContinentLoose = new long[continentCount];
        }

        public long NationTotal(int nation) => NationCash[nation] + NationReserve[nation];

        /// <summary>Recomputes unless already done this turn.</summary>
        public void Refresh(WorldState world)
        {
            if (_computedTurn != world.Get<SimClock>().Turn) Recompute(world);
        }

        public void Recompute(WorldState world)
        {
            _computedTurn = world.Get<SimClock>().Turn;
            var nodes = world.Get<NodeTable>();
            var banks = world.Get<NodeBanks>();
            var bank = world.Get<BankState>();
            if (_nationOfNode == null)
            {
                _nationOfNode = new int[nodes.Count];
                for (int node = 0; node < nodes.Count; node++)
                {
                    int nationNode = nodes.AncestorAt(node, WorldLevel.Nation);
                    _nationOfNode[node] = nationNode < 0 ? -1 : banks.NationIndex(nationNode);
                }
            }
            Array.Clear(NationCash, 0, NationCash.Length);
            Array.Clear(ContinentLoose, 0, ContinentLoose.Length);

            var player = world.Get<PlayerState>();
            for (int c = 0; c < player.Coins.Length; c++)
                AddCash(nodes, player.Node, c, player.Coins[c]);

            var facilities = world.Get<FacilityTable>();
            AddCash(nodes, facilities.Node.AsArray(), facilities.Cash.AsArray());
            var agents = world.Get<AgentTable>();
            AddCash(nodes, agents.Node.AsArray(), agents.Cash.AsArray());
            var groups = world.Get<GroupTable>();
            AddCash(nodes, groups.Node.AsArray(), groups.Cash.AsArray());

            // Households' and traders' purses are cash held there too (DEC-061).
            var purses = world.Get<PurseState>();
            for (int p = 0; p < purses.Count; p++)
            {
                if (purses.Purse[p] <= 0) continue;
                if (p < NationCash.Length) NationCash[p] += purses.Purse[p];
                else ContinentLoose[purses.ContinentOf(p)] += purses.Purse[p];
            }

            Array.Copy(bank.Reserve, NationReserve, NationReserve.Length);
            Array.Copy(bank.PlayerDeposit, NationDeposits, NationDeposits.Length);
            for (int c = 0; c < ContinentTotal.Length; c++) ContinentTotal[c] = ContinentLoose[c] + bank.InternationalReserve[c];
            for (int n = 0; n < NationCash.Length; n++) ContinentTotal[nodes.Continent[banks.NationNode(n)]] += NationTotal(n);
        }

        // Each holder's cash in its node's continent coin.
        void AddCash(NodeTable nodes, Unity.Collections.NativeArray<int> node, Unity.Collections.NativeArray<long> cash)
        {
            for (int i = 0; i < node.Length; i++)
            {
                if (cash[i] <= 0) continue;
                int at = node[i];
                AddCash(nodes, at, nodes.Continent[at], cash[i]);
            }
        }

        // Negative cash (upkeep arrears) is owed to nobody, so it is no money.
        void AddCash(NodeTable nodes, int node, int continent, long amount)
        {
            if (amount <= 0) return;
            int nation = _nationOfNode[node];
            if (nation >= 0 && nodes.Continent[node] == continent) NationCash[nation] += amount;
            else ContinentLoose[continent] += amount;
        }
    }

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
            return ev == FinancialEventKind.RateSpike ? rate * config.RateSpikeMultiplier : rate;
        }

        /// <summary>Interest for a whole term at a yearly rate, added up front (DEC-037).</summary>
        public static long Interest(Fx yearlyRate, long amount, int termWeeks) =>
            yearlyRate.MulInt(amount) * termWeeks / SimClock.WeeksPerYear;
    }

    /// <summary>Borrower ids of NPC loans: a facility, or a merchant group.</summary>
    public static class NpcBorrowers
    {
        const int MerchantBase = 1 << 28;

        public static int Facility(int facility) => facility;
        public static int Merchant(int agent) => MerchantBase + agent;

        public static bool IsMerchant(int borrower, out int agent)
        {
            agent = borrower - MerchantBase;
            return borrower >= MerchantBase;
        }
    }

    /// <summary>
    /// Loans of NPC facilities and merchant groups (SoA, DEC-037): one per borrower, in the lending nation's
    /// continent coin, repaid in weekly instalments from the borrower's cash.
    /// </summary>
    public sealed class NpcLoanTable : NativeTable
    {
        public readonly NativeColumn<int> Borrower;
        public readonly NativeColumn<int> Nation;
        /// <summary>Everything still owed, interest included.</summary>
        public readonly NativeColumn<long> Balance;
        public readonly NativeColumn<long> WeeklyPayment;
        /// <summary>Owed now: this week's instalment plus arrears.</summary>
        public readonly NativeColumn<long> Due;
        public readonly NativeColumn<int> OverdueWeeks;

        readonly Dictionary<int, int> _rowOf = new Dictionary<int, int>();
        bool _indexed;

        public override string Key => "agents.npc_loans";
        public override int Version => 1;

        public NpcLoanTable()
        {
            Borrower = Column<int>();
            Nation = Column<int>();
            Balance = Column<long>();
            WeeklyPayment = Column<long>();
            Due = Column<long>();
            OverdueWeeks = Column<int>();
        }

        protected override void OnRead() => _indexed = false;

        public int Find(int borrower)
        {
            if (!_indexed)
            {
                _rowOf.Clear();
                for (int r = 0; r < Count; r++) _rowOf[Borrower[r]] = r;
                _indexed = true;
            }
            return _rowOf.TryGetValue(borrower, out int row) ? row : -1;
        }

        public void Add(int borrower, int nation, long balance, long weekly)
        {
            Find(borrower); // builds the index before the row exists
            Borrower.Add(borrower);
            Nation.Add(nation);
            Balance.Add(balance);
            WeeklyPayment.Add(weekly);
            Due.Add(0);
            OverdueWeeks.Add(0);
            _rowOf[borrower] = Count - 1;
        }

        public void RemoveRow(int row)
        {
            Find(Borrower[row]);
            _rowOf.Remove(Borrower[row]);
            int last = Count - 1;
            if (row != last) _rowOf[Borrower[last]] = row;
            RemoveRowSwapBack(row);
        }

        /// <summary>Drops a borrower's loan, if any: what is still owed is lost to the bank.</summary>
        public void WriteOff(int borrower)
        {
            int row = Find(borrower);
            if (row >= 0) RemoveRow(row);
        }

    }

    public sealed class NpcLoanDefaulted : ISimEvent
    {
        /// <summary>The facility seized for auction, or none for a merchant group.</summary>
        public readonly FacilityId Facility;
        /// <summary>The merchant group whose cargo was seized, or none for a facility.</summary>
        public readonly AgentId Merchant;
        public readonly NodeId Nation;
        public readonly int Turn;

        public NpcLoanDefaulted(FacilityId facility, AgentId merchant, NodeId nation, int turn)
        {
            Facility = facility;
            Merchant = merchant;
            Nation = nation;
            Turn = turn;
        }
    }

    public sealed class FinancialEventStarted : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly FinancialEventKind Kind;
        public readonly int Turn;

        public FinancialEventStarted(NodeId nation, FinancialEventKind kind, int turn)
        {
            Nation = nation;
            Kind = kind;
            Turn = turn;
        }
    }

    public sealed class FinancialEventEnded : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly FinancialEventKind Kind;
        public readonly int Turn;

        public FinancialEventEnded(NodeId nation, FinancialEventKind kind, int turn)
        {
            Nation = nation;
            Kind = kind;
            Turn = turn;
        }
    }

    public sealed class StatisticsPublished : ISimEvent
    {
        public readonly int Year;

        public StatisticsPublished(int year) => Year = year;
    }

    /// <summary>NPC lending decisions shared by the bank and the systems whose borrowers go bankrupt.</summary>
    public static class NpcLending
    {
        /// <summary>
        /// Lends up to <paramref name="amount"/> to an NPC from its nation's bank, unless a credit crunch stops
        /// new loans (DEC-044): no more than the config share of the reserve (DEC-046). Returns what was lent.
        /// </summary>
        public static long Lend(WorldState world, SimulationConfig config, int borrower, int nation, long amount)
        {
            var finance = world.Get<FinanceState>();
            if (finance.EventOf(nation) == FinancialEventKind.CreditCrunch) return 0;
            var bank = world.Get<BankState>();
            amount = Math.Min(amount, config.NpcLoanMaxReserveShare.MulInt(Math.Max(0, bank.Reserve[nation])));
            if (amount <= 0) return 0;
            long balance = amount + FinanceRules.Interest(Fx.FromRaw(finance.LoanRate[nation]), amount, config.NpcLoanTermWeeks);
            long weekly = (balance + config.NpcLoanTermWeeks - 1) / config.NpcLoanTermWeeks;
            bank.Reserve[nation] -= amount;
            world.Get<NpcLoanTable>().Add(borrower, nation, balance, weekly);
            return amount;
        }

        /// <summary>
        /// The bank takes an NPC facility whose loan failed and auctions it (DEC-037, DEC-039); the loan is
        /// closed and the sale's proceeds go to the lending bank.
        /// </summary>
        public static void SeizeFacility(SimContext ctx, int facility)
        {
            var loans = ctx.World.Get<NpcLoanTable>();
            int row = loans.Find(NpcBorrowers.Facility(facility));
            int nation = loans.Nation[row];
            loans.RemoveRow(row);

            var db = ctx.Services.Get<GameDatabase>();
            var facilities = ctx.World.Get<FacilityTable>();
            facilities.Owner[facility] = OwnerIds.Bank;
            facilities.Mode[facility] = (byte)ControlMode.Automated;
            // The bank runs it until the sale without the old arrears, so it cannot go bankrupt meanwhile; the
            // arrears are written off against the purse there (DEC-061).
            if (facilities.Cash[facility] < 0) ctx.World.Get<PurseState>().Change(facilities.Node[facility], facilities.Cash[facility]);
            facilities.Cash[facility] = Math.Max(0, facilities.Cash[facility]);
            facilities.NegativeCashWeeks[facility] = 0;
            long reserve = db.Config.AuctionReserveShare.MulInt(FacilityValue(db, facilities, facility));
            ctx.World.Get<AuctionTable>().Add(facility, nation, ctx.Turn + db.Config.AuctionWeeks, reserve, forPlayerLoan: false);
            var nationNode = new NodeId(ctx.World.Get<NodeBanks>().NationNode(nation));
            ctx.Events.Publish(new NpcLoanDefaulted(new FacilityId(facility), AgentId.None, nationNode, ctx.Turn));
        }

        public static long FacilityValue(GameDatabase db, FacilityTable facilities, int f) =>
            db.Config.FacilityValue[(int)db.Facilities[facilities.DefIndex[f]].Tier] * facilities.Level[f];

        public static long FacilityUpkeep(GameDatabase db, FacilityTable facilities, int f) =>
            db.Config.FacilityUpkeep[(int)db.Facilities[facilities.DefIndex[f]].Tier] * facilities.Level[f];
    }

    /// <summary>
    /// Records, rates, statistics and financial events (Finance phase, after the banks): at each quarter's end
    /// the money totals are recorded (DEC-043), loan rates follow the reserve ratio (DEC-042) and events start or
    /// end (DEC-044, DEC-047); at each new year the statistics of the year before are published.
    /// </summary>
    public sealed class FinanceSystem : ISimSystem
    {
        static readonly uint EventStream = DetHash.Of("agents.finance.events");

        SimulationConfig _config;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        LoanTable _loans;
        NpcLoanTable _npcLoans;
        FinanceState _finance;
        MoneyTotals _totals;
        CurrencyState _currency;

        public string Id => "agents.finance";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 10;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _loans = ctx.World.Get<LoanTable>();
            _npcLoans = ctx.World.Get<NpcLoanTable>();
            _finance = ctx.World.Get<FinanceState>();
            _totals = ctx.World.Get<MoneyTotals>();
            _currency = ctx.World.Get<CurrencyState>();
        }

        public void Execute(SimContext ctx)
        {
            if (ctx.Clock.IsNewYear && ctx.Turn > 0) Publish(ctx);
            if (!ctx.Clock.IsQuarterEnd) return;

            _totals.Recompute(ctx.World);
            var record = Record(ctx.Turn);
            for (int n = 0; n < _finance.NationCount; n++)
            {
                UpdateEvent(ctx, n, record.ReserveRatio(n));
                SetRate(n, record.ReserveRatio(n));
            }
        }

        FinanceRecord Record(int turn)
        {
            var record = new FinanceRecord(_finance.NationCount, _finance.ContinentCount) { Turn = turn };
            for (int n = 0; n < _finance.NationCount; n++)
            {
                record.NationTotal[n] = _totals.NationTotal(n);
                record.NationCash[n] = _totals.NationCash[n];
                record.NationReserve[n] = _totals.NationReserve[n];
                record.NationDeposits[n] = _totals.NationDeposits[n];
            }
            for (int r = 0; r < _npcLoans.Count; r++) record.NationLent[_npcLoans.Nation[r]] += _npcLoans.Balance[r];
            for (int r = 0; r < _loans.Count; r++) record.NationLent[_loans.Nation[r]] += _loans.Balance[r];
            for (int c = 0; c < _finance.ContinentCount; c++)
            {
                record.ContinentTotal[c] = _totals.ContinentTotal[c];
                record.PriceIndex[c] = _currency.PriceIndex[c].Raw;
            }
            _finance.Records.Add(record);
            while (_finance.Records.Count > _config.FinanceHistoryQuarters) _finance.Records.RemoveAt(0);
            return record;
        }

        // An event lasts at least a quarter and ends once the reserve ratio has recovered (DEC-047); a nation
        // without one may draw a new one at random (DEC-044).
        void UpdateEvent(SimContext ctx, int n, Fx ratio)
        {
            var nation = new NodeId(_banks.NationNode(n));
            var current = _finance.EventOf(n);
            if (current != FinancialEventKind.None)
            {
                if (ctx.Turn - _finance.EventStart[n] < SimClock.WeeksPerQuarter || ratio < _config.FinancialEventEndReserveRatio) return;
                _finance.Event[n] = (byte)FinancialEventKind.None;
                ctx.Events.Publish(new FinancialEventEnded(nation, current, ctx.Turn));
                return;
            }
            var rng = DetRandom.For(ctx.Seed, ctx.Turn, EventStream, n);
            if (!rng.ChancePermille(_config.FinancialEventChancePermille)) return;
            var kind = (FinancialEventKind)(1 + rng.NextInt(3));
            _finance.Event[n] = (byte)kind;
            _finance.EventStart[n] = ctx.Turn;
            // [Draft] A run starts with the public taking a share of the bank's coins out.
            if (kind == FinancialEventKind.BankRun)
            {
                // The withdrawn coins go to the nation's households (DEC-061).
                long drain = _config.BankRunDrain.MulInt(Math.Max(0, _bank.Reserve[n]));
                _bank.Reserve[n] -= drain;
                ctx.World.Get<PurseState>().Purse[n] += drain;
            }
            ctx.Events.Publish(new FinancialEventStarted(nation, kind, ctx.Turn));
        }

        void SetRate(int n, Fx ratio) => _finance.LoanRate[n] = FinanceRules.LoanRate(_config, ratio, _finance.EventOf(n)).Raw;

        // The statistics of the year that just ended, from its last quarter's record (DEC-043, DEC-044).
        void Publish(SimContext ctx)
        {
            var record = _finance.Latest;
            if (record == null) return;
            _finance.PublishedYear = ctx.Clock.Year - 1;
            for (int c = 0; c < _finance.ContinentCount; c++)
            {
                _finance.PublishedContinentTotal[c] = record.ContinentTotal[c];
                var previous = Fx.FromRaw(_finance.PublishedPriceIndex[c]);
                var index = Fx.FromRaw(record.PriceIndex[c]);
                _finance.PublishedInflation[c] = (index / previous - Fx.One).Raw;
                _finance.PublishedPriceIndex[c] = index.Raw;
            }
            for (int n = 0; n < _finance.NationCount; n++)
            {
                _finance.PublishedNationTotal[n] = record.NationTotal[n];
                _finance.PublishedReserveRatio[n] = record.ReserveRatio(n).Raw;
                _finance.PublishedLoanRate[n] = _finance.LoanRate[n];
            }
            ctx.Events.Publish(new StatisticsPublished(_finance.PublishedYear));
        }
    }
}
