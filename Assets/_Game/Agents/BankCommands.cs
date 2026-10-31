using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    static class PlayerBank
    {
        /// <summary>The nation index of the bank branch where the player stands, or -1 with an error.</summary>
        public static int Branch(SimContext ctx, out string error)
        {
            var player = ctx.World.Get<PlayerState>();
            int nation = player.IsTravelling ? -1 : ctx.World.Get<NodeBanks>().BranchNation(player.Node);
            error = nation < 0 ? "There is no bank branch here." : null;
            return nation;
        }
    }

    /// <summary>
    /// Pays local coins into (positive amount) or takes them out of (negative) the player's account with the
    /// bank of the nation whose branch they are at. Deposits sit in the bank's reserve, so a withdrawal needs
    /// both the balance and the bank's coins.
    /// </summary>
    public sealed class BankTransferCommand : ICommand
    {
        public readonly long Amount;

        public BankTransferCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount == 0 || Amount == long.MinValue) return CommandResult.Fail("Amount must not be zero.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            var bank = ctx.World.Get<BankState>();
            if (Amount > 0) return ctx.World.Get<PlayerState>().LocalCoins >= Amount ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
            if (bank.PlayerDeposit[nation] < -Amount) return CommandResult.Fail("The account does not hold that much.");
            var finance = ctx.World.Get<FinanceState>();
            if (finance.EventOf(nation) == FinancialEventKind.BankRun)
            {
                // During a run the bank pays out a share of the deposit, once a week (DEC-044).
                if (finance.LastWithdrawTurn[nation] == ctx.Turn) return CommandResult.Fail("During a bank run, one withdrawal a week.");
                long cap = ctx.Services.Get<GameDatabase>().Config.BankRunWithdrawShare.MulInt(bank.PlayerDeposit[nation]);
                if (-Amount > cap) return CommandResult.Fail($"During a bank run, at most {cap} can be withdrawn.");
            }
            return bank.Reserve[nation] >= -Amount ? CommandResult.Success : CommandResult.Fail("The bank cannot pay that out now.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var bank = ctx.World.Get<BankState>();
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            bank.PlayerDeposit[nation] += Amount;
            bank.Reserve[nation] += Amount;
            if (Amount < 0) ctx.World.Get<FinanceState>().LastWithdrawTurn[nation] = ctx.Turn;
        }
    }

    public sealed class CoinsExchanged : ISimEvent
    {
        public readonly int FromContinent;
        public readonly int ToContinent;
        public readonly long Paid;
        public readonly long Received;
        public readonly long Fee;

        public CoinsExchanged(int fromContinent, int toContinent, long paid, long received, long fee)
        {
            FromContinent = fromContinent;
            ToContinent = toContinent;
            Paid = paid;
            Received = received;
            Fee = fee;
        }
    }

    /// <summary>
    /// Changes coins at the international bank of the continent the player is in (DEC-031, DEC-036): local
    /// coins into another continent's or back. The rate follows the price indices; the bank keeps a fee.
    /// </summary>
    public sealed class ExchangeCommand : ICommand
    {
        public readonly int FromContinent;
        public readonly int ToContinent;
        public readonly long Amount;

        public ExchangeCommand(int fromContinent, int toContinent, long amount)
        {
            FromContinent = fromContinent;
            ToContinent = toContinent;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            int continents = player.Coins.Length;
            if (FromContinent < 0 || FromContinent >= continents || ToContinent < 0 || ToContinent >= continents || FromContinent == ToContinent)
                return CommandResult.Fail("Pick two different currencies.");
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            if (player.IsTravelling || !ctx.World.Get<NodeBanks>().IsInternationalBank(player.Node))
                return CommandResult.Fail("Coins are exchanged only at an international bank.");
            if (FromContinent != player.LocalContinent && ToContinent != player.LocalContinent)
                return CommandResult.Fail("This bank changes only its own continent's coin.");
            if (player.Coins[FromContinent] < Amount) return CommandResult.Fail("Not enough coins.");
            long received = Quote(ctx, out _);
            if (received <= 0) return CommandResult.Fail("The amount is too small to exchange.");
            return ctx.World.Get<BankState>().InternationalReserve[ToContinent] >= received
                ? CommandResult.Success
                : CommandResult.Fail("The international banks hold too few of those coins now.");
        }

        // The coins handed in go to their continent's international bank and the coins handed out come from
        // theirs, so no continent's money changes (DEC-061); the fee stays with the banks.
        public void Apply(SimContext ctx)
        {
            long received = Quote(ctx, out long fee);
            var player = ctx.World.Get<PlayerState>();
            var bank = ctx.World.Get<BankState>();
            player.Coins[FromContinent] -= Amount;
            bank.InternationalReserve[FromContinent] += Amount;
            player.Coins[ToContinent] += received;
            bank.InternationalReserve[ToContinent] -= received;
            ctx.Events.Publish(new CoinsExchanged(FromContinent, ToContinent, Amount, received, fee));
        }

        long Quote(SimContext ctx, out long fee)
        {
            long gross = ctx.World.Get<CurrencyState>().Convert(Amount, FromContinent, ToContinent);
            fee = ctx.Services.Get<GameDatabase>().Config.ExchangeFee.MulInt(gross);
            return gross - fee;
        }
    }

    /// <summary>
    /// Borrows from the bank whose branch the player is at, in local coins: up to the credit limit on the
    /// collateral and the bank's reserve (DEC-037, DEC-046), unless a credit crunch stops lending. Interest at
    /// the bank's current rate for the term is added up front and the whole is repaid in equal weekly
    /// instalments. One loan per bank at a time.
    /// </summary>
    public sealed class TakeLoanCommand : ICommand
    {
        public readonly long Amount;

        public TakeLoanCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<LoanTable>().Find(nation) >= 0) return CommandResult.Fail("This bank's loan is not repaid yet.");
            if (ctx.World.Get<FinanceState>().EventOf(nation) == FinancialEventKind.CreditCrunch)
                return CommandResult.Fail("A credit crunch: the bank makes no new loans.");
            if (ctx.World.Get<BankState>().Reserve[nation] < Amount) return CommandResult.Fail("The bank has no coins left to lend.");
            var sheet = new PlayerBalanceSheet(ctx.World, ctx.Services.Get<GameDatabase>());
            return sheet.CreditLimit(ctx.World.Get<PlayerState>().LocalContinent) >= Amount
                ? CommandResult.Success
                : CommandResult.Fail("Not enough collateral.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var config = ctx.Services.Get<GameDatabase>().Config;
            var rate = Fx.FromRaw(ctx.World.Get<FinanceState>().LoanRate[nation]);
            long interest = FinanceRules.Interest(rate, Amount, config.LoanTermWeeks);
            long balance = Amount + interest;
            long weekly = (balance + config.LoanTermWeeks - 1) / config.LoanTermWeeks;
            ctx.World.Get<BankState>().Reserve[nation] -= Amount;
            ctx.World.Get<PlayerState>().LocalCoins += Amount;
            ctx.World.Get<LoanTable>().Add(nation, balance, weekly);
            ctx.Events.Publish(new LoanTaken(new NodeId(ctx.World.Get<NodeBanks>().NationNode(nation)), Amount, balance, weekly));
        }
    }

    /// <summary>Pays off part or all of the loan of the bank whose branch the player is at, from local coins.</summary>
    public sealed class RepayLoanCommand : ICommand
    {
        public readonly long Amount;

        public RepayLoanCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<LoanTable>().Find(nation) < 0) return CommandResult.Fail("No loan with this bank.");
            return ctx.World.Get<PlayerState>().LocalCoins >= Amount ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var loans = ctx.World.Get<LoanTable>();
            int r = loans.Find(nation);
            long paid = Math.Min(Amount, loans.Balance[r]);
            ctx.World.Get<PlayerState>().LocalCoins -= paid;
            loans.Balance[r] -= paid;
            loans.Due[r] = Math.Max(0, loans.Due[r] - paid);
            if (loans.Due[r] == 0) loans.OverdueWeeks[r] = 0;
            ctx.World.Get<BankState>().Reserve[nation] += paid;
            if (loans.Balance[r] > 0) return;
            loans.RemoveRow(r);
            ctx.Events.Publish(new LoanRepaid(new NodeId(ctx.World.Get<NodeBanks>().NationNode(nation)), ctx.Turn));
        }
    }

    /// <summary>
    /// Bids for a seized facility at auction (DEC-039). The bid is paid when the auction closes, from the
    /// coins of the facility's continent and then deposits there; a bid that cannot be paid then loses.
    /// </summary>
    public sealed class BidCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly long Amount;

        public BidCommand(FacilityId facility, long amount)
        {
            Facility = facility;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var auctions = ctx.World.Get<AuctionTable>();
            int a = auctions.Find(Facility);
            if (a < 0) return CommandResult.Fail("That facility is not up for auction.");
            if (Amount < auctions.ReservePrice[a]) return CommandResult.Fail("The bid is below the reserve price.");
            return Amount > auctions.PlayerBid[a] ? CommandResult.Success : CommandResult.Fail("The bid must beat the current one.");
        }

        public void Apply(SimContext ctx)
        {
            var auctions = ctx.World.Get<AuctionTable>();
            auctions.PlayerBid[auctions.Find(Facility)] = Amount;
        }
    }
}
