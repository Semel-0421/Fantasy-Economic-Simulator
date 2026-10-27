using System;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>
    /// Danger of every route before cargo and countermeasures (DEC-026): the terrain's chance of an encounter
    /// per trip times the danger factor of the destination's region. Static, so not saved.
    /// </summary>
    public sealed class RouteHazards : IWorldComponent, IDisposable
    {
        /// <summary>Raw Fx chance of an encounter per trip with worthless cargo and no guards.</summary>
        public NativeArray<long> BaseDanger;

        public RouteHazards(MapDef map, NodeTable nodes, RouteTable routes, SimulationConfig config)
        {
            var regionDanger = new long[nodes.Count];
            for (int n = 0; n < nodes.Count; n++) regionDanger[n] = config.RegionDanger.Raw;
            foreach (var def in map.Nodes)
            {
                if (def.DangerPermille < 0) continue;
                nodes.TryFind(def.Key, out var node);
                if (def.Level != WorldLevel.Region)
                    throw new FormatException($"map node '{def.Key}': danger_permille is set per region.");
                regionDanger[node.Value] = Fx.FromPermille(def.DangerPermille).Raw;
            }

            BaseDanger = new NativeArray<long>(routes.Count, Allocator.Persistent);
            for (int r = 0; r < routes.Count; r++)
            {
                int region = nodes.AncestorAt(routes.To[r], WorldLevel.Region);
                long factor = region >= 0 ? regionDanger[region] : config.RegionDanger.Raw;
                BaseDanger[r] = Fx.RoundShift(config.TerrainDanger[routes.Terrain[r]].Raw * factor);
            }
        }

        public void Dispose() => BaseDanger.DisposeIfCreated();
    }
}
