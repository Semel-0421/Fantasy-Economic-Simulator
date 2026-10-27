using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>
    /// Each node's specialty base goods, from its terrain tags (DEC-016). Static, derived from the map and
    /// specialties.csv, so not saved. Node n's specialties are BaseGood[Start[n] .. Start[n] + Count[n]),
    /// sorted.
    /// </summary>
    public sealed class NodeSpecialties : IWorldComponent, IDisposable
    {
        public NativeArray<int> Start;
        public NativeArray<int> Count;
        public NativeArray<int> BaseGood;

        public NodeSpecialties(MapDef map, NodeTable nodes, GameDatabase db)
        {
            var perNode = new SortedSet<int>[nodes.Count];
            int total = 0;
            foreach (var def in map.Nodes)
            {
                if (def.Tags.Length == 0) continue;
                nodes.TryFind(def.Key, out var node);
                var goods = perNode[node.Value] = new SortedSet<int>();
                foreach (var tag in def.Tags)
                {
                    if (!db.TryGetSpecialty(tag, out var rule))
                        throw new FormatException($"map node '{def.Key}': tag '{tag}' is not in {GameDatabase.SpecialtiesFile}.");
                    goods.UnionWith(rule.BaseGoods);
                }
                total += goods.Count;
            }

            Start = new NativeArray<int>(nodes.Count, Allocator.Persistent);
            Count = new NativeArray<int>(nodes.Count, Allocator.Persistent);
            BaseGood = new NativeArray<int>(total, Allocator.Persistent);
            int k = 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                Start[n] = k;
                if (perNode[n] == null) continue;
                foreach (int b in perNode[n]) BaseGood[k++] = b;
                Count[n] = perNode[n].Count;
            }
        }

        public bool IsSpecialty(int node, int baseGood)
        {
            int end = Start[node] + Count[node];
            for (int k = Start[node]; k < end; k++)
                if (BaseGood[k] == baseGood) return true;
            return false;
        }

        public void Dispose()
        {
            Start.DisposeIfCreated();
            Count.DisposeIfCreated();
            BaseGood.DisposeIfCreated();
        }
    }
}
