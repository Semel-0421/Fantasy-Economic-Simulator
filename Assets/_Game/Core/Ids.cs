using System;

namespace FES.Core
{
    // Typed dense indices. Each id doubles as an offset into the owning SoA table.
    // Entities are never deleted (only flagged inactive), so ids stay stable for saves.

    public readonly struct NodeId : IEquatable<NodeId>
    {
        public static readonly NodeId None = new NodeId(-1);
        public readonly int Value;
        public NodeId(int value) => Value = value;
        public bool IsValid => Value >= 0;
        public bool Equals(NodeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NodeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Node#{Value}";
        public static bool operator ==(NodeId a, NodeId b) => a.Value == b.Value;
        public static bool operator !=(NodeId a, NodeId b) => a.Value != b.Value;
    }

    /// <summary>Flat good id: <c>baseGoodIndex * QualityGrades.Count + grade</c>.</summary>
    public readonly struct GoodId : IEquatable<GoodId>
    {
        public static readonly GoodId None = new GoodId(-1);
        public readonly int Value;
        public GoodId(int value) => Value = value;
        public bool IsValid => Value >= 0;
        public int BaseIndex => Value / QualityGrades.Count;
        public QualityGrade Grade => (QualityGrade)(Value % QualityGrades.Count);
        public static GoodId From(int baseIndex, QualityGrade grade) => new GoodId(baseIndex * QualityGrades.Count + (int)grade);
        public bool Equals(GoodId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GoodId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Good#{Value}";
        public static bool operator ==(GoodId a, GoodId b) => a.Value == b.Value;
        public static bool operator !=(GoodId a, GoodId b) => a.Value != b.Value;
    }

    public readonly struct AgentId : IEquatable<AgentId>
    {
        public static readonly AgentId None = new AgentId(-1);
        public readonly int Value;
        public AgentId(int value) => Value = value;
        public bool IsValid => Value >= 0;
        public bool Equals(AgentId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AgentId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Agent#{Value}";
        public static bool operator ==(AgentId a, AgentId b) => a.Value == b.Value;
        public static bool operator !=(AgentId a, AgentId b) => a.Value != b.Value;
    }

    public readonly struct FacilityId : IEquatable<FacilityId>
    {
        public static readonly FacilityId None = new FacilityId(-1);
        public readonly int Value;
        public FacilityId(int value) => Value = value;
        public bool IsValid => Value >= 0;
        public bool Equals(FacilityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FacilityId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Facility#{Value}";
        public static bool operator ==(FacilityId a, FacilityId b) => a.Value == b.Value;
        public static bool operator !=(FacilityId a, FacilityId b) => a.Value != b.Value;
    }

    /// <summary>Who owns an agent or facility. Values >= 0 are players (0 = local player).</summary>
    public static class OwnerIds
    {
        public const int Npc = -1;
        public const int Player = 0;
        /// <summary>A state bank holding a seized facility until its auction closes.</summary>
        public const int Bank = -2;
    }
}
