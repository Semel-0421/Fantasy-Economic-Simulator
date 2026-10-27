using System;
using System.IO;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>Which nodes are ports (DEC-053). Water routes must run between ports. Static, so not saved.</summary>
    public sealed class NodePorts : IWorldComponent, IDisposable
    {
        public NativeArray<byte> IsPort;

        public NodePorts(MapDef map, NodeTable nodes, RouteTable routes)
        {
            IsPort = new NativeArray<byte>(nodes.Count, Allocator.Persistent);
            foreach (var def in map.Nodes)
                if (def.Port && nodes.TryFind(def.Key, out var node)) IsPort[node.Value] = 1;
            for (int r = 0; r < routes.Count; r++)
            {
                if (!((RouteTerrain)routes.Terrain[r]).IsWater()) continue;
                if (IsPort[routes.From[r]] == 0 || IsPort[routes.To[r]] == 0)
                {
                    string from = nodes.KeyOf(routes.From[r]), to = nodes.KeyOf(routes.To[r]);
                    IsPort.Dispose();
                    throw new FormatException($"Water route {from}->{to} must run between ports.");
                }
            }
        }

        public bool Contains(int node) => IsPort[node] != 0;

        public void Dispose() => IsPort.DisposeIfCreated();
    }

    /// <summary>
    /// Paved roads (DEC-054): each route's condition (0 = unpaved, 1000 = fully paved), who maintains it and
    /// any road being built on it. Paving cuts the per-unit charge and the encounter chance in proportion to
    /// the condition; unpaid upkeep lets it decay.
    /// </summary>
    public sealed class RoadState : IPersistentComponent
    {
        public const int MaxCondition = 1000;
        /// <summary>Owner value for roads the player built; nation-maintained roads hold the nation index.</summary>
        public const int PlayerOwner = -2;
        public const int NoOwner = -1;

        /// <summary>Per route: condition in permille.</summary>
        public readonly int[] Condition;
        /// <summary>Per route: nation index maintaining it, <see cref="PlayerOwner"/> or <see cref="NoOwner"/>.</summary>
        public readonly int[] Owner;
        /// <summary>Per route: turn the road under construction is finished, or -1.</summary>
        public readonly int[] BuildDoneTurn;

        public string Key => "world.roads";
        public int Version => 1;

        /// <summary>Roads paved in the map start in full condition, maintained by the nation of their start node.</summary>
        public RoadState(MapDef map, RouteTable routes, NodeBanks banks, NodeTable nodes)
        {
            Condition = new int[routes.Count];
            Owner = new int[routes.Count];
            BuildDoneTurn = new int[routes.Count];
            for (int r = 0; r < routes.Count; r++)
            {
                Owner[r] = NoOwner;
                BuildDoneTurn[r] = -1;
                if (!map.Routes[r].Paved) continue;
                if (((RouteTerrain)routes.Terrain[r]).IsWater())
                    throw new FormatException($"Route {map.Routes[r].FromKey}->{map.Routes[r].ToKey}: water routes cannot be paved.");
                Condition[r] = MaxCondition;
                int nation = nodes.AncestorAt(routes.From[r], WorldLevel.Nation);
                Owner[r] = nation >= 0 ? banks.NationIndex(nation) : NoOwner;
            }
        }

        public bool IsPaved(int route) => Condition[route] > 0;

        /// <summary>Share of a paving effect that applies: the effect's full size scaled by the condition.</summary>
        public Fx Effect(int route, Fx fullEffect) => fullEffect * Fx.FromRatio(Condition[route], MaxCondition);

        public void Write(BinaryWriter writer)
        {
            writer.Write(Condition.Length);
            for (int r = 0; r < Condition.Length; r++)
            {
                writer.Write(Condition[r]);
                writer.Write(Owner[r]);
                writer.Write(BuildDoneTurn[r]);
            }
        }

        public void Read(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count != Condition.Length) throw new InvalidDataException($"Saved route count {count} != {Condition.Length}.");
            for (int r = 0; r < count; r++)
            {
                Condition[r] = reader.ReadInt32();
                Owner[r] = reader.ReadInt32();
                BuildDoneTurn[r] = reader.ReadInt32();
            }
        }
    }
}
