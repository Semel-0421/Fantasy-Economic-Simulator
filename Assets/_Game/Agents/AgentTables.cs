using System.IO;
using FES.Core;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Merchant groups (SoA). A row is a trading group operating at <see cref="Scale"/> from its home node.
    /// Player-hired merchants are ordinary rows with Owner = <see cref="OwnerIds.Player"/>.
    /// </summary>
    public sealed class AgentTable : NativeTable
    {
        public const string SectionKey = "agents.merchants";

        /// <summary>Where the group is; while travelling, the node it left.</summary>
        public readonly NativeColumn<int> Node;
        public readonly NativeColumn<int> Owner;
        public readonly NativeColumn<byte> Scale;
        public readonly NativeColumn<byte> Mode;
        /// <summary>Coins of the continent the group trades in (it stays on its home continent).</summary>
        public readonly NativeColumn<long> Cash;
        /// <summary>Where the group was founded; a replacement after bankruptcy starts here.</summary>
        public readonly NativeColumn<int> Home;
        /// <summary>Node the group is travelling to, or -1.</summary>
        public readonly NativeColumn<int> Destination;
        public readonly NativeColumn<int> ArrivalTurn;
        /// <summary>Consecutive weeks that ended with cash below zero.</summary>
        public readonly NativeColumn<int> NegativeCashWeeks;
        /// <summary>The interest group owning the merchant group; -1 for none (DEC-055).</summary>
        public readonly NativeColumn<int> Group;
        /// <summary>Personality and occupation, see <see cref="MerchantTraits"/>.</summary>
        public readonly NativeColumn<byte> Trait;
        /// <summary>The nodes the group knows the prices of.</summary>
        public readonly NativeColumn<NodeMemory> Memory;
        /// <summary>What the group carries with, see <see cref="MerchantVehicles"/> (DEC-084).</summary>
        public readonly NativeColumn<byte> Vehicle;
        /// <summary>The guild or company the group belongs to, or -1 (DEC-086); apart from its owner <see cref="Group"/>.</summary>
        public readonly NativeColumn<int> Association;
        /// <summary>What the group paid for the goods on board, for its profit and loss (DEC-093).</summary>
        public readonly NativeColumn<long> LoadCost;
        /// <summary>What the bank guarantees the goods on board will bring, in the group's coins (DEC-099).</summary>
        public readonly NativeColumn<long> HedgeFloor;

        public override string Key => SectionKey;
        public override int Version => 8;

        public AgentTable()
        {
            Node = Column<int>();
            Owner = Column<int>();
            Scale = Column<byte>();
            Mode = Column<byte>();
            Cash = Column<long>();
            Home = Column<int>();
            Destination = Column<int>();
            ArrivalTurn = Column<int>();
            NegativeCashWeeks = Column<int>();
            Group = Column<int>();
            Trait = Column<byte>();
            Memory = Column<NodeMemory>();
            Vehicle = Column<byte>();
            Association = Column<int>();
            LoadCost = Column<long>();
            HedgeFloor = Column<long>();
        }

        public AgentId Add(int node, int owner, WorldLevel scale, ControlMode mode, long cash)
        {
            Node.Add(node);
            Owner.Add(owner);
            Scale.Add((byte)scale);
            Mode.Add((byte)mode);
            Cash.Add(cash);
            Home.Add(node);
            Destination.Add(-1);
            ArrivalTurn.Add(-1);
            NegativeCashWeeks.Add(0);
            Group.Add(-1);
            Trait.Add(MerchantTraits.Neutral);
            Memory.Add(NodeMemory.Empty);
            Vehicle.Add(MerchantVehicles.Pack);
            Association.Add(-1);
            LoadCost.Add(0);
            HedgeFloor.Add(0);
            return new AgentId(Count - 1);
        }

        public bool IsTravelling(int agent) => Destination[agent] >= 0;
    }

    /// <summary>Version 1 had no trip state: every group was at home and idle.</summary>
    public sealed class AgentTableV1Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            var reader = new BinaryReader(new MemoryStream(section));
            int rows = reader.ReadInt32();
            var nodes = new int[rows];
            for (int r = 0; r < rows; r++) nodes[r] = reader.ReadInt32();

            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                foreach (int node in nodes) writer.Write(node); // Home
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(-1); // Destination
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(-1); // ArrivalTurn
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(0); // NegativeCashWeeks
            }
            return output.ToArray();
        }
    }

    /// <summary>Version 6 kept no books: what is on board counts as paid for nothing until it is sold.</summary>
    public sealed class AgentTableV6Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 6;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(0L);
            }
            return output.ToArray();
        }
    }

    /// <summary>Version 7 had no price cover.</summary>
    public sealed class AgentTableV7Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 7;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(0L);
            }
            return output.ToArray();
        }
    }

    /// <summary>Producers: farms, mines, workshops, services (SoA). Behavior comes from FacilityDef and its recipe.</summary>
    public sealed class FacilityTable : NativeTable
    {
        public const string SectionKey = "agents.facilities";

        public readonly NativeColumn<int> Node;
        public readonly NativeColumn<int> DefIndex;
        public readonly NativeColumn<int> Owner;
        public readonly NativeColumn<byte> Mode;
        public readonly NativeColumn<int> Level;
        /// <summary>Raw Fx.</summary>
        public readonly NativeColumn<long> Efficiency;
        /// <summary>
        /// Coins of the facility's continent. Pays for inputs and weekly upkeep; NPC output sales add to it.
        /// Upkeep can push it below zero.
        /// </summary>
        public readonly NativeColumn<long> Cash;
        /// <summary>Consecutive weeks that ended with cash below zero.</summary>
        public readonly NativeColumn<int> NegativeCashWeeks;
        /// <summary>
        /// After the player lost this facility to bankruptcy: the turn until which they can still empty its
        /// warehouse, or -1.
        /// </summary>
        public readonly NativeColumn<int> WarehouseClaimUntil;
        /// <summary>The interest group owning an NPC facility, or the player's company; -1 for none (DEC-055).</summary>
        public readonly NativeColumn<int> Group;

        public override string Key => SectionKey;
        public override int Version => 3;

        public FacilityTable()
        {
            Node = Column<int>();
            DefIndex = Column<int>();
            Owner = Column<int>();
            Mode = Column<byte>();
            Level = Column<int>();
            Efficiency = Column<long>();
            Cash = Column<long>();
            NegativeCashWeeks = Column<int>();
            WarehouseClaimUntil = Column<int>();
            Group = Column<int>();
        }

        public FacilityId Add(int node, int defIndex, int owner, ControlMode mode, int level, Fx efficiency, long cash)
        {
            Node.Add(node);
            DefIndex.Add(defIndex);
            Owner.Add(owner);
            Mode.Add((byte)mode);
            Level.Add(level);
            Efficiency.Add(efficiency.Raw);
            Cash.Add(cash);
            NegativeCashWeeks.Add(0);
            WarehouseClaimUntil.Add(-1);
            Group.Add(-1);
            return new FacilityId(Count - 1);
        }
    }

    /// <summary>
    /// Version 1 had no cash or warehouse-claim columns. The migration cannot see the facility types, so it writes a cash
    /// that no facility can reach, which <see cref="ProductionSystem"/> replaces with the tier's starting cash after loading.
    /// </summary>
    public sealed class FacilityTableV1Migration : ISaveMigration
    {
        public const long UnsetCash = long.MinValue;

        public string SectionKey => FacilityTable.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(UnsetCash);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(0); // NegativeCashWeeks
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(-1); // WarehouseClaimUntil
            }
            return output.ToArray();
        }
    }

    /// <summary>The peddler the player controls. While travelling, the player still counts as at <see cref="Node"/>.</summary>
    public sealed class PlayerState : IPersistentComponent
    {
        public const string SectionKey = "agents.player";
        /// <summary>Coin count written by <see cref="PlayerStateV2Migration"/>: one legacy balance follows.</summary>
        internal const int LegacyCashMarker = -1;

        readonly NodeTable _nodes;

        public int Node;
        /// <summary>Coins carried, per continent currency. Trades are paid in the market's local coin.</summary>
        public readonly long[] Coins;
        /// <summary>Node the player is travelling to, or -1.</summary>
        public int TravelDestination = -1;
        /// <summary>Turn on which the player reaches <see cref="TravelDestination"/>.</summary>
        public int ArrivalTurn = -1;

        public PlayerState(NodeTable nodes)
        {
            _nodes = nodes;
            Coins = new long[nodes.ContinentCount];
        }

        public bool IsTravelling => TravelDestination >= 0;

        /// <summary>Continent of <see cref="Node"/>: its coins are the local currency.</summary>
        public int LocalContinent => _nodes.Continent[Node];

        public ref long CoinsAt(int node) => ref Coins[_nodes.Continent[node]];

        public ref long LocalCoins => ref Coins[LocalContinent];

        public string Key => SectionKey;
        public int Version => 3;

        public void Write(BinaryWriter writer)
        {
            writer.Write(Node);
            writer.Write(TravelDestination);
            writer.Write(ArrivalTurn);
            writer.Write(Coins.Length);
            foreach (long coins in Coins) writer.Write(coins);
        }

        public void Read(BinaryReader reader)
        {
            int node = reader.ReadInt32(), destination = reader.ReadInt32();
            if (node < 0 || node >= _nodes.Count || destination < -1 || destination >= _nodes.Count)
                throw new InvalidDataException($"Saved player position {node} or destination {destination} is off the map.");
            Node = node;
            TravelDestination = destination;
            ArrivalTurn = reader.ReadInt32();
            int count = reader.ReadInt32();
            System.Array.Clear(Coins, 0, Coins.Length);
            if (count == LegacyCashMarker)
            {
                Coins[LocalContinent] = reader.ReadInt64();
                return;
            }
            if (count != Coins.Length) throw new InvalidDataException($"Saved continent count {count} != {Coins.Length}.");
            for (int c = 0; c < count; c++) Coins[c] = reader.ReadInt64();
        }
    }

    /// <summary>Version 1 had no travel state: the player was never in transit.</summary>
    public sealed class PlayerStateV1Migration : ISaveMigration
    {
        public string SectionKey => PlayerState.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(-1); // TravelDestination
                writer.Write(-1); // ArrivalTurn
            }
            return output.ToArray();
        }
    }

    /// <summary>
    /// Version 2 had one cash balance. It becomes the coins of the continent the player is in; the
    /// migration cannot see the map, so it leaves a marker that <see cref="PlayerState.Read"/> resolves.
    /// </summary>
    public sealed class PlayerStateV2Migration : ISaveMigration
    {
        public string SectionKey => PlayerState.SectionKey;
        public int FromVersion => 2;

        public byte[] Migrate(byte[] section)
        {
            using var reader = new BinaryReader(new MemoryStream(section));
            int node = reader.ReadInt32();
            long cash = reader.ReadInt64();
            int destination = reader.ReadInt32();
            int arrival = reader.ReadInt32();

            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(node);
                writer.Write(destination);
                writer.Write(arrival);
                writer.Write(PlayerState.LegacyCashMarker);
                writer.Write(cash);
            }
            return output.ToArray();
        }
    }
}
