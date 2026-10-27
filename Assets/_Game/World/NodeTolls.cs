using System;
using FES.Core;
using FES.Data;
using Unity.Collections;

namespace FES.World
{
    /// <summary>
    /// Toll rates of every node (DEC-014): a share of the value of cargo entering it, plus a surcharge on
    /// goods in its specialty categories. From the map, with config defaults; not saved. The rates in force
    /// are the map's times the nation's toll policy (DEC-058), which its owner reapplies after loading.
    /// </summary>
    public sealed class NodeTolls : IWorldComponent, IDisposable
    {
        /// <summary>Raw Fx shares of the cargo value, in force.</summary>
        public NativeArray<long> Tariff;
        public NativeArray<long> SpecialtySurcharge;
        readonly long[] _baseTariff;
        readonly long[] _baseSurcharge;

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
            _baseTariff = Tariff.ToArray();
            _baseSurcharge = SpecialtySurcharge.ToArray();
        }

        /// <summary>Sets the rates of every node inside <paramref name="nationNode"/> to the map's times <paramref name="scale"/>.</summary>
        public void ScaleNation(NodeTable nodes, int nationNode, Fx scale)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                if (nodes.AncestorAt(n, WorldLevel.Nation) != nationNode) continue;
                Tariff[n] = scale.MulInt(_baseTariff[n]);
                SpecialtySurcharge[n] = scale.MulInt(_baseSurcharge[n]);
            }
        }

        public void Dispose()
        {
            Tariff.DisposeIfCreated();
            SpecialtySurcharge.DisposeIfCreated();
        }
    }
}
