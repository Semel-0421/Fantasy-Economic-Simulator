using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    /// <summary>
    /// Facilities turn recipe inputs into outputs every week. Outputs add to market supply and inputs to
    /// market demand at the nearest market (the facility's node or an ancestor) that trades the good.
    /// Detailed facilities roll weekly efficiency variance; aggregate ones use the expected value.
    /// Runs as a single-threaded Burst job (facilities share market cells, so order matters for clamping);
    /// each facility's target cells are resolved once and cached until the facility table changes.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem, ILoadListener, IDisposable
    {
        const string SystemId = "agents.production";
        static readonly uint Stream = DetHash.Of(SystemId);

        GameDatabase _db;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        LodState _lod;

        // Flow lines of every facility, outputs then inputs, only those with a market: [LineStart[f], LineStart[f + 1]).
        NativeList<int> _lineStart;
        NativeList<int> _lineCell;
        NativeList<int> _lineModifier;
        NativeList<int> _lineQuantity;
        NativeList<byte> _lineIsDemand;
        int _linesForCount = -1;

        public string Id => SystemId;
        public SimPhase Phase => SimPhase.Production;
        public int Order => SimOrder.Default;

        /// <summary>When true the job runs as plain managed code (parity tests).</summary>
        public bool ForceManaged { get; set; }

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _facilities = ctx.World.Get<FacilityTable>();
            _lod = ctx.World.Get<LodState>();
            _lineStart = new NativeList<int>(Allocator.Persistent);
            _lineCell = new NativeList<int>(Allocator.Persistent);
            _lineModifier = new NativeList<int>(Allocator.Persistent);
            _lineQuantity = new NativeList<int>(Allocator.Persistent);
            _lineIsDemand = new NativeList<byte>(Allocator.Persistent);
        }

        public void OnLoaded(SimContext ctx) => _linesForCount = -1;

        public void Execute(SimContext ctx)
        {
            if (_linesForCount != _facilities.Count) BuildLines();

            var job = new ProductionJob
            {
                Seed = ctx.Seed,
                Turn = ctx.Turn,
                Stream = Stream,
                VariancePermille = _db.Config.ProductionVariancePermille,
                FacilityNode = _facilities.Node.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                FacilityEfficiency = _facilities.Efficiency.AsArray(),
                NodeDetail = _lod.Detail,
                LineStart = _lineStart.AsArray(),
                LineCell = _lineCell.AsArray(),
                LineModifier = _lineModifier.AsArray(),
                LineQuantity = _lineQuantity.AsArray(),
                LineIsDemand = _lineIsDemand.AsArray(),
                ModifierMul = _flows.Multipliers,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();
            // TODO: gate output on input availability and route player-owned output to the player's inventory.
        }

        void BuildLines()
        {
            _lineStart.Clear();
            _lineCell.Clear();
            _lineModifier.Clear();
            _lineQuantity.Clear();
            _lineIsDemand.Clear();
            for (int f = 0; f < _facilities.Count; f++)
            {
                _lineStart.Add(_lineCell.Length);
                var recipe = _db.Facilities[_facilities.DefIndex[f]].Recipe;
                AddLines(_facilities.Node[f], recipe.Outputs, ModifierTarget.Supply);
                AddLines(_facilities.Node[f], recipe.Inputs, ModifierTarget.Demand);
            }
            _lineStart.Add(_lineCell.Length);
            _linesForCount = _facilities.Count;
        }

        void AddLines(int node, RecipeLine[] lines, ModifierTarget target)
        {
            foreach (var line in lines)
            {
                if (!_flows.TryFindNearestMarket(node, line.Good, out int market, out int cell)) continue;
                _lineCell.Add(cell);
                _lineModifier.Add(_flows.MultiplierIndex(market, _db.Good(line.Good).CategoryIndex, target));
                _lineQuantity.Add(line.Quantity);
                _lineIsDemand.Add((byte)(target == ModifierTarget.Demand ? 1 : 0));
            }
        }

        public void Dispose()
        {
            if (_lineStart.IsCreated) _lineStart.Dispose();
            if (_lineCell.IsCreated) _lineCell.Dispose();
            if (_lineModifier.IsCreated) _lineModifier.Dispose();
            if (_lineQuantity.IsCreated) _lineQuantity.Dispose();
            if (_lineIsDemand.IsCreated) _lineIsDemand.Dispose();
        }
    }

    /// <summary>
    /// Weekly facility flows. Same arithmetic as <see cref="MarketFlows"/> (event modifier applied per flow,
    /// cells clamped after every add), in facility order, so results are independent of Burst.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ProductionJob : IJob
    {
        public ulong Seed;
        public int Turn;
        public uint Stream;
        public int VariancePermille;

        [ReadOnly] public NativeArray<int> FacilityNode;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> FacilityEfficiency;
        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> LineStart;
        [ReadOnly] public NativeArray<int> LineCell;
        [ReadOnly] public NativeArray<int> LineModifier;
        [ReadOnly] public NativeArray<int> LineQuantity;
        [ReadOnly] public NativeArray<byte> LineIsDemand;
        [ReadOnly] public NativeArray<long> ModifierMul;

        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        public void Execute()
        {
            for (int f = 0; f < FacilityNode.Length; f++)
            {
                var output = Fx.FromRaw(FacilityEfficiency[f]) * Fx.FromInt(FacilityLevel[f]);
                if (NodeDetail[FacilityNode[f]] == (byte)SimDetail.Detailed)
                    output *= DetRandom.For(Seed, Turn, Stream, f).NextSpread(VariancePermille);
                // Aggregate: expected output, no per-facility randomness or decisions.

                for (int k = LineStart[f]; k < LineStart[f + 1]; k++)
                {
                    long quantity = Fx.MulIntRaw(LineQuantity[k], output.Raw);
                    long scaled = Fx.MulIntRaw(quantity, ModifierMul[LineModifier[k]]);
                    int cell = LineCell[k];
                    if (LineIsDemand[k] == 0) Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + scaled);
                    else Demand[cell] = MarketStore.ClampToNonNegativeInt(Demand[cell] + scaled);
                }
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
