using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Trade licenses held by the player (SoA, SYS-LICENSES). One row per (base good, nation); a license
    /// covers every grade of the good and is valid inside the issuing nation until <see cref="ExpiresTurn"/>.
    /// NPC merchant groups are not stored here: whether a group holds licenses follows from the seed
    /// (<see cref="LicenseRules.MerchantHoldsLicenses"/>).
    /// </summary>
    public sealed class LicenseTable : NativeTable
    {
        public readonly NativeColumn<int> BaseGood;
        /// <summary>Node index of the issuing nation.</summary>
        public readonly NativeColumn<int> Nation;
        /// <summary>First turn on which the license is no longer valid.</summary>
        public readonly NativeColumn<int> ExpiresTurn;

        public override string Key => "agents.licenses";
        public override int Version => 1;

        public LicenseTable()
        {
            BaseGood = Column<int>();
            Nation = Column<int>();
            ExpiresTurn = Column<int>();
        }

        public int Find(int baseGood, int nation)
        {
            for (int r = 0; r < Count; r++)
                if (BaseGood[r] == baseGood && Nation[r] == nation) return r;
            return -1;
        }

        public bool IsValid(int baseGood, int nation, int turn)
        {
            int row = Find(baseGood, nation);
            return row >= 0 && turn < ExpiresTurn[row];
        }

        /// <summary>Adds a year of validity, counted from the current expiry when still valid (renewal).</summary>
        public void Extend(int baseGood, int nation, int turn)
        {
            int row = Find(baseGood, nation);
            if (row < 0)
            {
                BaseGood.Add(baseGood);
                Nation.Add(nation);
                ExpiresTurn.Add(turn + SimClock.WeeksPerYear);
                return;
            }
            ExpiresTurn[row] = System.Math.Max(turn, ExpiresTurn[row]) + SimClock.WeeksPerYear;
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    public readonly struct LicenseInfo
    {
        public readonly int BaseGood;
        public readonly NodeId Nation;
        public readonly int ExpiresTurn;

        public LicenseInfo(int baseGood, NodeId nation, int expiresTurn)
        {
            BaseGood = baseGood;
            Nation = nation;
            ExpiresTurn = expiresTurn;
        }
    }

    public static class LicenseRules
    {
        static readonly uint MerchantStream = DetHash.Of("agents.licenses.merchants");

        /// <summary>A license's price, in the issuing nation's coin: the good's Mid base price times the config factor.</summary>
        public static long Price(GameDatabase db, int baseGood) =>
            db.Config.LicensePriceMultiplier.MulInt(db.Good(GoodId.From(baseGood, QualityGrade.Mid)).BasePrice);

        /// <summary>
        /// What the player pays in <paramref name="nationNode"/>: the price at the nation's license price policy
        /// (DEC-058); members of a guild of the good's trade in that nation get its recommendation and a
        /// discount (DEC-057).
        /// </summary>
        public static long PlayerPrice(WorldState world, GameDatabase db, int baseGood, int nationNode, int turn)
        {
            int nation = world.Get<NodeBanks>().NationIndex(nationNode);
            long price = PolicyRules.Scale(db.Config, world.Get<PolicyState>().LicensePriceStep[nation]).MulInt(Price(db, baseGood));
            int category = db.Good(GoodId.From(baseGood, QualityGrade.Mid)).CategoryIndex;
            return GuildBenefits.MemberOfTrade(world, db, nationNode, category, turn)
                ? price - db.Config.GuildLicenseDiscount.MulInt(price)
                : price;
        }

        /// <summary>Whether the player may sell <paramref name="good"/> at <paramref name="node"/> this turn (DEC-024, DEC-058).</summary>
        public static bool PlayerMaySell(WorldState world, GameDatabase db, GoodId good, int node, int turn)
        {
            if (!PolicyRules.NeedsLicense(world, good, node)) return true;
            int nation = world.Get<NodeTable>().AncestorAt(node, WorldLevel.Nation);
            return nation >= 0 && world.Get<LicenseTable>().IsValid(good.BaseIndex, nation, turn);
        }

        /// <summary>
        /// Whether a merchant group holds licenses (DEC-024): the share in config, picked from the seed. A
        /// licensed group holds every license of the nation it trades in, renewed without cost tracking.
        /// </summary>
        public static bool MerchantHoldsLicenses(ulong seed, AgentId merchant, int sharePermille) =>
            DetRandom.For(seed, 0, MerchantStream, merchant.Value).ChancePermille(sharePermille);
    }

    public sealed class LicenseBought : ISimEvent
    {
        public readonly int BaseGood;
        public readonly NodeId Nation;
        public readonly int ExpiresTurn;
        public readonly long Price;

        public LicenseBought(int baseGood, NodeId nation, int expiresTurn, long price)
        {
            BaseGood = baseGood;
            Nation = nation;
            ExpiresTurn = expiresTurn;
            Price = price;
        }
    }

    public sealed class LicenseExpired : ISimEvent
    {
        public readonly int BaseGood;
        public readonly NodeId Nation;
        public readonly int Turn;

        public LicenseExpired(int baseGood, NodeId nation, int turn)
        {
            BaseGood = baseGood;
            Nation = nation;
            Turn = turn;
        }
    }

    /// <summary>
    /// Buys, or renews for another year, the license for a good from the nation the player is in, paid in
    /// local coins. Signals a trade direction.
    /// </summary>
    public sealed class BuyLicenseCommand : ICommand, IGuidanceSource
    {
        public readonly GoodId Good;

        public BuyLicenseCommand(GoodId good) => Good = good;

        public CommandResult Validate(SimContext ctx)
        {
            var db = ctx.Services.Get<GameDatabase>();
            if (!Good.IsValid || Good.Value >= db.GoodCount) return CommandResult.Fail("Unknown good.");
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("Licenses are bought in a nation, not on the road.");
            if (Nation(ctx) < 0) return CommandResult.Fail("No nation issues licenses here.");
            if (!PolicyRules.NeedsLicense(ctx.World, Good, player.Node)) return CommandResult.Fail("This good needs no license here.");
            return player.LocalCoins >= Price(ctx) ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            long price = Price(ctx);
            int nation = Nation(ctx);
            var licenses = ctx.World.Get<LicenseTable>();
            ctx.World.Get<PlayerState>().LocalCoins -= price;
            MoneyLedger.ToState(ctx.World, nation, price); // license money goes to the nation (DEC-061)
            licenses.Extend(Good.BaseIndex, nation, ctx.Turn);
            int expires = licenses.ExpiresTurn[licenses.Find(Good.BaseIndex, nation)];
            ctx.Events.Publish(new LicenseBought(Good.BaseIndex, new NodeId(nation), expires, price));
        }

        int Nation(SimContext ctx) => ctx.World.Get<NodeTable>().AncestorAt(ctx.World.Get<PlayerState>().Node, WorldLevel.Nation);

        long Price(SimContext ctx) =>
            LicenseRules.PlayerPrice(ctx.World, ctx.Services.Get<GameDatabase>(), Good.BaseIndex, Nation(ctx), ctx.Turn);

        public GuidanceSignal GetGuidance(SimContext ctx) => new GuidanceSignal(
            GuidanceAxis.Trade, ctx.Services.Get<GameDatabase>().Config.GuidanceWeightBuyLicense, new NodeId(ctx.World.Get<PlayerState>().Node));
    }

    /// <summary>Drops the player's expired licenses and announces them. Runs at the end of the turn.</summary>
    public sealed class LicenseSystem : ISimSystem
    {
        LicenseTable _licenses;

        public string Id => "agents.licenses";
        public SimPhase Phase => SimPhase.Cleanup;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx) => _licenses = ctx.World.Get<LicenseTable>();

        public void Execute(SimContext ctx)
        {
            // Expired means invalid from ExpiresTurn on; drop it at the end of the turn before.
            for (int r = _licenses.Count - 1; r >= 0; r--)
            {
                if (ctx.Turn + 1 < _licenses.ExpiresTurn[r]) continue;
                ctx.Events.Publish(new LicenseExpired(_licenses.BaseGood[r], new NodeId(_licenses.Nation[r]), ctx.Turn));
                _licenses.RemoveRow(r);
            }
        }

        /// <summary>The player's licenses, valid ones only.</summary>
        public static void Collect(LicenseTable licenses, List<LicenseInfo> into)
        {
            into.Clear();
            for (int r = 0; r < licenses.Count; r++)
                into.Add(new LicenseInfo(licenses.BaseGood[r], new NodeId(licenses.Nation[r]), licenses.ExpiresTurn[r]));
        }
    }
}
