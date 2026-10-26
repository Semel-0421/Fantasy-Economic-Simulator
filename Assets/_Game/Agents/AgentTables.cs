using System.IO;
using FES.Core;

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

    /// <summary>The peddler the player controls.</summary>
    public sealed class PlayerState : IPersistentComponent
    {
        public int Node;
        public long Cash;

        public string Key => "agents.player";
        public int Version => 1;

        public void Write(BinaryWriter writer)
        {
            writer.Write(Node);
            writer.Write(Cash);
        }

        public void Read(BinaryReader reader)
        {
            Node = reader.ReadInt32();
            Cash = reader.ReadInt64();
        }
    }
}
