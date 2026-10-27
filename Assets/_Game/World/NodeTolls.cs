using System;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>
    /// Toll rates of every node (DEC-014): a share of the value of cargo entering it, plus a surcharge on
    /// goods in its specialty categories. From the map, with config defaults; static, so not saved.
    /// </summary>
    public sealed class NodeTolls : IWorldComponent, IDisposable
    {
        /// <summary>Raw Fx shares of the cargo value.</summary>
        public NativeArray<long> Tariff;
        public NativeArray<long> SpecialtySurcharge;

        public NodeTolls(MapDef map, NodeTable nodes, SimulationConfig config)
        {
            Tariff = new NativeArray<long>(nodes.Count, Allocator.Persistent);
            SpecialtySurcharge = new NativeArray<long>(nodes.Count, Allocator.Persistent);
            foreach (var def in map.Nodes)
            {
                if (def.TariffPermille < -1 || def.SpecialtySurchargePermille < -1)
                    throw new FormatException($"map node '{def.Key}': toll rates must not be negative.");
                nodes.TryFind(def.Key, out var node);
                Tariff[node.Value] = (def.TariffPermille < 0 ? config.TollRate : Fx.FromPermille(def.TariffPermille)).Raw;
                SpecialtySurcharge[node.Value] = (def.SpecialtySurchargePermille < 0
                    ? config.TollSpecialtySurcharge
                    : Fx.FromPermille(def.SpecialtySurchargePermille)).Raw;
            }
        }

        public void Dispose()
        {
            Tariff.DisposeIfCreated();
            SpecialtySurcharge.DisposeIfCreated();
        }
    }
}
