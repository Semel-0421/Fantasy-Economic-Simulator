using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>The only place that turns a <see cref="MapDef"/> into world tables. Procedural maps plug in upstream.</summary>
    public static class WorldBuilder
    {
        public static NodeTable BuildNodes(MapDef map)
        {
            var byKey = new Dictionary<string, MapNodeDef>(StringComparer.Ordinal);
            foreach (var n in map.Nodes)
            {
                if (string.IsNullOrEmpty(n.Key)) throw new FormatException("Map node with empty key.");
                if (!byKey.TryAdd(n.Key, n)) throw new FormatException($"Duplicate map node key '{n.Key}'.");
            }
            foreach (var n in map.Nodes) Validate(n, byKey);

            // Order level by level; within a level by (parent's new index, key) so the layout is deterministic.
            var ordered = new List<MapNodeDef>(map.Nodes.Count);
            var newIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var levelStart = new int[WorldLevels.Count];
            var levelCount = new int[WorldLevels.Count];
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                var level = (WorldLevel)l;
                var nodes = map.Nodes.FindAll(n => n.Level == level);
                nodes.Sort((a, b) =>
                {
                    int pa = a.Level == WorldLevel.Continent ? -1 : newIndex[a.ParentKey];
                    int pb = b.Level == WorldLevel.Continent ? -1 : newIndex[b.ParentKey];
                    return pa != pb ? pa.CompareTo(pb) : string.CompareOrdinal(a.Key, b.Key);
                });
                levelStart[l] = ordered.Count;
                levelCount[l] = nodes.Count;
                foreach (var n in nodes)
                {
                    newIndex.Add(n.Key, ordered.Count);
                    ordered.Add(n);
                }
            }

            int count = ordered.Count;
            var keys = new string[count];
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = ordered[i].Key;
                names[i] = string.IsNullOrEmpty(ordered[i].Name) ? ordered[i].Key : ordered[i].Name;
            }

            var table = new NodeTable(count, keys, names);
            for (int l = 0; l < WorldLevels.Count; l++) table.SetLevelRange((WorldLevel)l, levelStart[l], levelCount[l]);

            var parents = new int[count];
            for (int i = 0; i < count; i++)
            {
                var n = ordered[i];
                table.Level[i] = (byte)n.Level;
                int parent = n.Level == WorldLevel.Continent ? -1 : newIndex[n.ParentKey];
                parents[i] = parent;
                table.Parent[i] = parent;
                table.Continent[i] = parent < 0 ? i - levelStart[0] : table.Continent[parent];
            }
            // Children of a parent may sit in different levels (a village can hang off a region);
            // CSR fills in item order, which keeps each child list sorted by index.
            BuildCsr(parents, table.ChildStart, table.ChildCount, table.Children);
            return table;
        }

        public static RouteTable BuildRoutes(MapDef map, NodeTable nodes)
        {
            // Resolve and validate everything before allocating native memory, so bad data cannot leak it.
            int routeCount = map.Routes.Count;
            var from = new int[routeCount];
            var to = new int[routeCount];
            for (int i = 0; i < routeCount; i++)
            {
                var r = map.Routes[i];
                if (!nodes.TryFind(r.FromKey, out var f)) throw new FormatException($"Route from unknown node '{r.FromKey}'.");
                if (!nodes.TryFind(r.ToKey, out var t)) throw new FormatException($"Route to unknown node '{r.ToKey}'.");
                if (r.TravelTurns < 1) throw new FormatException($"Route {r.FromKey}->{r.ToKey}: travel_turns must be >= 1.");
                if (r.CostPerUnit < 0) throw new FormatException($"Route {r.FromKey}->{r.ToKey}: cost_per_unit must be >= 0.");
                from[i] = f.Value;
                to[i] = t.Value;
            }

            var routes = new RouteTable(routeCount, nodes.Count);
            for (int i = 0; i < routeCount; i++)
            {
                routes.From[i] = from[i];
                routes.To[i] = to[i];
                routes.TravelTurns[i] = map.Routes[i].TravelTurns;
                routes.CostPerUnit[i] = map.Routes[i].CostPerUnit;
                routes.BaseFee[i] = map.Routes[i].BaseFee;
            }
            BuildCsr(from, routes.OutStart, routes.OutCount, routes.RouteIndex);
            return routes;
        }

        /// <summary>
        /// Compressed-sparse-row grouping: items of owner o end up in items[start[o] .. start[o] + count[o]),
        /// in ascending item order. Items with a negative owner are skipped.
        /// </summary>
        static void BuildCsr(int[] ownerOfItem, NativeArray<int> start, NativeArray<int> count, NativeArray<int> items)
        {
            var sizes = new int[start.Length];
            foreach (int owner in ownerOfItem)
                if (owner >= 0) sizes[owner]++;
            int offset = 0;
            for (int o = 0; o < start.Length; o++)
            {
                start[o] = offset;
                count[o] = sizes[o];
                offset += sizes[o];
            }
            var cursor = new int[start.Length];
            for (int i = 0; i < ownerOfItem.Length; i++)
            {
                int owner = ownerOfItem[i];
                if (owner < 0) continue;
                items[start[owner] + cursor[owner]++] = i;
            }
        }

        static void Validate(MapNodeDef n, Dictionary<string, MapNodeDef> byKey)
        {
            if (n.Level == WorldLevel.Continent)
            {
                if (!string.IsNullOrEmpty(n.ParentKey)) throw new FormatException($"Continent '{n.Key}' must not have a parent.");
                return;
            }
            if (string.IsNullOrEmpty(n.ParentKey) || !byKey.TryGetValue(n.ParentKey, out var parent))
                throw new FormatException($"Node '{n.Key}' has unknown parent '{n.ParentKey}'.");
            bool ok = n.Level == WorldLevel.Village
                ? parent.Level == WorldLevel.City || parent.Level == WorldLevel.Region
                : parent.Level == n.Level - 1;
            if (!ok) throw new FormatException($"Node '{n.Key}' ({n.Level}) cannot be a child of '{parent.Key}' ({parent.Level}).");
        }
    }

    /// <summary>Registers the world hierarchy and routes. Requires a <see cref="MapDef"/> service.</summary>
    public sealed class WorldModule : ISimModule
    {
        public string Id => "world";
        public IReadOnlyList<string> Requires => Array.Empty<string>();

        public void Register(SimBuilder builder)
        {
            var map = builder.Services.Get<MapDef>();
            var nodes = builder.World.Add(WorldBuilder.BuildNodes(map));
            builder.World.Add(WorldBuilder.BuildRoutes(map, nodes));
            var db = builder.Services.Get<GameDatabase>();
            builder.World.Add(new NodeSpecialties(map, nodes, db));
            builder.World.Add(new NodeTolls(map, nodes, db.Config));
        }
    }
}
