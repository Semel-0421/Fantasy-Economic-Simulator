using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Facilities turn recipe inputs into outputs every week. Outputs add to market supply and inputs to
    /// market demand at the nearest market (the facility's node or an ancestor) that trades the good.
    /// Detailed facilities roll weekly efficiency variance; aggregate ones use the expected value.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem
    {
        const string SystemId = "agents.production";
        static readonly uint Stream = DetHash.Of(SystemId);

        GameDatabase _db;
        MarketFlows _flows;
        FacilityTable _facilities;
        LodState _lod;

        public string Id => SystemId;
        public SimPhase Phase => SimPhase.Production;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _flows = ctx.World.Get<MarketFlows>();
            _facilities = ctx.World.Get<FacilityTable>();
            _lod = ctx.World.Get<LodState>();
        }

        public void Execute(SimContext ctx)
        {
            int variance = _db.Config.ProductionVariancePermille;
            for (int f = 0; f < _facilities.Count; f++)
            {
                int node = _facilities.Node[f];
                var output = Fx.FromRaw(_facilities.Efficiency[f]) * Fx.FromInt(_facilities.Level[f]);
                if (_lod.IsDetailed(node))
                    output *= ctx.RandomFor(Stream, f).NextSpread(variance);
                // Aggregate: expected output, no per-facility randomness or decisions.

                var recipe = _db.Facilities[_facilities.DefIndex[f]].Recipe;
                foreach (var line in recipe.Outputs)
                    if (_flows.TryFindNearestMarket(node, line.Good, out int market, out int cell))
                        _flows.AddSupply(market, cell, output.MulInt(line.Quantity));
                foreach (var line in recipe.Inputs)
                    if (_flows.TryFindNearestMarket(node, line.Good, out int market, out int cell))
                        _flows.AddDemand(market, cell, output.MulInt(line.Quantity));
                // TODO: gate output on input availability and route player-owned output to the player's inventory.
            }
        }
    }

    /// <summary>How merchant groups trade. Replace the service to plug in real merchant AI.</summary>
    public interface IMerchantBehavior
    {
        /// <summary>One merchant group decides, buys, travels and sells.</summary>
        void RunDetailed(SimContext ctx, AgentId merchant);

        /// <summary>All merchants of an aggregate node, summarized as node-level trade flows.</summary>
        void RunAggregate(SimContext ctx, NodeId node);
    }

    /// <summary>Placeholder until merchant trading is designed: merchants exist but do not trade yet.</summary>
    public sealed class IdleMerchantBehavior : IMerchantBehavior
    {
        public void RunDetailed(SimContext ctx, AgentId merchant) { }
        public void RunAggregate(SimContext ctx, NodeId node) { }
    }

    /// <summary>Dispatches each merchant of a detailed node, and each aggregate node, to the merchant behavior.</summary>
    public sealed class MerchantSystem : ISimSystem
    {
        NodeTable _nodes;
        AgentTable _agents;
        LodState _lod;
        IMerchantBehavior _behavior;

        public string Id => "agents.merchants";
        public SimPhase Phase => SimPhase.Agents;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _nodes = ctx.World.Get<NodeTable>();
            _agents = ctx.World.Get<AgentTable>();
            _lod = ctx.World.Get<LodState>();
            _behavior = ctx.Services.Get<IMerchantBehavior>();
        }

        public void Execute(SimContext ctx)
        {
            for (int a = 0; a < _agents.Count; a++)
                if (_lod.IsDetailed(_agents.Node[a])) _behavior.RunDetailed(ctx, new AgentId(a));
            for (int n = 0; n < _nodes.Count; n++)
                if (!_lod.IsDetailed(n)) _behavior.RunAggregate(ctx, new NodeId(n));
        }
    }
}
