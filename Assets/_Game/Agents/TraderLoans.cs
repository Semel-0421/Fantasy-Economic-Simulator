using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>What each nation's traders owe for the money lent to their float (DEC-092).</summary>
    public sealed class TraderLoans : IPersistentComponent
    {
        /// <summary>Per node: lent by its households and not yet paid back, and the interest due on it.</summary>
        public readonly long[] OwedHouseholds, InterestHouseholds;
        /// <summary>Per nation: the same for its bank.</summary>
        public readonly long[] OwedBank, InterestBank;

        public string Key => "agents.trader_loans";
        public int Version => 1;

        public TraderLoans(int nodes, int nations)
        {
            OwedHouseholds = new long[nodes];
            InterestHouseholds = new long[nodes];
            OwedBank = new long[nations];
            InterestBank = new long[nations];
        }

        public void Write(BinaryWriter writer)
        {
            foreach (var column in new[] { OwedHouseholds, InterestHouseholds, OwedBank, InterestBank })
                foreach (long v in column) writer.Write(v);
        }

        public void Read(BinaryReader reader)
        {
            foreach (var column in new[] { OwedHouseholds, InterestHouseholds, OwedBank, InterestBank })
                for (int n = 0; n < column.Length; n++) column[n] = reader.ReadInt64();
        }
    }

    /// <summary>
    /// Sellers are paid from the traders' float, and households pay only for what reaches them, so an empty float
    /// stops goods while households sit on coins (DEC-092). Each week the float pays the simple interest and a part of
    /// each loan, as far as a share of it allows. While it keeps up, households lend part of what they can spare beyond
    /// their spending, and the bank part of its excess. Coins only move between purses. Off while no share is set.
    /// </summary>
    public sealed class TraderLoanSystem : ISimSystem
    {
        GameDatabase _db;
        TraderLoans _loans;
        PurseState _purses;
        HouseholdCash _cash;
        BankState _bank;
        NodeBanks _banks;
        WorldState _world;

        public string Id => "agents.trader_loans";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.TraderLoans;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _loans = ctx.World.Get<TraderLoans>();
            _purses = ctx.World.Get<PurseState>();
            _cash = ctx.World.Get<HouseholdCash>();
            _bank = ctx.World.Get<BankState>();
            _banks = ctx.World.Get<NodeBanks>();
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            for (int nation = 0; nation < _banks.NationCount; nation++)
                if (Repay(nation)) Lend(nation);
        }

        // Whether the float paid all that fell due.
        bool Repay(int nation)
        {
            var nodes = _purses.NationNodes[nation];
            long total = 0;
            foreach (int node in nodes) total += Accrue(ref _loans.OwedHouseholds[node], ref _loans.InterestHouseholds[node]);
            total += Accrue(ref _loans.OwedBank[nation], ref _loans.InterestBank[nation]);
            if (total <= 0) return true;
            long free = Math.Min(total, _db.Config.TraderLoanRepayShare.MulInt(Math.Max(0, _purses.Float[nation])));
            foreach (int node in nodes)
            {
                long owedBefore = _loans.OwedHouseholds[node];
                long paid = Pay(ref _loans.OwedHouseholds[node], ref _loans.InterestHouseholds[node], total, free);
                _cash.Cash[node] += paid;
                _cash.SavedYear[node] -= owedBefore - _loans.OwedHouseholds[node]; // interest is income, the rest was theirs
                _purses.Float[nation] -= paid;
            }
            long toBank = Pay(ref _loans.OwedBank[nation], ref _loans.InterestBank[nation], total, free);
            _bank.Reserve[nation] += toBank;
            _purses.Float[nation] -= toBank;
            return free >= total;
        }

        long Accrue(ref long owed, ref long interest)
        {
            interest += _db.Config.TraderLoanInterest.MulInt(owed) / SimClock.WeeksPerYear;
            return Due(owed, interest);
        }

        // A loan's week: the interest and a part of what is owed, at least a coin so it ends.
        long Due(long owed, long interest) => interest + (owed <= 0 ? 0 : Math.Max(1, owed / _db.Config.TraderLoanWeeks));

        // Its share of what the float can pay, interest first.
        long Pay(ref long owed, ref long interest, long total, long free)
        {
            long paid = Due(owed, interest) * free / total;
            long toInterest = Math.Min(paid, interest);
            interest -= toInterest;
            owed -= paid - toInterest;
            return paid;
        }

        void Lend(int nation)
        {
            var config = _db.Config;
            if (config.TraderLoanShare.Raw > 0)
                foreach (int node in _purses.NationNodes[nation])
                {
                    long spare = _cash.Cash[node] - config.TraderLoanKeepWeeks * _cash.Desired[node];
                    long lent = config.TraderLoanShare.MulInt(Math.Max(0, spare));
                    _cash.Cash[node] -= lent;
                    _cash.SavedYear[node] += lent;
                    _loans.OwedHouseholds[node] += lent;
                    _purses.Float[nation] += lent;
                }
            if (config.TraderBankLoanShare.Raw > 0)
            {
                long lent = config.TraderBankLoanShare.MulInt(Math.Max(0, MoneyLedger.StateExcess(_world, config, nation)));
                _bank.Reserve[nation] -= lent;
                _loans.OwedBank[nation] += lent;
                _purses.Float[nation] += lent;
            }
        }
    }
}
