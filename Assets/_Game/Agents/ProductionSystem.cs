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
    /// goes bankrupt and gets a new NPC operator (DEC-051). An NPC facility pays a share of its cash above
    /// 26 weeks of costs to its group as dividends; the group passes most of them on (DEC-061).
    /// Facilities making one of their node's specialties produce more, partly one grade higher (DEC-016).
    /// Detailed facilities roll weekly efficiency variance; aggregate ones use the expected value.
    /// Runs as a single-threaded Burst job (facilities share stock, so order decides who is served first);
    /// each facility's lines are resolved once and cached until the facility table changes.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem, ILoadListener, IDisposable
    {
        const string SystemId = "agents.production";
        static readonly uint Stream = DetHash.Of(SystemId);
        static readonly uint GradeStream = DetHash.Of("agents.production.grade");

        GameDatabase _db;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        CargoTable _cargo;
        LodState _lod;
        NodeSpecialties _specialties;
        GroupDirectory _groups;

        // Lines of every facility, inputs then outputs: [LineStart[f], LineStart[f + 1]). Cell -1 = no market.
        NativeList<int> _lineStart;
        NativeList<int> _lineCell;
        NativeList<int> _lineGood;
        NativeList<int> _lineModifier;
        NativeList<int> _lineQuantity;
        NativeList<byte> _lineIsInput;
        // Output lines only: the specialty multiplier (raw Fx, 1.0 otherwise) and the one-grade-higher
        // good with its market cell and modifier (-1 when the line has no grade-up).
        NativeList<long> _lineOutputMul;
        NativeList<int> _lineUpGood;
        NativeList<int> _lineUpCell;
        NativeList<int> _lineUpModifier;
        int _linesForCount = -1;

        NativeList<int> _playerOutputFacility;
        NativeList<int> _playerOutputGood;
        NativeList<int> _playerOutputCell;
        NativeList<int> _playerOutputQuantity;
        NativeList<int> _bankrupt;
        NativeList<long> _inputCost;
        NativeList<long> _groupDividend;
        NativeArray<long> _defUpkeep;
        GroupTable _groupTable;
        WorldState _world;
        PurseState _purses;
        HouseholdCash _households;

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
            _purses = ctx.World.Get<PurseState>();
            _households = ctx.World.Get<HouseholdCash>();
            _lod = ctx.World.Get<LodState>();
            _specialties = ctx.World.Get<NodeSpecialties>();
            _groups = new GroupDirectory(ctx.World.Get<GroupTable>(), ctx.World.Get<NodeTable>());
            _lineStart = new NativeList<int>(Allocator.Persistent);
            _lineCell = new NativeList<int>(Allocator.Persistent);
            _lineGood = new NativeList<int>(Allocator.Persistent);
            _lineModifier = new NativeList<int>(Allocator.Persistent);
            _lineQuantity = new NativeList<int>(Allocator.Persistent);
            _lineIsInput = new NativeList<byte>(Allocator.Persistent);
            _lineOutputMul = new NativeList<long>(Allocator.Persistent);
            _lineUpGood = new NativeList<int>(Allocator.Persistent);
            _lineUpCell = new NativeList<int>(Allocator.Persistent);
            _lineUpModifier = new NativeList<int>(Allocator.Persistent);
            _playerOutputFacility = new NativeList<int>(Allocator.Persistent);
            _playerOutputGood = new NativeList<int>(Allocator.Persistent);
            _playerOutputCell = new NativeList<int>(Allocator.Persistent);
            _playerOutputQuantity = new NativeList<int>(Allocator.Persistent);
            _bankrupt = new NativeList<int>(Allocator.Persistent);
            _inputCost = new NativeList<long>(Allocator.Persistent);
            _groupDividend = new NativeList<long>(Allocator.Persistent);
            _groupTable = ctx.World.Get<GroupTable>();
            _world = ctx.World;
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
            _playerOutputFacility.Clear();
            _playerOutputGood.Clear();
            _playerOutputCell.Clear();
            _playerOutputQuantity.Clear();
            _bankrupt.Clear();
            _inputCost.ResizeUninitialized(_facilities.Count);
            _groupDividend.ResizeUninitialized(_groupTable.Count);
            for (int g = 0; g < _groupDividend.Length; g++) _groupDividend[g] = 0;

            var job = new ProductionJob
            {
                Seed = ctx.Seed,
                Turn = ctx.Turn,
                Stream = Stream,
                GradeStream = GradeStream,
                VariancePermille = _db.Config.ProductionVariancePermille,
                GradeUpChanceRaw = _db.Config.SpecialtyGradeUpChance.Raw,
                FacilityNode = _facilities.Node.AsArray(),
                FacilityOwner = _facilities.Owner.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                FacilityEfficiency = _facilities.Efficiency.AsArray(),
                NodeFloat = _purses.NodeFloat,
                Float = _purses.Float,
                FacilityCash = _facilities.Cash.AsArray(),
                NodeDetail = _lod.Detail,
                LineStart = _lineStart.AsArray(),
                LineCell = _lineCell.AsArray(),
                LineGood = _lineGood.AsArray(),
                LineModifier = _lineModifier.AsArray(),
                LineQuantity = _lineQuantity.AsArray(),
                LineIsInput = _lineIsInput.AsArray(),
                LineOutputMul = _lineOutputMul.AsArray(),
                LineUpGood = _lineUpGood.AsArray(),
                LineUpCell = _lineUpCell.AsArray(),
                LineUpModifier = _lineUpModifier.AsArray(),
                ModifierMul = _flows.Multipliers,
                Price = _markets.Price,
                Stock = _markets.Stock,
                Supply = _markets.Supply,
                Demand = _markets.Demand,
                PlayerOutputFacility = _playerOutputFacility,
                PlayerOutputGood = _playerOutputGood,
                PlayerOutputCell = _playerOutputCell,
                PlayerOutputQuantity = _playerOutputQuantity,
                FacilityInputCost = _inputCost.AsArray(),
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();

            // Upkeep after every sale of the week, the warehouse overflow included.
            StorePlayerOutput();
            var upkeep = new FacilityUpkeepJob
            {
                BankruptWeeks = _db.Config.FacilityBankruptWeeks,
                DividendBufferWeeks = _db.Config.DividendBufferWeeks,
                DividendShareRaw = _db.Config.DividendShare.Raw,
                FacilityDefIndex = _facilities.DefIndex.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                DefUpkeep = _defUpkeep,
                FacilityCash = _facilities.Cash.AsArray(),
                FacilityNegativeCashWeeks = _facilities.NegativeCashWeeks.AsArray(),
                FacilityNode = _facilities.Node.AsArray(),
                HouseholdCash = _households.Cash,
                Bankrupt = _bankrupt,
                FacilityOwner = _facilities.Owner.AsArray(),
                FacilityGroup = _facilities.Group.AsArray(),
                FacilityInputCost = _inputCost.AsArray(),
                GroupDividend = _groupDividend.AsArray(),
            };
            if (ForceManaged) upkeep.Execute();
            else upkeep.Schedule().Complete();
            PassOnDividends();

            foreach (int f in _bankrupt) GoBankrupt(ctx, f);
            ExpireWarehouseClaims(ctx);
        }

        // Player-owned output, in facility order: into the site's warehouse, the overflow sold (DEC-005, DEC-029).
        void StorePlayerOutput()
        {
            var capacity = new CargoCapacity(_db.Config.WarehouseMaxWeight, _db.Config.WarehouseMaxSize);
            for (int i = 0; i < _playerOutputFacility.Length; i++)
            {
                int f = _playerOutputFacility[i];
                var good = new GoodId(_playerOutputGood[i]);
                int holder = CargoHolders.Warehouse(new FacilityId(f));
                var load = _cargo.LoadOf(holder, _markets.GoodWeight, _markets.GoodSize);
                int quantity = _playerOutputQuantity[i];
                int stored = Math.Min(quantity, capacity.MaxAdditional(load, _markets.GoodWeight[good.Value], _markets.GoodSize[good.Value]));
                _cargo.Change(holder, good, stored);

                int cell = _playerOutputCell[i];
                int overflow = quantity - stored;
                if (overflow == 0 || cell < 0) continue; // no market for the good: the overflow is lost
                // As much as the traders can pay for is sold; the rest is lost too (DEC-061).
                overflow = _purses.Sellable(_facilities.Node[f], overflow, _markets.Price[cell]);
                if (overflow == 0) continue;
                _flows.Sell(cell, overflow);
                _facilities.Cash[f] += (long)overflow * _markets.Price[cell];
                _purses.ChangeFloat(_facilities.Node[f], -(long)overflow * _markets.Price[cell]);
            }
        }

        // A group keeps part of its dividends and passes the rest to the households of its home node; a state
        // enterprise's go to the state (DEC-061; [Draft] household cash is one pot per node, not per quintile).
        void PassOnDividends()
        {
            var share = _db.Config.GroupPayoutShare;
            for (int g = 0; g < _groupDividend.Length; g++)
            {
                long dividend = _groupDividend[g];
                if (dividend == 0) continue;
                long passed = share.MulInt(dividend);
                if (_groupTable.KindOf(g) == GroupKind.StateEnterprise) MoneyLedger.ToState(_world, _groupTable.Node[g], passed);
                else _purses.PayHouseholds(_groupTable.Node[g], passed);
                _groupTable.Cash[g] += dividend - passed;
            }
        }

        // A new NPC operator takes over the same row, so facility ids stay stable.
        void GoBankrupt(SimContext ctx, int f)
        {
            bool wasPlayer = _facilities.Owner[f] == OwnerIds.Player;
            // A bankrupt NPC facility with a bank loan goes to the bank's auction instead (DEC-037).
            if (!wasPlayer && ctx.World.Get<NpcLoanTable>().Find(NpcBorrowers.Facility(f)) >= 0)
            {
                NpcLending.SeizeFacility(ctx, f);
                return;
            }
            int claimUntil = wasPlayer ? ctx.Turn + _db.Config.LostWarehouseWeeks : -1;
            _facilities.Owner[f] = OwnerIds.Npc;
            _facilities.Mode[f] = (byte)ControlMode.Automated;
            // [Draft] An NPC group keeps its facility under a new manager; a player's goes to a local family.
            if (wasPlayer) _facilities.Group[f] = _groups.PrivateAt(_facilities.Node[f], _db.Config);
            _facilities.Cash[f] = MoneyLedger.FundNewOperator(ctx.World, _facilities.Group[f], _facilities.Node[f], _facilities.Cash[f], StartCash(f));
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

        void BuildLines()
        {
            _lineStart.Clear();
            _lineCell.Clear();
            _lineGood.Clear();
            _lineModifier.Clear();
            _lineQuantity.Clear();
            _lineIsInput.Clear();
            _lineOutputMul.Clear();
            _lineUpGood.Clear();
            _lineUpCell.Clear();
            _lineUpModifier.Clear();
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
                _lineModifier.Add(found && !isInput ? SupplyModifier(market, line.Good) : -1);
                _lineQuantity.Add(line.Quantity);
                _lineIsInput.Add((byte)(isInput ? 1 : 0));

                // A specialty of the facility's own node: more output, and some of it one grade higher (DEC-016).
                bool specialty = !isInput && _specialties.IsSpecialty(node, line.Good.BaseIndex);
                _lineOutputMul.Add(specialty ? _db.Config.SpecialtyOutputMultiplier.Raw : Fx.OneRaw);
                int upGood = -1, upCell = -1, upModifier = -1;
                if (specialty && line.Good.Grade < QualityGrade.Best)
                {
                    var higher = GoodId.From(line.Good.BaseIndex, line.Good.Grade + 1);
                    if (_flows.TryFindNearestMarket(node, higher, out int upMarket, out upCell))
                    {
                        upGood = higher.Value;
                        upModifier = SupplyModifier(upMarket, higher);
                    }
                }
                _lineUpGood.Add(upGood);
                _lineUpCell.Add(upCell);
                _lineUpModifier.Add(upModifier);
            }
        }

        int SupplyModifier(int market, GoodId good) =>
            _flows.MultiplierIndex(market, _db.Good(good).CategoryIndex, ModifierTarget.Supply);

        public void Dispose()
        {
            if (_lineStart.IsCreated) _lineStart.Dispose();
            if (_lineCell.IsCreated) _lineCell.Dispose();
            if (_lineGood.IsCreated) _lineGood.Dispose();
            if (_lineModifier.IsCreated) _lineModifier.Dispose();
            if (_lineQuantity.IsCreated) _lineQuantity.Dispose();
            if (_lineIsInput.IsCreated) _lineIsInput.Dispose();
            if (_lineOutputMul.IsCreated) _lineOutputMul.Dispose();
            if (_lineUpGood.IsCreated) _lineUpGood.Dispose();
            if (_lineUpCell.IsCreated) _lineUpCell.Dispose();
            if (_lineUpModifier.IsCreated) _lineUpModifier.Dispose();
            if (_playerOutputFacility.IsCreated) _playerOutputFacility.Dispose();
            if (_playerOutputGood.IsCreated) _playerOutputGood.Dispose();
            if (_playerOutputCell.IsCreated) _playerOutputCell.Dispose();
            if (_playerOutputQuantity.IsCreated) _playerOutputQuantity.Dispose();
            if (_bankrupt.IsCreated) _bankrupt.Dispose();
            if (_inputCost.IsCreated) _inputCost.Dispose();
            if (_groupDividend.IsCreated) _groupDividend.Dispose();
            _defUpkeep.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Weekly facility production in facility order, so results are independent of Burst. Two passes per
    /// facility: check every input's stock and the total cost, then buy, pay and produce. A purchase takes
    /// units out of the stock and passes them through supply into demand, like <see cref="MarketFlows.Buy"/>.
    /// Cells are clamped after every add, like <see cref="MarketFlows"/>. A specialty output line is
    /// multiplied, and part of it comes out one grade higher: all of it with the grade-up chance when the
    /// facility is detailed, the expected share when it is aggregate.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ProductionJob : IJob
    {
        public ulong Seed;
        public int Turn;
        public uint Stream;
        public uint GradeStream;
        public int VariancePermille;
        public long GradeUpChanceRaw;

        [ReadOnly] public NativeArray<int> FacilityNode;
        [ReadOnly] public NativeArray<int> FacilityOwner;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> FacilityEfficiency;
        public NativeArray<long> FacilityCash;
        /// <summary>Per node: its traders' float; buyers pay into it and sellers are paid from it (DEC-061).</summary>
        [ReadOnly] public NativeArray<int> NodeFloat;
        public NativeArray<long> Float;
        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> LineStart;
        [ReadOnly] public NativeArray<int> LineCell;
        [ReadOnly] public NativeArray<int> LineGood;
        [ReadOnly] public NativeArray<int> LineModifier;
        [ReadOnly] public NativeArray<int> LineQuantity;
        [ReadOnly] public NativeArray<byte> LineIsInput;
        [ReadOnly] public NativeArray<long> LineOutputMul;
        [ReadOnly] public NativeArray<int> LineUpGood;
        [ReadOnly] public NativeArray<int> LineUpCell;
        [ReadOnly] public NativeArray<int> LineUpModifier;
        [ReadOnly] public NativeArray<long> ModifierMul;
        [ReadOnly] public NativeArray<int> Price;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        /// <summary>Output of player-owned facilities (facility, good, market cell, quantity), stored after the job.</summary>
        public NativeList<int> PlayerOutputFacility;
        public NativeList<int> PlayerOutputGood;
        public NativeList<int> PlayerOutputCell;
        public NativeList<int> PlayerOutputQuantity;
        /// <summary>Per facility: this week's inputs at current prices, bought or not (the dividend buffer counts them).</summary>
        public NativeArray<long> FacilityInputCost;

        public void Execute()
        {
            for (int f = 0; f < FacilityNode.Length; f++)
            {
                bool detailed = NodeDetail[FacilityNode[f]] == (byte)SimDetail.Detailed;
                var output = Fx.FromRaw(FacilityEfficiency[f]) * Fx.FromInt(FacilityLevel[f]);
                if (detailed) output *= DetRandom.For(Seed, Turn, Stream, f).NextSpread(VariancePermille);
                // Aggregate: expected output, no per-facility randomness or decisions.

                int start = LineStart[f], end = LineStart[f + 1];
                long inputCost = 0;
                for (int k = start; k < end && LineIsInput[k] != 0; k++)
                    if (LineCell[k] >= 0) inputCost += Fx.MulIntRaw(LineQuantity[k], output.Raw) * Price[LineCell[k]];
                FacilityInputCost[f] = inputCost;
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
                Float[NodeFloat[FacilityNode[f]]] += cost;

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

                    quantity = Fx.MulIntRaw(quantity, LineOutputMul[k]);
                    long upgraded = 0;
                    if (LineUpGood[k] >= 0)
                    {
                        if (!detailed) upgraded = Fx.MulIntRaw(quantity, GradeUpChanceRaw);
                        else if (DetRandom.For(Seed, Turn, GradeStream, f, (uint)k).NextFx01().Raw < GradeUpChanceRaw) upgraded = quantity;
                        Emit(f, LineUpGood[k], LineUpCell[k], LineUpModifier[k], upgraded, playerOwned);
                    }
                    Emit(f, LineGood[k], cell, LineModifier[k], quantity - upgraded, playerOwned);
                }
            }
        }

        void Emit(int f, int good, int cell, int modifier, long quantity, bool playerOwned)
        {
            if (cell >= 0) quantity = Fx.MulIntRaw(quantity, ModifierMul[modifier]);
            int clamped = MarketStore.ClampToNonNegativeInt(quantity);
            if (clamped == 0) return;
            if (playerOwned)
            {
                PlayerOutputFacility.Add(f);
                PlayerOutputGood.Add(good);
                PlayerOutputCell.Add(cell);
                PlayerOutputQuantity.Add(clamped);
            }
            else if (cell >= 0)
            {
                // Sold as far as the traders can pay; the rest is lost, since NPC facilities keep no stock (DEC-061).
                int traders = NodeFloat[FacilityNode[f]];
                long price = Price[cell];
                if (price > 0)
                {
                    long payable = Float[traders] > 0 ? Float[traders] / price : 0;
                    if (clamped > payable) clamped = (int)payable;
                }
                if (clamped == 0) return;
                Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + clamped);
                FacilityCash[f] += clamped * price;
                Float[traders] -= clamped * price;
            }
        }
    }

    /// <summary>
    /// Weekly upkeep (wages) of every facility, scaled by its level, paid to the households of its node
    /// (DEC-061); cash may go below zero. A facility whose cash ended <see cref="BankruptWeeks"/> weeks in a
    /// row below zero is listed as bankrupt (DEC-051). An NPC facility then pays a share of its cash above
    /// <see cref="DividendBufferWeeks"/> weeks of costs (upkeep and inputs) to its group, or to the
    /// households of its node without one (DEC-061).
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct FacilityUpkeepJob : IJob
    {
        public int BankruptWeeks;
        public int DividendBufferWeeks;
        public long DividendShareRaw;

        [ReadOnly] public NativeArray<int> FacilityDefIndex;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> DefUpkeep;
        public NativeArray<long> FacilityCash;
        public NativeArray<int> FacilityNegativeCashWeeks;
        public NativeList<int> Bankrupt;
        [ReadOnly] public NativeArray<int> FacilityNode;
        /// <summary>Per node: <see cref="FES.Economy.HouseholdCash.Cash"/>.</summary>
        public NativeArray<long> HouseholdCash;
        [ReadOnly] public NativeArray<int> FacilityOwner;
        [ReadOnly] public NativeArray<int> FacilityGroup;
        [ReadOnly] public NativeArray<long> FacilityInputCost;
        /// <summary>Per group: dividends received this week.</summary>
        public NativeArray<long> GroupDividend;

        public void Execute()
        {
            for (int f = 0; f < FacilityCash.Length; f++)
            {
                long upkeep = DefUpkeep[FacilityDefIndex[f]] * FacilityLevel[f];
                FacilityCash[f] -= upkeep;
                HouseholdCash[FacilityNode[f]] += upkeep;
                long buffer = (upkeep + FacilityInputCost[f]) * DividendBufferWeeks;
                if (FacilityOwner[f] == OwnerIds.Npc && FacilityCash[f] > buffer)
                {
                    long dividend = Fx.MulIntRaw(FacilityCash[f] - buffer, DividendShareRaw);
                    FacilityCash[f] -= dividend;
                    if (FacilityGroup[f] >= 0) GroupDividend[FacilityGroup[f]] += dividend;
                    else HouseholdCash[FacilityNode[f]] += dividend;
                }
                if (FacilityCash[f] >= 0) FacilityNegativeCashWeeks[f] = 0;
                else if (++FacilityNegativeCashWeeks[f] >= BankruptWeeks) Bankrupt.Add(f);
            }
        }
    }
}
