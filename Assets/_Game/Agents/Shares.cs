using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>
    /// DEC-129: shares in facilities held by others than the facility's group, one row per facility and holder. The
    /// group holds the rest of <see cref="SimulationConfig.FacilityShares"/>.
    /// </summary>
    public sealed class ShareTable : NativeTable
    {
        public readonly NativeColumn<int> Facility;
        /// <summary>A group, <see cref="PlayerHolder"/>, or <see cref="Households"/> of a nation.</summary>
        public readonly NativeColumn<int> Holder;
        public readonly NativeColumn<int> Count;

        public const int PlayerHolder = int.MinValue;
        public static int Households(int nation) => -1 - nation;
        public static bool IsHouseholds(int holder, out int nation)
        {
            nation = -1 - holder;
            return holder < 0 && holder != PlayerHolder;
        }

        public override string Key => "agents.shares";
        public override int Version => 1;

        public ShareTable()
        {
            Facility = Column<int>();
            Holder = Column<int>();
            Count = Column<int>();
        }

        int Row(int f, int holder)
        {
            for (int r = 0; r < Count.Length; r++)
                if (Facility[r] == f && Holder[r] == holder) return r;
            return -1;
        }

        public int Held(int f, int holder)
        {
            int r = Row(f, holder);
            return r < 0 ? 0 : Count[r];
        }

        /// <summary>Shares of a facility that others than its group hold.</summary>
        public int Outside(int f)
        {
            int sum = 0;
            for (int r = 0; r < Count.Length; r++)
                if (Facility[r] == f) sum += Count[r];
            return sum;
        }

        public void Add(int f, int holder, int count)
        {
            int r = Row(f, holder);
            if (r >= 0)
            {
                Count[r] += count;
                if (Count[r] == 0) RemoveRowSwapBack(r);
                return;
            }
            if (count == 0) return;
            Facility.Add(f);
            Holder.Add(holder);
            Count.Add(count);
        }
    }

    /// <summary>DEC-129: what a share is worth, and the holders' part of a facility's dividends.</summary>
    public static class ShareBook
    {
        /// <summary>[Draft] The fundamental value of a share: the facility's asking price (DEC-114) over its shares.</summary>
        public static long Fundamental(WorldState world, GameDatabase db, int f) =>
            Math.Max(1, BuyFacilityCommand.AskingPrice(world, db, f) / Math.Max(1, db.Config.FacilityShares));

        /// <summary>The market price: the fundamental times the premium of the facility's nation's exchange (DEC-131).</summary>
        public static long Price(WorldState world, GameDatabase db, int f)
        {
            int nation = world.Get<NodeBanks>().NationOf(world.Get<FacilityTable>().Node[f]);
            return Math.Max(1, world.Get<AssetMarkets>().StockPremium(nation).MulInt(Fundamental(world, db, f)));
        }

        /// <summary>Before the week's dividends: what each facility with outside holders had paid so far this year.</summary>
        public static void Snapshot(ShareTable shares, NativeList<long> dividendsYear, Dictionary<int, long> into)
        {
            into.Clear();
            for (int r = 0; r < shares.Count.Length; r++)
            {
                int f = shares.Facility[r];
                if (!into.ContainsKey(f)) into[f] = dividendsYear[f];
            }
        }

        /// <summary>After them: the holders' part of each facility's week of dividends, taken from what its group received.</summary>
        public static void PayHolders(WorldState world, GameDatabase db, Dictionary<int, long> before, NativeList<long> dividendsYear)
        {
            var shares = world.Get<ShareTable>();
            foreach (var pair in before)
            {
                int f = pair.Key;
                long week = dividendsYear[f] - pair.Value;
                if (week <= 0) continue;
                long paid = 0;
                for (int r = 0; r < shares.Count.Length; r++)
                {
                    if (shares.Facility[r] != f) continue;
                    long part = week * shares.Count[r] / db.Config.FacilityShares;
                    Pay(world, f, shares.Holder[r], part);
                    paid += part;
                }
                TakeFromOwner(world, f, paid);
            }
        }

        static void TakeFromOwner(WorldState world, int f, long amount)
        {
            var facilities = world.Get<FacilityTable>();
            int group = facilities.Group[f];
            if (group >= 0) world.Get<GroupTable>().Cash[group] -= amount;
            else world.Get<PurseState>().PayHouseholds(facilities.Node[f], -amount);
        }

        static void Pay(WorldState world, int f, int holder, long amount)
        {
            if (amount <= 0) return;
            int node = world.Get<FacilityTable>().Node[f];
            if (holder == ShareTable.PlayerHolder)
            {
                int nation = world.Get<NodeBanks>().NationOf(node);
                var bank = world.Get<BankState>();
                bank.PlayerDeposit[nation] += amount;
                bank.Reserve[nation] += amount;
            }
            else if (ShareTable.IsHouseholds(holder, out int nation)) MoneyLedger.PayNationHouseholds(world, nation, amount);
            else world.Get<GroupTable>().Cash[holder] += amount;
        }

        /// <summary>A group raises up to <paramref name="amount"/> by listing shares to the nation's households, keeping control. Returns the coins raised.</summary>
        public static long List(WorldState world, GameDatabase db, int turn, int f, int nation, long amount)
        {
            var shares = world.Get<ShareTable>();
            int room = db.Config.FacilityMaxListedShares - shares.Outside(f);
            long price = Price(world, db, f);
            int wanted = (int)Math.Min(room, amount / price + (amount % price > 0 ? 1 : 0));
            if (wanted <= 0) return 0;
            long taken = BondDesk.TakeFromHouseholds(world, db.Config, turn, nation, wanted * price);
            int sold = (int)(taken / price);
            if (sold <= 0)
            {
                if (taken > 0) MoneyLedger.PayNationHouseholds(world, nation, taken); // too little for a share: back to them
                return 0;
            }
            long kept = sold * price;
            if (taken > kept) MoneyLedger.PayNationHouseholds(world, nation, taken - kept);
            shares.Add(f, ShareTable.Households(nation), sold);
            return kept;
        }

        /// <summary>DEC-130: shares the bank sells for the player. The group buys back what it can pay for, the nation's households the rest. Returns the coins raised.</summary>
        public static long SellForPlayer(WorldState world, GameDatabase db, int turn, int f, int count)
        {
            var shares = world.Get<ShareTable>();
            var facilities = world.Get<FacilityTable>();
            var groups = world.Get<GroupTable>();
            count = Math.Min(count, shares.Held(f, ShareTable.PlayerHolder));
            long price = Price(world, db, f);
            int group = facilities.Group[f];
            int back = group >= 0 ? (int)Math.Min(count, Math.Max(0, groups.Cash[group]) / price) : 0;
            if (back > 0) groups.Cash[group] -= back * price;
            shares.Add(f, ShareTable.PlayerHolder, -back);
            long raised = back * price;
            int nation = world.Get<NodeBanks>().NationOf(facilities.Node[f]);
            if (count == back || nation < 0) return raised;
            long taken = BondDesk.TakeFromHouseholds(world, db.Config, turn, nation, (count - back) * price);
            int sold = (int)(taken / price);
            if (taken > sold * price) MoneyLedger.PayNationHouseholds(world, nation, taken - sold * price);
            shares.Add(f, ShareTable.PlayerHolder, -sold);
            shares.Add(f, ShareTable.Households(nation), sold);
            return raised + sold * price;
        }
    }

    /// <summary>Shares trade at any branch of the facility's nation's bank (DEC-129).</summary>
    static class Exchange
    {
        public static string Check(SimContext ctx, FacilityId facility)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!facility.IsValid || facility.Value >= facilities.Count) return "Unknown facility.";
            int f = facility.Value;
            if (facilities.Owner[f] != OwnerIds.Npc || facilities.Group[f] < 0 || facilities.Level[f] == 0) return "This facility has no shares for sale.";
            if (ctx.Services.Get<GameDatabase>().Facilities[facilities.DefIndex[f]].StateOwned) return "The state does not sell shares of this facility.";
            var player = ctx.World.Get<PlayerState>();
            var banks = ctx.World.Get<NodeBanks>();
            int nation = banks.NationOf(facilities.Node[f]);
            return !player.IsTravelling && nation >= 0 && banks.BranchNation(player.Node) == nation ? null : "Shares of this facility trade at its nation's bank.";
        }
    }

    /// <summary>The player buys shares from the facility's group, at the share price (DEC-129).</summary>
    public sealed class BuyShareCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly int Count;

        public BuyShareCommand(FacilityId facility, int count)
        {
            Facility = facility;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = Exchange.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            var db = ctx.Services.Get<GameDatabase>();
            if (ctx.World.Get<ShareTable>().Outside(Facility.Value) + Count > db.Config.FacilityMaxListedShares) return CommandResult.Fail("The group keeps control of it.");
            long cost = Count * ShareBook.Price(ctx.World, db, Facility.Value);
            var player = ctx.World.Get<PlayerState>();
            return player.CoinsAt(player.Node) >= cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var db = ctx.Services.Get<GameDatabase>();
            long cost = Count * ShareBook.Price(ctx.World, db, Facility.Value);
            var player = ctx.World.Get<PlayerState>();
            player.CoinsAt(player.Node) -= cost;
            ctx.World.Get<GroupTable>().Cash[ctx.World.Get<FacilityTable>().Group[Facility.Value]] += cost;
            ctx.World.Get<ShareTable>().Add(Facility.Value, ShareTable.PlayerHolder, Count);
        }
    }

    /// <summary>The player sells shares back to the facility's group, as far as it can pay (DEC-129).</summary>
    public sealed class SellShareCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly int Count;

        public SellShareCommand(FacilityId facility, int count)
        {
            Facility = facility;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = Exchange.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<ShareTable>().Held(Facility.Value, ShareTable.PlayerHolder) < Count) return CommandResult.Fail("The player holds fewer shares.");
            long value = Count * ShareBook.Price(ctx.World, ctx.Services.Get<GameDatabase>(), Facility.Value);
            int group = ctx.World.Get<FacilityTable>().Group[Facility.Value];
            return ctx.World.Get<GroupTable>().Cash[group] >= value ? CommandResult.Success : CommandResult.Fail("The group cannot pay for them.");
        }

        public void Apply(SimContext ctx)
        {
            long value = Count * ShareBook.Price(ctx.World, ctx.Services.Get<GameDatabase>(), Facility.Value);
            var player = ctx.World.Get<PlayerState>();
            player.CoinsAt(player.Node) += value;
            ctx.World.Get<GroupTable>().Cash[ctx.World.Get<FacilityTable>().Group[Facility.Value]] -= value;
            ctx.World.Get<ShareTable>().Add(Facility.Value, ShareTable.PlayerHolder, -Count);
        }
    }
}
