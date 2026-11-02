using System;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    public sealed class GuildJoined : ISimEvent
    {
        public readonly int Guild;
        public readonly int ExpiresTurn;
        public readonly long Fee;

        public GuildJoined(int guild, int expiresTurn, long fee)
        {
            Guild = guild;
            ExpiresTurn = expiresTurn;
            Fee = fee;
        }
    }

    public sealed class GuildMembershipExpired : ISimEvent
    {
        public readonly int Guild;
        public readonly int Turn;

        public GuildMembershipExpired(int guild, int turn)
        {
            Guild = guild;
            Turn = turn;
        }
    }

    static class PlayerGuild
    {
        /// <summary>The guild exists and the player stands in its city.</summary>
        public static string Check(SimContext ctx, int guild)
        {
            var groups = ctx.World.Get<GroupTable>();
            if (guild < 0 || guild >= groups.Count || groups.KindOf(guild) != GroupKind.Guild) return "Unknown guild.";
            var player = ctx.World.Get<PlayerState>();
            return player.IsTravelling || player.Node != groups.Node[guild] ? "Guilds are joined in their city." : null;
        }
    }

    /// <summary>Joins, or renews for a year, a guild in the city the player is in, paying the yearly fee (DEC-056).</summary>
    public sealed class JoinGuildCommand : ICommand
    {
        public readonly int Guild;

        public JoinGuildCommand(int guild) => Guild = guild;

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerGuild.Check(ctx, Guild);
            if (error != null) return CommandResult.Fail(error);
            return ctx.World.Get<PlayerState>().LocalCoins >= ctx.Services.Get<GameDatabase>().Config.GuildFee
                ? CommandResult.Success
                : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            long fee = ctx.Services.Get<GameDatabase>().Config.GuildFee;
            ctx.World.Get<PlayerState>().LocalCoins -= fee;
            ctx.World.Get<GroupTable>().Cash[Guild] += fee;
            var memberships = ctx.World.Get<GuildMembershipTable>();
            memberships.Extend(Guild, ctx.Turn);
            ctx.Events.Publish(new GuildJoined(Guild, memberships.ExpiresTurn[memberships.Find(Guild)], fee));
        }
    }

    /// <summary>
    /// Moves goods between the cart and the guild hall of a guild the player belongs to, in its city
    /// (DEC-057). Goods stay in the hall if the membership lapses, but can be moved again only after renewing.
    /// </summary>
    public sealed class MoveGuildHallCargoCommand : ICommand
    {
        public readonly int Guild;
        public readonly GoodId Good;
        public readonly int Quantity;
        public readonly bool ToHall;

        public MoveGuildHallCargoCommand(int guild, GoodId good, int quantity, bool toHall)
        {
            Guild = guild;
            Good = good;
            Quantity = quantity;
            ToHall = toHall;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Quantity <= 0) return CommandResult.Fail("Quantity must be positive.");
            if (!Good.IsValid || Good.Value >= ctx.Services.Get<GameDatabase>().GoodCount) return CommandResult.Fail("Unknown good.");
            var error = PlayerGuild.Check(ctx, Guild);
            if (error != null) return CommandResult.Fail(error);
            if (!ctx.World.Get<GuildMembershipTable>().IsMember(Guild, ctx.Turn)) return CommandResult.Fail("Only members use the guild hall.");
            return Fill(ctx) > 0 ? CommandResult.Success : CommandResult.Fail("Nothing to move, or no room.");
        }

        public void Apply(SimContext ctx)
        {
            int moved = Fill(ctx);
            var cargo = ctx.World.Get<CargoTable>();
            int hall = CargoHolders.GuildHall(Guild);
            cargo.Change(ToHall ? CargoHolders.PlayerCart : hall, Good, -moved);
            cargo.Change(ToHall ? hall : CargoHolders.PlayerCart, Good, moved);
        }

        int Fill(SimContext ctx)
        {
            var cargo = ctx.World.Get<CargoTable>();
            var markets = ctx.World.Get<FES.Economy.MarketStore>();
            var config = ctx.Services.Get<GameDatabase>().Config;
            int hall = CargoHolders.GuildHall(Guild);
            int to = ToHall ? hall : CargoHolders.PlayerCart;
            var capacity = ToHall
                ? new CargoCapacity(config.GuildHallMaxWeight, config.GuildHallMaxSize)
                : PlayerTrade.LoadCapacity(ctx);
            int held = cargo.QuantityOf(ToHall ? CargoHolders.PlayerCart : hall, Good);
            int room = capacity.MaxAdditional(cargo.LoadOf(to, markets.GoodWeight, markets.GoodSize),
                markets.GoodWeight[Good.Value], markets.GoodSize[Good.Value]);
            return Math.Min(Quantity, Math.Min(held, room));
        }
    }

    public static class GuildBenefits
    {
        /// <summary>Whether the player belongs to any guild of the city at (or above the village at) <paramref name="node"/>.</summary>
        public static bool MemberHere(WorldState world, int node, int turn)
        {
            var groups = world.Get<GroupTable>();
            var memberships = world.Get<GuildMembershipTable>();
            int city = new GroupDirectory(groups, world.Get<NodeTable>()).CityOf(node);
            if (city < 0) return false;
            for (int r = 0; r < memberships.Count; r++)
                if (turn < memberships.ExpiresTurn[r] && groups.Node[memberships.Guild[r]] == city) return true;
            return false;
        }

        /// <summary>Whether the player belongs to a guild of the good's trade anywhere in the nation (license recommendation).</summary>
        public static bool MemberOfTrade(WorldState world, GameDatabase db, int nationNode, int category, int turn)
        {
            var groups = world.Get<GroupTable>();
            var memberships = world.Get<GuildMembershipTable>();
            var nodes = world.Get<NodeTable>();
            for (int r = 0; r < memberships.Count; r++)
            {
                int g = memberships.Guild[r];
                if (turn < memberships.ExpiresTurn[r] && groups.Category[g] == category && nodes.AncestorAt(groups.Node[g], WorldLevel.Nation) == nationNode)
                    return true;
            }
            return false;
        }
    }
}
