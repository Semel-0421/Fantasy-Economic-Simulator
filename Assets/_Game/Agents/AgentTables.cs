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
        public readonly NativeColumn<int> Node;
        public readonly NativeColumn<int> Owner;
        public readonly NativeColumn<byte> Scale;
        public readonly NativeColumn<byte> Mode;
        public readonly NativeColumn<long> Cash;

        public override string Key => "agents.merchants";
        public override int Version => 1;

        public AgentTable()
        {
            Node = Column<int>();
            Owner = Column<int>();
            Scale = Column<byte>();
            Mode = Column<byte>();
            Cash = Column<long>();
        }

        public AgentId Add(int node, int owner, WorldLevel scale, ControlMode mode, long cash)
        {
            Node.Add(node);
            Owner.Add(owner);
            Scale.Add((byte)scale);
            Mode.Add((byte)mode);
            Cash.Add(cash);
            return new AgentId(Count - 1);
        }
    }

    /// <summary>Producers: farms, mines, workshops, services (SoA). Behavior comes from FacilityDef and its recipe.</summary>
    public sealed class FacilityTable : NativeTable
    {
        public readonly NativeColumn<int> Node;
        public readonly NativeColumn<int> DefIndex;
        public readonly NativeColumn<int> Owner;
        public readonly NativeColumn<byte> Mode;
        public readonly NativeColumn<int> Level;
        /// <summary>Raw Fx.</summary>
        public readonly NativeColumn<long> Efficiency;

        public override string Key => "agents.facilities";
        public override int Version => 1;

        public FacilityTable()
        {
            Node = Column<int>();
            DefIndex = Column<int>();
            Owner = Column<int>();
            Mode = Column<byte>();
            Level = Column<int>();
            Efficiency = Column<long>();
        }

        public FacilityId Add(int node, int defIndex, int owner, ControlMode mode, int level, Fx efficiency)
        {
            Node.Add(node);
            DefIndex.Add(defIndex);
            Owner.Add(owner);
            Mode.Add((byte)mode);
            Level.Add(level);
            Efficiency.Add(efficiency.Raw);
            return new FacilityId(Count - 1);
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
            Node = reader.ReadInt32();
            TravelDestination = reader.ReadInt32();
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
