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
    /// A facility ran out of cash and got a new NPC operator; a player-owned one is lost (DEC-028, DEC-035),
    /// and the player can empty its warehouse until <see cref="WarehouseClaimUntil"/> (DEC-052).
    /// </summary>
    public sealed class FacilityBankrupt : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly bool WasPlayerOwned;
        /// <summary>Last turn the player can take goods out of the lost warehouse, or -1.</summary>
        public readonly int WarehouseClaimUntil;
        public readonly int Turn;

        public FacilityBankrupt(FacilityId facility, bool wasPlayerOwned, int warehouseClaimUntil, int turn)
        {
            Facility = facility;
            WasPlayerOwned = wasPlayerOwned;
            WarehouseClaimUntil = warehouseClaimUntil;
            Turn = turn;
        }
    }

    /// <summary>The claim on a lost facility's warehouse ran out; what was left in it is gone.</summary>
    public sealed class LostWarehouseCleared : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly int Turn;

        public LostWarehouseCleared(FacilityId facility, int turn)
        {
            Facility = facility;
            Turn = turn;
        }
    }

    /// <summary>
    /// Facilities turn recipe inputs into outputs every week (SYS-PRODUCTION). Inputs are bought from the
    /// stock of the nearest market (the facility's node or an ancestor) that trades each good, paid from the
    /// facility's cash, all or nothing: one input short of stock or cash and nothing is made. NPC output is
    /// sold to the nearest market at its current price; player-owned output goes to the facility's
    /// warehouse, and what does not fit is sold. Then every facility pays its weekly upkeep, which can push
    /// its cash below zero; after <see cref="SimulationConfig.FacilityBankruptWeeks"/> such weeks in a row it
    /// goes bankrupt and gets a new NPC operator (DEC-051).
    /// Detailed facilities roll weekly efficiency variance; aggregate ones use the expected value.
    /// Runs as a single-threaded Burst job (facilities share stock, so order decides who is served first);
    /// each facility's lines are resolved once and cached until the facility table changes.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem, ILoadListener, IDisposable
    {
        const string SystemId = "agents.production";
        static readonly uint Stream = DetHash.Of(SystemId);

        GameDatabase _db;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        CargoTable _cargo;
        LodState _lod;

        // Lines of every facility, inputs then outputs: [LineStart[f], LineStart[f + 1]). Cell -1 = no market.
        NativeList<int> _lineStart;
        NativeList<int> _lineCell;
        NativeList<int> _lineGood;
        NativeList<int> _lineModifier;
        NativeList<int> _lineQuantity;
        NativeList<byte> _lineIsInput;
        int _linesForCount = -1;

        NativeList<int> _playerOutputLine;
        NativeList<int> _playerOutputQuantity;
        NativeList<int> _bankrupt;
        NativeArray<long> _defUpkeep;

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
            _cargo = ctx.World.Get<CargoTable>();
            _lod = ctx.World.Get<LodState>();
            _lineStart = new NativeList<int>(Allocator.Persistent);
            _lineCell = new NativeList<int>(Allocator.Persistent);
            _lineGood = new NativeList<int>(Allocator.Persistent);
            _lineModifier = new NativeList<int>(Allocator.Persistent);
            _lineQuantity = new NativeList<int>(Allocator.Persistent);
            _lineIsInput = new NativeList<byte>(Allocator.Persistent);
            _playerOutputLine = new NativeList<int>(Allocator.Persistent);
            _playerOutputQuantity = new NativeList<int>(Allocator.Persistent);
            _bankrupt = new NativeList<int>(Allocator.Persistent);
            _defUpkeep = new NativeArray<long>(_db.Facilities.Count, Allocator.Persistent);
            for (int d = 0; d < _db.Facilities.Count; d++) _defUpkeep[d] = _db.Config.FacilityUpkeep[(int)_db.Facilities[d].Tier];
        }

        public void OnLoaded(SimContext ctx)
        {
            _linesForCount = -1;
            // Saves from before facility cash (FacilityTableV1Migration) start every facility at its tier's cash.
            for (int f = 0; f < _facilities.Count; f++)
                if (_facilities.Cash[f] == FacilityTableV1Migration.UnsetCash) _facilities.Cash[f] = StartCash(f);
        }

        public void Execute(SimContext ctx)
        {
            if (_linesForCount != _facilities.Count) BuildLines();
            _playerOutputLine.Clear();
            _playerOutputQuantity.Clear();
            _bankrupt.Clear();

            var job = new ProductionJob
            {
                Seed = ctx.Seed,
                Turn = ctx.Turn,
                Stream = Stream,
                VariancePermille = _db.Config.ProductionVariancePermille,
                FacilityNode = _facilities.Node.AsArray(),
                FacilityOwner = _facilities.Owner.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                FacilityEfficiency = _facilities.Efficiency.AsArray(),
                FacilityCash = _facilities.Cash.AsArray(),
                NodeDetail = _lod.Detail,
                LineStart = _lineStart.AsArray(),
                LineCell = _lineCell.AsArray(),
                LineModifier = _lineModifier.AsArray(),
                LineQuantity = _lineQuantity.AsArray(),
                LineIsInput = _lineIsInput.AsArray(),
                ModifierMul = _flows.Multipliers,
                Price = _markets.Price,
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
                PlayerOutputLine = _playerOutputLine,
                PlayerOutputQuantity = _playerOutputQuantity,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();

            // Upkeep after every sale of the week, the warehouse overflow included.
            StorePlayerOutput();
            var upkeep = new FacilityUpkeepJob
            {
                BankruptWeeks = _db.Config.FacilityBankruptWeeks,
                FacilityDefIndex = _facilities.DefIndex.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                DefUpkeep = _defUpkeep,
                FacilityCash = _facilities.Cash.AsArray(),
                FacilityNegativeCashWeeks = _facilities.NegativeCashWeeks.AsArray(),
                Bankrupt = _bankrupt,
            };
            if (ForceManaged) upkeep.Execute();
            else upkeep.Schedule().Complete();

            foreach (int f in _bankrupt) GoBankrupt(ctx, f);
            ExpireWarehouseClaims(ctx);
        }

        // Player-owned output, in facility order: into the site's warehouse, the overflow sold (DEC-005, DEC-029).
        void StorePlayerOutput()
        {
            var capacity = new CargoCapacity(_db.Config.WarehouseMaxWeight, _db.Config.WarehouseMaxSize);
            for (int i = 0; i < _playerOutputLine.Length; i++)
            {
                int line = _playerOutputLine[i];
                int f = FacilityOfLine(line);
                var good = new GoodId(_lineGood[line]);
                int holder = CargoHolders.Warehouse(new FacilityId(f));
                var load = _cargo.LoadOf(holder, _markets.GoodWeight, _markets.GoodSize);
                int quantity = _playerOutputQuantity[i];
                int stored = Math.Min(quantity, capacity.MaxAdditional(load, _markets.GoodWeight[good.Value], _markets.GoodSize[good.Value]));
                _cargo.Change(holder, good, stored);

                int cell = _lineCell[line];
                int overflow = quantity - stored;
                if (overflow == 0 || cell < 0) continue; // no market for the good: the overflow is lost
                _flows.Sell(cell, overflow);
                _facilities.Cash[f] += (long)overflow * _markets.Price[cell];
            }
        }

        // A new NPC operator takes over the same row, so facility ids stay stable.
        void GoBankrupt(SimContext ctx, int f)
        {
            bool wasPlayer = _facilities.Owner[f] == OwnerIds.Player;
            int claimUntil = wasPlayer ? ctx.Turn + _db.Config.LostWarehouseWeeks : -1;
            _facilities.Owner[f] = OwnerIds.Npc;
            _facilities.Mode[f] = (byte)ControlMode.Automated;
            _facilities.Cash[f] = StartCash(f);
            _facilities.NegativeCashWeeks[f] = 0;
            _facilities.WarehouseClaimUntil[f] = claimUntil;
            ctx.Events.Publish(new FacilityBankrupt(new FacilityId(f), wasPlayer, claimUntil, ctx.Turn));
        }

        // After the claim period, whatever the player left in a lost warehouse is gone (DEC-052).
        void ExpireWarehouseClaims(SimContext ctx)
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                int until = _facilities.WarehouseClaimUntil[f];
                if (until < 0 || ctx.Turn < until) continue;
                _facilities.WarehouseClaimUntil[f] = -1;
                _cargo.Clear(CargoHolders.Warehouse(new FacilityId(f)));
                ctx.Events.Publish(new LostWarehouseCleared(new FacilityId(f), ctx.Turn));
            }
        }

        long StartCash(int f) => _db.Config.FacilityStartCash[(int)_db.Facilities[_facilities.DefIndex[f]].Tier];

        int FacilityOfLine(int line)
        {
            // Lines are sorted by facility: binary-search the start offsets.
            int lo = 0, hi = _lineStart.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_lineStart[mid] <= line) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        void BuildLines()
        {
            _lineStart.Clear();
            _lineCell.Clear();
            _lineGood.Clear();
            _lineModifier.Clear();
            _lineQuantity.Clear();
            _lineIsInput.Clear();
            for (int f = 0; f < _facilities.Count; f++)
            {
                _lineStart.Add(_lineCell.Length);
                var recipe = _db.Facilities[_facilities.DefIndex[f]].Recipe;
                AddLines(_facilities.Node[f], recipe.Inputs, true);
                AddLines(_facilities.Node[f], recipe.Outputs, false);
            }
            _lineStart.Add(_lineCell.Length);
            _linesForCount = _facilities.Count;
        }

        void AddLines(int node, RecipeLine[] lines, bool isInput)
        {
            foreach (var line in lines)
            {
                bool found = _flows.TryFindNearestMarket(node, line.Good, out int market, out int cell);
                _lineCell.Add(found ? cell : -1);
                _lineGood.Add(line.Good.Value);
                // Inputs are purchases from stock and, like the player's, are not scaled by event modifiers.
                _lineModifier.Add(found && !isInput ? _flows.MultiplierIndex(market, _db.Good(line.Good).CategoryIndex, ModifierTarget.Supply) : -1);
                _lineQuantity.Add(line.Quantity);
                _lineIsInput.Add((byte)(isInput ? 1 : 0));
            }
        }

        public void Dispose()
        {
            if (_lineStart.IsCreated) _lineStart.Dispose();
            if (_lineCell.IsCreated) _lineCell.Dispose();
            if (_lineGood.IsCreated) _lineGood.Dispose();
            if (_lineModifier.IsCreated) _lineModifier.Dispose();
            if (_lineQuantity.IsCreated) _lineQuantity.Dispose();
            if (_lineIsInput.IsCreated) _lineIsInput.Dispose();
            if (_playerOutputLine.IsCreated) _playerOutputLine.Dispose();
            if (_playerOutputQuantity.IsCreated) _playerOutputQuantity.Dispose();
            if (_bankrupt.IsCreated) _bankrupt.Dispose();
            _defUpkeep.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Weekly facility production in facility order, so results are independent of Burst. Two passes per
    /// facility: check every input's stock and the total cost, then buy, pay and produce. A purchase takes
    /// units out of the stock and passes them through supply into demand, like <see cref="MarketFlows.Buy"/>.
    /// Cells are clamped after every add, like <see cref="MarketFlows"/>.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ProductionJob : IJob
    {
        public ulong Seed;
        public int Turn;
        public uint Stream;
        public int VariancePermille;

        [ReadOnly] public NativeArray<int> FacilityNode;
        [ReadOnly] public NativeArray<int> FacilityOwner;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> FacilityEfficiency;
        public NativeArray<long> FacilityCash;
        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> LineStart;
        [ReadOnly] public NativeArray<int> LineCell;
        [ReadOnly] public NativeArray<int> LineModifier;
        [ReadOnly] public NativeArray<int> LineQuantity;
        [ReadOnly] public NativeArray<byte> LineIsInput;
        [ReadOnly] public NativeArray<long> ModifierMul;
        [ReadOnly] public NativeArray<int> Price;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        /// <summary>Output of player-owned facilities (line, quantity), stored by the system after the job.</summary>
        public NativeList<int> PlayerOutputLine;
        public NativeList<int> PlayerOutputQuantity;

        public void Execute()
        {
            for (int f = 0; f < FacilityNode.Length; f++)
            {
                var output = Fx.FromRaw(FacilityEfficiency[f]) * Fx.FromInt(FacilityLevel[f]);
                if (NodeDetail[FacilityNode[f]] == (byte)SimDetail.Detailed)
                    output *= DetRandom.For(Seed, Turn, Stream, f).NextSpread(VariancePermille);
                // Aggregate: expected output, no per-facility randomness or decisions.

                int start = LineStart[f], end = LineStart[f + 1];
                bool inStock = true;
                long cost = 0;
                for (int k = start; k < end && LineIsInput[k] != 0; k++)
                {
                    int cell = LineCell[k];
                    long need = Fx.MulIntRaw(LineQuantity[k], output.Raw);
                    if (cell < 0 || Stock[cell] < need)
                    {
                        inStock = false;
                        break;
                    }
                    cost += need * Price[cell];
                }
                if (!inStock || cost > FacilityCash[f]) continue;
                FacilityCash[f] -= cost;

                bool playerOwned = FacilityOwner[f] == OwnerIds.Player;
                for (int k = start; k < end; k++)
                {
                    int cell = LineCell[k];
                    long quantity = Fx.MulIntRaw(LineQuantity[k], output.Raw);
                    if (LineIsInput[k] != 0)
                    {
                        Stock[cell] -= (int)quantity;
                        Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + quantity);
                        Demand[cell] = MarketStore.ClampToNonNegativeInt(Demand[cell] + quantity);
                        continue;
                    }
                    if (cell >= 0) quantity = Fx.MulIntRaw(quantity, ModifierMul[LineModifier[k]]);
                    int clamped = MarketStore.ClampToNonNegativeInt(quantity);
                    if (playerOwned)
                    {
                        if (clamped == 0) continue;
                        PlayerOutputLine.Add(k);
                        PlayerOutputQuantity.Add(clamped);
                    }
                    else if (cell >= 0)
                    {
                        Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + clamped);
                        FacilityCash[f] += (long)clamped * Price[cell];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Weekly upkeep (wages) of every facility, scaled by its level; cash may go below zero. A facility whose
    /// cash ended <see cref="BankruptWeeks"/> weeks in a row below zero is listed as bankrupt (DEC-051).
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct FacilityUpkeepJob : IJob
    {
        public int BankruptWeeks;

        [ReadOnly] public NativeArray<int> FacilityDefIndex;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> DefUpkeep;
        public NativeArray<long> FacilityCash;
        public NativeArray<int> FacilityNegativeCashWeeks;
        public NativeList<int> Bankrupt;

        public void Execute()
        {
            for (int f = 0; f < FacilityCash.Length; f++)
            {
                FacilityCash[f] -= DefUpkeep[FacilityDefIndex[f]] * FacilityLevel[f];
                if (FacilityCash[f] >= 0) FacilityNegativeCashWeeks[f] = 0;
                else if (++FacilityNegativeCashWeeks[f] >= BankruptWeeks) Bankrupt.Add(f);
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
