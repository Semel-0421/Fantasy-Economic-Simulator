using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    public enum GroupKind : byte
    {
        Company = 0,
        Guild = 1,
        StateEnterprise = 2,
        PrivateEnterprise = 3,
    }

    /// <summary>
    /// Economic interest groups (SoA, SYS-INTEREST-GROUPS, DEC-055). Companies, state enterprises and private
    /// enterprises own facilities and merchant groups (their <c>Group</c> column); guilds are associations per
    /// city and trade that the player can join. Rows are never removed, so group ids stay stable.
    /// </summary>
    public sealed class GroupTable : NativeTable
    {
        public readonly NativeColumn<byte> Kind;
        /// <summary>Home node: the nation node of companies and state enterprises, the city of a guild, the node of a private enterprise.</summary>
        public readonly NativeColumn<int> Node;
        /// <summary>A guild's trade (goods category); -1 for other kinds.</summary>
        public readonly NativeColumn<int> Category;
        /// <summary>Coins of the home node's continent; funds lobbying and investment later (P12b).</summary>
        public readonly NativeColumn<long> Cash;
        /// <summary>1 for the player's company.</summary>
        public readonly NativeColumn<byte> IsPlayer;

        public override string Key => "agents.groups";
        public override int Version => 1;

        public GroupTable()
        {
            Kind = Column<byte>();
            Node = Column<int>();
            Category = Column<int>();
            Cash = Column<long>();
            IsPlayer = Column<byte>();
        }

        public int Add(GroupKind kind, int node, int category, long cash, bool player = false)
        {
            Kind.Add((byte)kind);
            Node.Add(node);
            Category.Add(category);
            Cash.Add(cash);
            IsPlayer.Add((byte)(player ? 1 : 0));
            return Count - 1;
        }

        public GroupKind KindOf(int group) => (GroupKind)Kind[group];

        /// <summary>The player's company, or -1 before it is founded.</summary>
        public int PlayerCompany()
        {
            for (int g = 0; g < Count; g++)
                if (IsPlayer[g] != 0) return g;
            return -1;
        }
    }

    /// <summary>The player's guild memberships (SoA): one row per guild, valid until <see cref="ExpiresTurn"/>.</summary>
    public sealed class GuildMembershipTable : NativeTable
    {
        public readonly NativeColumn<int> Guild;
        public readonly NativeColumn<int> ExpiresTurn;

        public override string Key => "agents.guild_memberships";
        public override int Version => 1;

        public GuildMembershipTable()
        {
            Guild = Column<int>();
            ExpiresTurn = Column<int>();
        }

        public int Find(int guild)
        {
            for (int r = 0; r < Count; r++)
                if (Guild[r] == guild) return r;
            return -1;
        }

        public bool IsMember(int guild, int turn)
        {
            int r = Find(guild);
            return r >= 0 && turn < ExpiresTurn[r];
        }

        /// <summary>Adds a year of membership, counted from the current expiry when still valid.</summary>
        public void Extend(int guild, int turn)
        {
            int r = Find(guild);
            if (r < 0)
            {
                Guild.Add(guild);
                ExpiresTurn.Add(turn + SimClock.WeeksPerYear);
                return;
            }
            ExpiresTurn[r] = Math.Max(turn, ExpiresTurn[r]) + SimClock.WeeksPerYear;
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>
    /// Lookups over <see cref="GroupTable"/>: a node's private enterprise, a nation's companies and state
    /// enterprise, a city's guild for a trade. Derived from the table and rebuilt when it grows.
    /// </summary>
    public sealed class GroupDirectory
    {
        readonly GroupTable _groups;
        readonly NodeTable _nodes;
        readonly Dictionary<int, int> _privateByNode = new Dictionary<int, int>();
        readonly Dictionary<int, int> _stateByNation = new Dictionary<int, int>();
        readonly Dictionary<int, List<int>> _companiesByNation = new Dictionary<int, List<int>>();
        readonly Dictionary<(int city, int category), int> _guilds = new Dictionary<(int, int), int>();
        int _builtFor = -1;

        public GroupDirectory(GroupTable groups, NodeTable nodes)
        {
            _groups = groups;
            _nodes = nodes;
        }

        void Refresh()
        {
            if (_builtFor == _groups.Count) return;
            _privateByNode.Clear();
            _stateByNation.Clear();
            _companiesByNation.Clear();
            _guilds.Clear();
            for (int g = 0; g < _groups.Count; g++)
            {
                int node = _groups.Node[g];
                switch (_groups.KindOf(g))
                {
                    case GroupKind.PrivateEnterprise: _privateByNode[node] = g; break;
                    case GroupKind.StateEnterprise: _stateByNation[node] = g; break;
                    case GroupKind.Guild: _guilds[(node, _groups.Category[g])] = g; break;
                    case GroupKind.Company:
                        if (_groups.IsPlayer[g] != 0) break;
                        if (!_companiesByNation.TryGetValue(node, out var list)) _companiesByNation.Add(node, list = new List<int>());
                        list.Add(g);
                        break;
                }
            }
            _builtFor = _groups.Count;
        }

        /// <summary>The node's private enterprise, created when the node has none yet.</summary>
        public int PrivateAt(int node, SimulationConfig config)
        {
            Refresh();
            if (_privateByNode.TryGetValue(node, out int g)) return g;
            g = _groups.Add(GroupKind.PrivateEnterprise, node, -1, config.GroupStartCash[(int)GroupKind.PrivateEnterprise]);
            _privateByNode.Add(node, g); // keep the lookup current instead of rebuilding it for every new row
            _builtFor = _groups.Count;
            return g;
        }

        public int StateEnterpriseOf(int nationNode)
        {
            Refresh();
            return _stateByNation.TryGetValue(nationNode, out int g) ? g : -1;
        }

        public IReadOnlyList<int> CompaniesOf(int nationNode)
        {
            Refresh();
            return _companiesByNation.TryGetValue(nationNode, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();
        }

        /// <summary>The guild of a trade in a city, or -1.</summary>
        public int GuildOf(int city, int category)
        {
            Refresh();
            return _guilds.TryGetValue((city, category), out int g) ? g : -1;
        }

        /// <summary>The city a node's guilds sit in: the node itself if it is a city, its city if it is a village, else -1.</summary>
        public int CityOf(int node)
        {
            var level = _nodes.LevelOf(node);
            if (level == WorldLevel.City) return node;
            if (level != WorldLevel.Village) return -1;
            int parent = _nodes.Parent[node];
            return _nodes.LevelOf(parent) == WorldLevel.City ? parent : -1;
        }
    }

    /// <summary>
    /// Founds the starting groups and gives every NPC facility and merchant group an owner (DEC-055). [Draft]
    /// rules (Claude's proposal): facility types marked state-owned belong to their nation's state enterprise;
    /// workshops (secondary tier) and merchant groups based at region level and above belong to the nation's
    /// companies in turn; everything else belongs to the private enterprise of its node. Each city gets a
    /// guild for every trade its facilities (and its villages') produce.
    /// </summary>
    public static class GroupSetup
    {
        public static void Found(GroupTable groups, NodeTable nodes, GameDatabase db, FacilityTable facilities)
        {
            var config = db.Config;
            int nationStart = nodes.LevelStart(WorldLevel.Nation);
            for (int n = nationStart; n < nationStart + nodes.LevelCount(WorldLevel.Nation); n++)
            {
                groups.Add(GroupKind.StateEnterprise, n, -1, config.GroupStartCash[(int)GroupKind.StateEnterprise]);
                for (int k = 0; k < config.CompaniesPerNation; k++)
                    groups.Add(GroupKind.Company, n, -1, config.GroupStartCash[(int)GroupKind.Company]);
            }

            var guilds = new SortedSet<(int city, int category)>();
            var directory = new GroupDirectory(groups, nodes);
            for (int f = 0; f < facilities.Count; f++)
            {
                int city = directory.CityOf(facilities.Node[f]);
                if (city < 0) continue;
                foreach (var line in db.Facilities[facilities.DefIndex[f]].Recipe.Outputs)
                    guilds.Add((city, db.Good(line.Good).CategoryIndex));
            }
            foreach (var (city, category) in guilds)
                groups.Add(GroupKind.Guild, city, category, config.GroupStartCash[(int)GroupKind.Guild]);
        }

        /// <summary>Gives an owner to every NPC facility and merchant group that has none (at start, and after loading older saves).</summary>
        public static void AssignOwners(GroupTable groups, NodeTable nodes, GameDatabase db, FacilityTable facilities, AgentTable agents)
        {
            var directory = new GroupDirectory(groups, nodes);
            for (int f = 0; f < facilities.Count; f++)
            {
                if (facilities.Group[f] >= 0 || facilities.Owner[f] == OwnerIds.Player) continue;
                var def = db.Facilities[facilities.DefIndex[f]];
                int node = facilities.Node[f];
                int nation = nodes.AncestorAt(node, WorldLevel.Nation);
                int group = -1;
                if (nation >= 0 && def.StateOwned) group = directory.StateEnterpriseOf(nation);
                else if (nation >= 0 && def.Tier == IndustryTier.Secondary) group = Pick(directory.CompaniesOf(nation), f);
                facilities.Group[f] = group >= 0 ? group : directory.PrivateAt(node, db.Config);
            }
            for (int a = 0; a < agents.Count; a++)
            {
                if (agents.Group[a] >= 0 || agents.Owner[a] == OwnerIds.Player) continue;
                int node = agents.Home[a];
                int nation = nodes.AncestorAt(node, WorldLevel.Nation);
                int group = -1;
                if (nation >= 0 && nodes.LevelOf(node) <= WorldLevel.Region) group = Pick(directory.CompaniesOf(nation), a);
                agents.Group[a] = group >= 0 ? group : directory.PrivateAt(node, db.Config);
            }
        }

        static int Pick(IReadOnlyList<int> candidates, int index) => candidates.Count == 0 ? -1 : candidates[index % candidates.Count];
    }

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
                : new CargoCapacity(config.CartMaxWeight, config.CartMaxSize);
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

    /// <summary>
    /// Drops the player's lapsed guild memberships at the end of the turn, and after loading a save from
    /// before interest groups gives every NPC facility and merchant group its owner.
    /// </summary>
    public sealed class GroupSystem : ISimSystem, ILoadListener
    {
        GuildMembershipTable _memberships;

        public string Id => "agents.groups";
        public SimPhase Phase => SimPhase.Cleanup;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx) => _memberships = ctx.World.Get<GuildMembershipTable>();

        public void Execute(SimContext ctx)
        {
            for (int r = _memberships.Count - 1; r >= 0; r--)
            {
                if (ctx.Turn + 1 < _memberships.ExpiresTurn[r]) continue;
                ctx.Events.Publish(new GuildMembershipExpired(_memberships.Guild[r], ctx.Turn));
                _memberships.RemoveRow(r);
            }
        }

        public void OnLoaded(SimContext ctx) =>
            GroupSetup.AssignOwners(ctx.World.Get<GroupTable>(), ctx.World.Get<NodeTable>(), ctx.Services.Get<GameDatabase>(),
                ctx.World.Get<FacilityTable>(), ctx.World.Get<AgentTable>());
    }

    /// <summary>Appends an owner column of -1 (no group yet) to a v2 facility or v2 merchant table section.</summary>
    public sealed class GroupColumnMigration : ISaveMigration
    {
        public string SectionKey { get; }
        public int FromVersion { get; }

        public GroupColumnMigration(string sectionKey, int fromVersion)
        {
            SectionKey = sectionKey;
            FromVersion = fromVersion;
        }

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(-1);
            }
            return output.ToArray();
        }
    }
}
