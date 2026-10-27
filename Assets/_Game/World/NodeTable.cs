using System;
using System.Collections.Generic;
using FES.Core;
using Unity.Collections;

namespace FES.World
{
    /// <summary>
    /// The world hierarchy (continent > nation > region > city > village) as one flat SoA table.
    /// Nodes are sorted by level, then parent, then key, so each level is a contiguous index range
    /// and tier jobs can run over [LevelStart, LevelStart + LevelCount). Every node owns exactly one
    /// market whose index equals the node index.
    /// Static after build: rebuilt from the map, not saved.
    /// </summary>
    public sealed class NodeTable : IWorldComponent, IDisposable
    {
        public readonly int Count;
        public NativeArray<byte> Level;
        /// <summary>Parent node index, -1 for continents.</summary>
        public NativeArray<int> Parent;
        /// <summary>Ordinal of the continent this node belongs to (0..ContinentCount-1).</summary>
        public NativeArray<int> Continent;
        /// <summary>CSR child list: children of n are Children[ChildStart[n] .. ChildStart[n] + ChildCount[n]).</summary>
        public NativeArray<int> ChildStart;
        public NativeArray<int> ChildCount;
        public NativeArray<int> Children;

        readonly int[] _levelStart = new int[WorldLevels.Count];
        readonly int[] _levelCount = new int[WorldLevels.Count];
        readonly string[] _keys;
        readonly string[] _names;
        readonly Dictionary<string, int> _indexByKey;

        internal NodeTable(int count, string[] keys, string[] names)
        {
            Count = count;
            _keys = keys;
            _names = names;
            _indexByKey = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++) _indexByKey.Add(keys[i], i);
            Level = new NativeArray<byte>(count, Allocator.Persistent);
            Parent = new NativeArray<int>(count, Allocator.Persistent);
            Continent = new NativeArray<int>(count, Allocator.Persistent);
            ChildStart = new NativeArray<int>(count, Allocator.Persistent);
            ChildCount = new NativeArray<int>(count, Allocator.Persistent);
            Children = new NativeArray<int>(Math.Max(0, count), Allocator.Persistent);
        }

        internal void SetLevelRange(WorldLevel level, int start, int count)
        {
            _levelStart[(int)level] = start;
            _levelCount[(int)level] = count;
        }

        public int LevelStart(WorldLevel level) => _levelStart[(int)level];
        public int LevelCount(WorldLevel level) => _levelCount[(int)level];
        public int ContinentCount => _levelCount[(int)WorldLevel.Continent];

        public WorldLevel LevelOf(int node) => (WorldLevel)Level[node];
        public string KeyOf(int node) => _keys[node];
        public string NameOf(int node) => _names[node];

        public bool TryFind(string key, out NodeId node)
        {
            if (_indexByKey.TryGetValue(key, out int index))
            {
                node = new NodeId(index);
                return true;
            }
            node = NodeId.None;
            return false;
        }

        /// <summary>True when <paramref name="ancestor"/> is <paramref name="node"/> or one of its ancestors.</summary>
        public bool IsAncestorOrSelf(int ancestor, int node)
        {
            for (int n = node; n >= 0; n = Parent[n])
                if (n == ancestor) return true;
            return false;
        }

        /// <summary>Appends <paramref name="root"/> and all its descendants to <paramref name="into"/> (order unspecified).</summary>
        public void CollectSubtree(int root, List<int> into)
        {
            int first = into.Count;
            into.Add(root);
            for (int i = first; i < into.Count; i++)
            {
                int n = into[i];
                int start = ChildStart[n];
                for (int k = start; k < start + ChildCount[n]; k++) into.Add(Children[k]);
            }
        }

        /// <summary>The ancestor of <paramref name="node"/> at <paramref name="level"/> (or the node itself), -1 if none.</summary>
        public int AncestorAt(int node, WorldLevel level)
        {
            for (int n = node; n >= 0; n = Parent[n])
                if (Level[n] == (byte)level) return n;
            return -1;
        }

        public void Dispose()
        {
            Level.DisposeIfCreated();
            Parent.DisposeIfCreated();
            Continent.DisposeIfCreated();
            ChildStart.DisposeIfCreated();
            ChildCount.DisposeIfCreated();
            Children.DisposeIfCreated();
        }
    }

    /// <summary>Directed trade links between markets with travel time and per-unit transport cost. Static after build.</summary>
    public sealed class RouteTable : IWorldComponent, IDisposable
    {
        public readonly int Count;
        public NativeArray<int> From;
        public NativeArray<int> To;
        public NativeArray<int> TravelTurns;
        public NativeArray<int> CostPerUnit;
        public NativeArray<int> BaseFee;
        public NativeArray<byte> Terrain;
        /// <summary>CSR outgoing routes: routes of node n are RouteIndex[OutStart[n] .. OutStart[n] + OutCount[n]).</summary>
        public NativeArray<int> OutStart;
        public NativeArray<int> OutCount;
        public NativeArray<int> RouteIndex;

        internal RouteTable(int routeCount, int nodeCount)
        {
            Count = routeCount;
            From = new NativeArray<int>(routeCount, Allocator.Persistent);
            To = new NativeArray<int>(routeCount, Allocator.Persistent);
            TravelTurns = new NativeArray<int>(routeCount, Allocator.Persistent);
            CostPerUnit = new NativeArray<int>(routeCount, Allocator.Persistent);
            BaseFee = new NativeArray<int>(routeCount, Allocator.Persistent);
            Terrain = new NativeArray<byte>(routeCount, Allocator.Persistent);
            OutStart = new NativeArray<int>(nodeCount, Allocator.Persistent);
            OutCount = new NativeArray<int>(nodeCount, Allocator.Persistent);
            RouteIndex = new NativeArray<int>(routeCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            From.DisposeIfCreated();
            To.DisposeIfCreated();
            TravelTurns.DisposeIfCreated();
            CostPerUnit.DisposeIfCreated();
            BaseFee.DisposeIfCreated();
            Terrain.DisposeIfCreated();
            OutStart.DisposeIfCreated();
            OutCount.DisposeIfCreated();
            RouteIndex.DisposeIfCreated();
        }
    }
}
