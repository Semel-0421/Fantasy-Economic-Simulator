using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>The player bought (positive) or sold (negative) bonds of an issuer.</summary>
    public sealed class BondTraded : ISimEvent
    {
        public readonly int Issuer;
        public readonly long Face;

        public BondTraded(int issuer, long face)
        {
            Issuer = issuer;
            Face = face;
        }
    }

    static class PlayerBondDesk
    {
        /// <summary>The nation an issuer's bonds trade in: its own, or that of the group's base; -1 for a group the player may not deal with.</summary>
        public static int NationOf(SimContext ctx, int issuer)
        {
            var purses = ctx.World.Get<PurseState>();
            if (BondTable.IsState(issuer)) return ~issuer < purses.NationCount ? ~issuer : -1;
            var groups = ctx.World.Get<GroupTable>();
            if (issuer >= groups.Count || groups.IsPlayer[issuer] != 0) return -1;
            int nation = purses.NodeFloat[groups.Node[issuer]];
            return nation < purses.NationCount ? nation : -1;
        }

        public static string Check(SimContext ctx, int issuer, long amount, out int nation)
        {
            nation = NationOf(ctx, issuer);
            if (amount <= 0) return "Amount must be positive.";
            if (nation < 0) return "Those bonds are not for sale.";
            int branch = PlayerBank.Branch(ctx, out var error);
            if (error != null) return error;
            return branch == nation ? null : "Those bonds trade at the issuer's own bank.";
        }
    }

    /// <summary>
    /// The player buys new bonds at the issuer's bank (DEC-102): the coins go where a sale's would, to the towns for a
    /// state and to the group for a company. Bonds are the only credit open to the player; bills are not.
    /// </summary>
    public sealed class BuyBondCommand : ICommand
    {
        public readonly int Issuer;
        public readonly long Amount;

        public BuyBondCommand(int issuer, long amount)
        {
            Issuer = issuer;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerBondDesk.Check(ctx, Issuer, Amount, out int nation);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<PlayerState>().LocalCoins < Amount) return CommandResult.Fail("Not enough cash.");
            if (ctx.World.Get<FinanceState>().EventOf(nation) == FinancialEventKind.CreditCrunch) return CommandResult.Fail("No bonds are sold in a credit crunch.");
            var db = ctx.Services.Get<GameDatabase>();
            long room = BondTable.IsState(Issuer) ? BondDesk.StateRoom(ctx.World, db.Config, nation) : BondDesk.CompanyRoom(ctx.World, db, Issuer);
            return Amount <= room ? CommandResult.Success : CommandResult.Fail($"The issuer may owe only {room} more.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBondDesk.NationOf(ctx, Issuer);
            var db = ctx.Services.Get<GameDatabase>();
            var stats = ctx.World.Get<CreditStats>();
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            ctx.World.Get<BondTable>().Add(Issuer, BondTable.PlayerHolder, Amount);
            if (BondTable.IsState(Issuer))
            {
                MoneyLedger.PayNationTownWages(ctx.World, db.Config, nation, Amount);
                stats.StateRaised += Amount;
            }
            else
            {
                ctx.World.Get<GroupTable>().Cash[Issuer] += Amount;
                stats.CompanyRaised += Amount;
            }
            ctx.Events.Publish(new BondTraded(Issuer, Amount));
        }
    }

    /// <summary>The player sells bonds at their face to whoever at the issuer's bank has idle money this week, as far as they do (DEC-102).</summary>
    public sealed class SellBondCommand : ICommand
    {
        public readonly int Issuer;
        public readonly long Amount;

        public SellBondCommand(int issuer, long amount)
        {
            Issuer = issuer;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerBondDesk.Check(ctx, Issuer, Amount, out int nation);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<BondTable>().Held(Issuer, BondTable.PlayerHolder) < Amount) return CommandResult.Fail("You do not hold that much.");
            long offered = BondDesk.Offered(ctx.World, ctx.Services.Get<GameDatabase>().Config, ctx.Turn, nation, BondTable.IsState(Issuer) ? -1 : Issuer);
            return offered > 0 ? CommandResult.Success : CommandResult.Fail("No one is buying this week.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBondDesk.NationOf(ctx, Issuer);
            long sold = BondDesk.Raise(ctx.World, ctx.Services.Get<GameDatabase>().Config, ctx.Turn, Issuer, nation, Amount);
            ctx.World.Get<BondTable>().Reduce(Issuer, BondTable.PlayerHolder, sold);
            ctx.World.Get<PlayerState>().LocalCoins += sold;
            ctx.Events.Publish(new BondTraded(Issuer, -sold));
        }
    }
}
