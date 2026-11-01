using System;
using FES.Core;
using FES.Data;

namespace FES.World
{
    /// <summary>
    /// Where banks are (DEC-032, DEC-036): every node from nation down to city level is a branch of its
    /// nation's bank, and each continent has at most one international bank, in a node the map marks.
    /// Static, so not saved.
    /// </summary>
    public sealed class NodeBanks : IWorldComponent
    {
        readonly NodeTable _nodes;
        /// <summary>Per continent: the international bank's node, or -1 when the map marks none.</summary>
        readonly int[] _internationalBank;

        public NodeBanks(MapDef map, NodeTable nodes)
        {
            _nodes = nodes;
            _internationalBank = new int[nodes.ContinentCount];
            for (int c = 0; c < _internationalBank.Length; c++) _internationalBank[c] = -1;
            foreach (var def in map.Nodes)
            {
                if (!def.InternationalBank) continue;
                nodes.TryFind(def.Key, out var node);
                if (!IsBranch(node.Value))
                    throw new FormatException($"map node '{def.Key}': an international bank must sit in a city or above, inside a nation.");
                int continent = nodes.Continent[node.Value];
                if (_internationalBank[continent] >= 0)
                    throw new FormatException($"map node '{def.Key}': its continent already has an international bank.");
                _internationalBank[continent] = node.Value;
            }
        }

        public int NationCount => _nodes.LevelCount(WorldLevel.Nation);

        /// <summary>Dense nation index (0 .. NationCount) of a nation node.</summary>
        public int NationIndex(int nationNode) => nationNode - _nodes.LevelStart(WorldLevel.Nation);
        public int NationNode(int nationIndex) => nationIndex + _nodes.LevelStart(WorldLevel.Nation);

        /// <summary>The nation index of a node, or -1 outside any nation.</summary>
        public int NationOf(int node)
        {
            int nationNode = _nodes.AncestorAt(node, WorldLevel.Nation);
            return nationNode < 0 ? -1 : NationIndex(nationNode);
        }

        /// <summary>The nation index of the bank whose branch is at <paramref name="node"/>, or -1 (villages, continents).</summary>
        public int BranchNation(int node)
        {
            if (!IsBranch(node)) return -1;
            return NationIndex(_nodes.AncestorAt(node, WorldLevel.Nation));
        }

        public bool IsBranch(int node)
        {
            if (_nodes.Absorbed) return _nodes.IsSeat(node); // banks stand at the core towns (DEC-104)
            var level = _nodes.LevelOf(node);
            return level >= WorldLevel.Nation && level <= WorldLevel.City;
        }

        public int InternationalBankOf(int continent) => _internationalBank[continent];

        public bool IsInternationalBank(int node) => _internationalBank[_nodes.Continent[node]] == node;
    }
}
