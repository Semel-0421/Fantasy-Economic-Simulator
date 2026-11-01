using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
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
        NativeList<long> _outputWage;
        NativeList<long> _groupDividend;
        NativeArray<long> _defUpkeep;
        GroupTable _groupTable;
        WorldState _world;
        PurseState _purses;
        HouseholdCash _households;
        IncomeTaxState _books;
        NativeArray<long> _floatReserve;
        NativeArray<long> _nodeWageAway;
        NativeArray<long> _nodeWagesAway;

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
            _books = ctx.World.Get<IncomeTaxState>();
            _floatReserve = new NativeArray<long>(_purses.Float.Length, Allocator.Persistent);
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
            var nodeTable = ctx.World.Get<NodeTable>();
            _nodeWageAway = new NativeArray<long>(nodeTable.Count, Allocator.Persistent);
            _nodeWagesAway = new NativeArray<long>(nodeTable.Count, Allocator.Persistent);
            for (int n = 0; n < nodeTable.Count; n++)
                if (nodeTable.LevelOf(n) == WorldLevel.Village && ctx.World.Get<NodeBanks>().NationOf(n) >= 0)
                    _nodeWageAway[n] = _db.Config.VillageWageAwayShare.Raw;
            _inputCost = new NativeList<long>(Allocator.Persistent);
            _outputWage = new NativeList<long>(Allocator.Persistent);
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
            _outputWage.ResizeUninitialized(_facilities.Count);
            _groupDividend.ResizeUninitialized(_groupTable.Count);
            for (int g = 0; g < _groupDividend.Length; g++) _groupDividend[g] = 0;
            for (int p = 0; p < _floatReserve.Length; p++)
                _floatReserve[p] = Math.Max(0, _purses.Float[p]) - _db.Config.ProducerFloatShare.MulInt(Math.Max(0, _purses.Float[p]));
            _books.EnsureRows(_facilities, _world.Get<AgentTable>());
            var development = _world.Get<DevelopmentState>();
            var bank = _world.Get<BankState>();
            long[] subsidyBudget = new long[bank.Reserve.Length];
            foreach (var policy in development.Subsidies) subsidyBudget[policy.Nation] = 1;
            for (int n = 0; n < subsidyBudget.Length; n++)
            {
                if (subsidyBudget[n] != 0)
                    subsidyBudget[n] = _db.Config.SubsidyShare.MulInt(Math.Max(0, MoneyLedger.StateExcess(_world, _db.Config, n))) / SimClock.WeeksPerYear;
                development.SubsidyLeft[n] = subsidyBudget[n];
            }

            var bills = _world.Get<BillTable>();
            var billRoom = _world.Get<BillRoom>();
            var job = new ProductionJob
            {
                Seed = ctx.Seed,
                Turn = ctx.Turn,
                Stream = Stream,
                GradeStream = GradeStream,
                VariancePermille = _db.Config.ProductionVariancePermille,
                GradeUpChanceRaw = _db.Config.SpecialtyGradeUpChance.Raw,
                ExpansionBonusRaw = _db.Config.FacilityExpansionBonus.Raw,
                UnaidedOutputRaw = _db.Config.FacilityUnaidedOutput.Raw,
                OutputWageRaw = _db.Config.FacilityOutputWage.Raw,
                CellPlan = _world.Get<DemandForecast>().Plan,
                CellProduced = _world.Get<DemandForecast>().Produced,
                GoodBasePrice = _markets.GoodBasePrice,
                FacilityOutputWage = _outputWage.AsArray(),
                FacilityNode = _facilities.Node.AsArray(),
                FacilityOwner = _facilities.Owner.AsArray(),
                FacilityLevel = _facilities.Level.AsArray(),
                FacilityEfficiency = _facilities.Efficiency.AsArray(),
                NodeFloat = _purses.NodeFloat,
                Float = _purses.Float,
                FloatReserve = _floatReserve,
                SpreadRaw = _db.Config.TraderSpread.Raw,
                GoodCount = _markets.GoodBasePrice.Length,
                SubsidyRate = development.SubsidyRate,
                SubsidyLeft = development.SubsidyLeft,
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
                InputBid = _markets.InputBid,
                PlayerOutputFacility = _playerOutputFacility,
                PlayerOutputGood = _playerOutputGood,
                PlayerOutputCell = _playerOutputCell,
                PlayerOutputQuantity = _playerOutputQuantity,
                FacilityInputCost = _inputCost.AsArray(),
                BillProducers = _db.Config.BillProducers,
                BillRoom = billRoom.Room,
                BillBankRoom = billRoom.BankRoom,
                BillBankSpent = billRoom.BankSpent,
                BillDiscountRaw = _db.Config.BillDiscount.Raw,
                BillWeeks = _db.Config.BillWeeks,
                BillHolder = bills.Holder.List,
                BillPurse = bills.Purse.List,
                BillFace = bills.Face.List,
                BillDue = bills.DueTurn.List,
            };
            if (ForceManaged) job.Execute();
            else job.Schedule().Complete();
            billRoom.SettleBank(bank);
            for (int n = 0; n < subsidyBudget.Length; n++) bank.Reserve[n] -= subsidyBudget[n] - development.SubsidyLeft[n];

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
                FacilityOutputWage = _outputWage.AsArray(),
                GroupDividend = _groupDividend.AsArray(),
                FacilityDividendsYear = _books.FacilityDividends.AsArray(),
                NodeWageAwayRaw = _nodeWageAway,
                NodeWagesAway = _nodeWagesAway,
            };
            if (ForceManaged) upkeep.Execute();
            else upkeep.Schedule().Complete();
            for (int n = 0; n < _nodeWagesAway.Length; n++)
            {
                if (_nodeWagesAway[n] == 0) continue;
                MoneyLedger.PayNationWages(_world, _db.Config, _purses.NodeFloat[n], _nodeWagesAway[n]);
                _nodeWagesAway[n] = 0;
            }
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
                int offer = _db.Config.TraderOffer(_markets.Price[cell]);
                overflow = _purses.Sellable(_facilities.Node[f], overflow, offer);
                if (overflow == 0) continue;
                _flows.Sell(cell, overflow);
                _facilities.Cash[f] += (long)overflow * offer;
                _purses.ChangeFloat(_facilities.Node[f], -(long)overflow * offer);
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
                bool state = _groupTable.KindOf(g) == GroupKind.StateEnterprise;
                long passed = (state ? _db.Config.StateFacilityLevy : share).MulInt(dividend);
                if (state) MoneyLedger.ToState(_world, _groupTable.Node[g], passed);
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
            _books.RestartFacility(f, _facilities.Cash[f]);
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
            var development = _world.Get<DevelopmentState>();
            for (int f = 0; f < _facilities.Count; f++)
            {
                _lineStart.Add(_lineCell.Length);
                var def = _db.Facilities[_facilities.DefIndex[f]];
                AddLines(_facilities.Node[f], def.Recipe.Inputs, true);
                int firstAid = _lineIsInput.Length;
                AddLines(_facilities.Node[f], def.Recipe.Aids, true);
                for (int k = firstAid; k < _lineIsInput.Length; k++) _lineIsInput[k] = ProductionJob.AidLine;
                int firstOutput = _lineOutputMul.Length;
                AddLines(_facilities.Node[f], def.Recipe.Outputs, false);
                // The state's land development raises primary output on that land (DEC-072).
                int land = def.Tier == IndustryTier.Primary ? development.LandLevel[_facilities.Node[f]] : 0;
                if (land == 0) continue;
                var bonus = Fx.One + _db.Config.StateLandBonus * Fx.FromInt(land);
                for (int k = firstOutput; k < _lineOutputMul.Length; k++) _lineOutputMul[k] = (Fx.FromRaw(_lineOutputMul[k]) * bonus).Raw;
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
            _nodeWageAway.DisposeIfCreated();
            _nodeWagesAway.DisposeIfCreated();
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
            if (_outputWage.IsCreated) _outputWage.Dispose();
            if (_groupDividend.IsCreated) _groupDividend.Dispose();
            _defUpkeep.DisposeIfCreated();
            _floatReserve.DisposeIfCreated();
        }
    }
}
