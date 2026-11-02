using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
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
            // The bank runs it until the sale without the old arrears; the arrears are wages and bills the households there never get (DEC-061).
            if (facilities.Cash[facility] < 0) MoneyLedger.PayLocalHouseholds(ctx.World, facilities.Node[facility], facilities.Cash[facility]);
            facilities.Cash[facility] = Math.Max(0, facilities.Cash[facility]);
            ctx.World.Get<IncomeTaxState>().RestartFacility(facility, facilities.Cash[facility]);
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
}
