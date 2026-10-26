using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;

namespace FES.Economy
{
    /// <summary>
    /// The single entry point for adding supply and demand to markets. Applies the active event modifiers
    /// (folded per market, category and target) at the moment a flow happens, so every modifier affects
    /// exactly the turns it is active, whatever the market's repricing cadence.
    /// Derived from <see cref="MarketModifierTable"/> each turn, so not saved.
    /// </summary>
    public sealed class MarketFlows : IWorldComponent, IDisposable
    {
        readonly MarketStore _store;
        readonly NodeTable _nodes;
        readonly List<int> _subtree = new List<int>();

        public readonly int CategoryCount;
        /// <summary>Multipliers (raw Q16) indexed ((market * CategoryCount) + category) * ModifierTargets.Count + target.</summary>
        public NativeArray<long> Multipliers;

        public MarketFlows(MarketStore store, NodeTable nodes, int categoryCount)
        {
            _store = store;
            _nodes = nodes;
            CategoryCount = Math.Max(1, categoryCount);
            Multipliers = new NativeArray<long>(nodes.Count * CategoryCount * ModifierTargets.Count, Allocator.Persistent);
            for (int i = 0; i < Multipliers.Length; i++) Multipliers[i] = Fx.OneRaw;
        }

        public int MultiplierIndex(int market, int category, ModifierTarget target) =>
            (market * CategoryCount + category) * ModifierTargets.Count + (int)target;

        public long Multiplier(int market, int category, ModifierTarget target) => Multipliers[MultiplierIndex(market, category, target)];

        public void AddSupply(int market, int cell, long quantity) => Add(market, cell, quantity, ModifierTarget.Supply);
        public void AddDemand(int market, int cell, long quantity) => Add(market, cell, quantity, ModifierTarget.Demand);

        void Add(int market, int cell, long quantity, ModifierTarget target)
        {
            int category = _store.GoodCategory[_store.CellGood[cell]];
            long scaled = Fx.FromRaw(Multiplier(market, category, target)).MulInt(quantity);
            if (target == ModifierTarget.Supply) _store.AddSupplyRaw(cell, scaled);
            else _store.AddDemandRaw(cell, scaled);
        }

        /// <summary>Finds the nearest market (the node itself, then its ancestors) that trades <paramref name="good"/>.</summary>
        public bool TryFindNearestMarket(int node, GoodId good, out int market, out int cell)
        {
            for (market = node; market >= 0; market = _nodes.Parent[market])
                if (_store.TryFindCell(market, good, out cell)) return true;
            cell = -1;
            return false;
        }

        /// <summary>Recomputes all multipliers from the active modifiers.</summary>
        public void Rebuild(MarketModifierTable modifiers)
        {
            for (int i = 0; i < Multipliers.Length; i++) Multipliers[i] = Fx.OneRaw;
            foreach (var mod in modifiers.Active)
            {
                int firstCategory = mod.Category < 0 ? 0 : mod.Category;
                int lastCategory = mod.Category < 0 ? CategoryCount - 1 : mod.Category;
                _subtree.Clear();
                _nodes.CollectSubtree(mod.Node, _subtree);
                foreach (int n in _subtree)
                {
                    for (int c = firstCategory; c <= lastCategory; c++)
                    {
                        int index = (n * CategoryCount + c) * ModifierTargets.Count + (int)mod.Target;
                        Multipliers[index] = Fx.RoundShift(Multipliers[index] * mod.Multiplier.Raw);
                    }
                }
            }
        }

        public void Dispose() => Multipliers.DisposeIfCreated();
    }

    /// <summary>Refolds modifiers after world events changed them, before any flow of the turn is added.</summary>
    public sealed class ModifierFoldSystem : ISimSystem, ILoadListener
    {
        MarketFlows _flows;
        MarketModifierTable _modifiers;

        public string Id => "economy.modifier_fold";
        public SimPhase Phase => SimPhase.Environment;
        public int Order => SimOrder.Late;

        public void Initialize(SimContext ctx)
        {
            _flows = ctx.World.Get<MarketFlows>();
            _modifiers = ctx.World.Get<MarketModifierTable>();
        }

        public void Execute(SimContext ctx) => _flows.Rebuild(_modifiers);

        public void OnLoaded(SimContext ctx) => _flows.Rebuild(_modifiers);
    }
}
