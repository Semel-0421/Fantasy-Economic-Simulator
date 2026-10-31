using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>
    /// Per-node level of detail for this turn. The <see cref="ILodPolicy"/> is the single authority:
    /// systems only ask <see cref="IsDetailed"/> and never add their own exceptions.
    /// Recomputed every turn (and after a load), so not saved.
    /// </summary>
    public sealed class LodState : IWorldComponent, IDisposable
    {
        public NativeArray<byte> Detail;

        public LodState(int nodeCount) => Detail = new NativeArray<byte>(nodeCount, Allocator.Persistent);

        public bool IsDetailed(int node) => Detail[node] == (byte)SimDetail.Detailed;

        public void Dispose() => Detail.DisposeIfCreated();
    }

    /// <summary>Decides which nodes are simulated per entity. Replace the service to change the rule.</summary>
    public interface ILodPolicy
    {
        void Evaluate(SimContext ctx, LodState lod);
    }

    /// <summary>
    /// Detailed: the subtree of the player's ancestor at the configured focus level (default Region), plus
    /// every node hosting a player-owned facility or merchant. Everything else runs as aggregate flows.
    /// </summary>
    public sealed class DefaultLodPolicy : ILodPolicy
    {
        readonly List<int> _subtree = new List<int>();

        public void Evaluate(SimContext ctx, LodState lod)
        {
            var nodes = ctx.World.Get<NodeTable>();
            var player = ctx.World.Get<PlayerState>();
            var facilities = ctx.World.Get<FacilityTable>();
            var agents = ctx.World.Get<AgentTable>();
            var config = ctx.Services.Get<GameDatabase>().Config;
            var focusLevel = config.LodFocusLevel;

            // Measuring: every node one way, whatever the player does.
            if (config.LodMode != 0)
            {
                for (int n = 0; n < lod.Detail.Length; n++)
                    lod.Detail[n] = (byte)(config.LodMode == 1 ? SimDetail.Detailed : SimDetail.Aggregate);
                return;
            }

            for (int n = 0; n < lod.Detail.Length; n++) lod.Detail[n] = (byte)SimDetail.Aggregate;

            int focus = nodes.AncestorAt(player.Node, focusLevel);
            _subtree.Clear();
            nodes.CollectSubtree(focus >= 0 ? focus : player.Node, _subtree);
            foreach (int n in _subtree) lod.Detail[n] = (byte)SimDetail.Detailed;

            for (int i = 0; i < facilities.Count; i++)
                if (facilities.Owner[i] == OwnerIds.Player) lod.Detail[facilities.Node[i]] = (byte)SimDetail.Detailed;
            for (int i = 0; i < agents.Count; i++)
                if (agents.Owner[i] == OwnerIds.Player) lod.Detail[agents.Node[i]] = (byte)SimDetail.Detailed;
        }
    }

    public sealed class LodSystem : ISimSystem, ILoadListener
    {
        LodState _lod;
        ILodPolicy _policy;

        public string Id => "agents.lod";
        public SimPhase Phase => SimPhase.Lod;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _lod = ctx.World.Get<LodState>();
            _policy = ctx.Services.Get<ILodPolicy>();
        }

        public void Execute(SimContext ctx) => _policy.Evaluate(ctx, _lod);

        public void OnLoaded(SimContext ctx) => _policy.Evaluate(ctx, _lod);
    }
}
