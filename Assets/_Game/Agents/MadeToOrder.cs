using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>What detailed merchants did on their trips, for reading balance.</summary>
    public sealed class MerchantTripStats : IWorldComponent
    {
        public long Trips, Guarded, Insured, Encounters;
        /// <summary>DEC-093: sales below what the goods cost and the coins lost on them, loads carried on instead of sold, sales made because the group would not last the trip, all coins from sales, and what failed groups had paid for the goods they lost.</summary>
        public long LossSales, LossAmount, CarriedOn, ForcedSales, Sold, LostWithGroups;
        /// <summary>DEC-099: where merchants' coins go. What they pay for goods, on fees, tolls, guards, cover, upkeep and to thieves; what cover paid back; lost on bills' discount and foreign exchange.</summary>
        public long SpentGoods, SpentFees, SpentTolls, SpentGuards, SpentCover, SpentUpkeep, SpentStolen, CoverRefunds, SpentDiscount;
        /// <summary>DEC-099: what merchants pay outside their trips: wages, income tax, association fees, loans, vehicles.</summary>
        public long SpentWages, SpentTax, SpentFeesToAssociations, SpentLoans, SpentVehicles;
        /// <summary>DEC-099: price cover bought, and what banks paid on it.</summary>
        public long SpentHedge, HedgePaid;
    }

    /// <summary>
    /// Orders for goods made on request (DEC-085). An order waits until its market has the units, reserves them, and
    /// is collected and paid for after a lead time. No money is held while it waits.
    /// </summary>
    public sealed class OrderTable : NativeTable
    {
        /// <summary>The merchant group ordering, or -1 for the households of <see cref="Node"/>.</summary>
        public readonly NativeColumn<int> Buyer;
        public readonly NativeColumn<int> Node;
        public readonly NativeColumn<int> Cell;
        public readonly NativeColumn<int> Quantity;
        public readonly NativeColumn<int> PlacedTurn;
        /// <summary>The turn the reserved units can be collected, or -1 while none are reserved.</summary>
        public readonly NativeColumn<int> ReadyTurn;
        public readonly NativeColumn<byte> Vehicle;

        public override string Key => "agents.orders";
        public override int Version => 1;

        public OrderTable()
        {
            Buyer = Column<int>();
            Node = Column<int>();
            Cell = Column<int>();
            Quantity = Column<int>();
            PlacedTurn = Column<int>();
            ReadyTurn = Column<int>();
            Vehicle = Column<byte>();
        }

        public void Add(int buyer, int node, int cell, int quantity, int turn, byte vehicle)
        {
            Buyer.Add(buyer);
            Node.Add(node);
            Cell.Add(cell);
            Quantity.Add(quantity);
            PlacedTurn.Add(turn);
            ReadyTurn.Add(-1);
            Vehicle.Add(vehicle);
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>
    /// Runs the order book (DEC-085). Weekly it reserves what markets hold, hands over what is due, and drops orders
    /// too long unfilled. Quarterly households order the made-to-order luxuries they can afford. Merchants order
    /// vehicles in <see cref="MerchantVehicleSystem"/>.
    /// </summary>
    public sealed class OrderSystem : ISimSystem
    {
        public const int QuarterWeeks = 13;

        GameDatabase _db;
        OrderTable _orders;
        AgentTable _agents;
        MarketStore _markets;
        PurseState _purses;
        HouseholdState _population;
        HouseholdCash _cash;
        NodeTable _nodes;
        CurrencyState _currency;
        DemandParams _demand;
        bool[] _made;

        public string Id => "agents.orders";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Orders;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _orders = ctx.World.Get<OrderTable>();
            _agents = ctx.World.Get<AgentTable>();
            _markets = ctx.World.Get<MarketStore>();
            _purses = ctx.World.Get<PurseState>();
            _population = ctx.World.Get<HouseholdState>();
            _cash = ctx.World.Get<HouseholdCash>();
            _nodes = ctx.World.Get<NodeTable>();
            _currency = ctx.World.Get<CurrencyState>();
            _demand = DemandParams.From(_db.Config);
            _made = new bool[_db.GoodCount];
            foreach (var def in _db.Facilities)
                foreach (var line in def.Recipe.Outputs) _made[line.Good.Value] = true;
        }

        public void Execute(SimContext ctx)
        {
            if (ctx.Turn > 0 && ctx.Turn % QuarterWeeks == 0) HouseholdsOrder(ctx);
            for (int r = _orders.Count - 1; r >= 0; r--)
            {
                int cell = _orders.Cell[r], quantity = _orders.Quantity[r];
                if (_orders.ReadyTurn[r] < 0)
                {
                    if (_markets.Stock[cell] >= quantity)
                    {
                        _markets.Stock[cell] -= quantity;
                        _orders.ReadyTurn[r] = ctx.Turn + _db.Config.OrderLeadWeeks;
                    }
                    else if (ctx.Turn - _orders.PlacedTurn[r] >= _db.Config.OrderExpiryWeeks) _orders.RemoveRow(r);
                    continue;
                }
                if (ctx.Turn >= _orders.ReadyTurn[r]) Collect(ctx, r, cell, quantity);
            }
        }

        void Collect(SimContext ctx, int r, int cell, int quantity)
        {
            long cost = (long)quantity * _markets.Price[cell];
            int buyer = _orders.Buyer[r], node = _orders.Node[r];
            bool households = buyer < 0;
            long held = households ? _cash.Cash[node] : _agents.Cash[buyer];
            if (held < cost)
            {
                _markets.Stock[cell] += quantity; // cannot pay: back on the shelf
                _orders.RemoveRow(r);
                return;
            }
            if (households)
            {
                _cash.Cash[node] -= cost;
                _cash.SpentYear[node] += cost;
            }
            else
            {
                _agents.Cash[buyer] -= cost;
                ctx.World.Get<MerchantTripStats>().SpentVehicles += cost;
                if (_orders.Vehicle[r] > _agents.Vehicle[buyer])
                {
                    _agents.Vehicle[buyer] = _orders.Vehicle[r];
                    ctx.Events.Publish(new MerchantVehicleBought(new AgentId(buyer), _orders.Vehicle[r]));
                }
            }
            _purses.ChangeFloat(node, cost);
            _markets.Supply[cell] = MarketStore.ClampToNonNegativeInt((long)_markets.Supply[cell] + quantity);
            _markets.Demand[cell] = MarketStore.ClampToNonNegativeInt((long)_markets.Demand[cell] + quantity);
            _orders.RemoveRow(r);
        }

        void HouseholdsOrder(SimContext ctx)
        {
            var config = _db.Config;
            for (int n = 0; n < _nodes.Count; n++)
            {
                long share = _cash.LuxuryBudgetFactor(n, config.HouseholdBudgetWeeks);
                if (share <= 0) continue;
                int level = _nodes.Level[n], end = _markets.MarketCellStart[n] + _markets.MarketCellCount[n];
                long index = _currency.PriceIndex[_nodes.Continent[n]].Raw;
                for (int c = _markets.MarketCellStart[n]; c < end; c++)
                {
                    var def = _db.Goods[_markets.CellGood[c]];
                    if (!def.MadeToOrder || def.BaseDemand <= 0 || !_made[def.Id.Value]) continue;
                    long weekly = HouseholdDemand.CellBase(def.BaseDemand, def.BasePrice, config.LevelDemandWeight[level].Raw, Fx.OneRaw, index, out long anchor);
                    long units = HouseholdDemand.Weekly(_demand, weekly, _markets.Price[c], anchor, Fx.OneRaw,
                        _population.NecessityWeight(n, level, config).Raw, _population.LuxuryWeight(n, level, config).Raw, out _, out _);
                    units = Fx.RoundShift(units * share) * QuarterWeeks;
                    if (units > 0) _orders.Add(-1, n, c, MarketStore.ClampToNonNegativeInt(units), ctx.Turn, 0);
                }
            }
        }
    }

    /// <summary>
    /// What makers selling at each market plan to make (DEC-085, DEC-091), as <see cref="DemandForecastSystem"/> sets it.
    /// </summary>
    public sealed class DemandForecast : IPersistentComponent, IDisposable
    {
        /// <summary>Per cell, raw Fx: the share of capacity factories selling there run at.</summary>
        public NativeArray<long> Plan;
        /// <summary>Per cell: units factories made this quarter.</summary>
        public NativeArray<long> Produced;
        /// <summary>Per cell: how much lately left the market beyond what came in, smoothed, per quarter.</summary>
        public readonly long[] Forecast;
        public readonly long[] StockAtStart;

        public string Key => "agents.demand_forecast";
        public int Version => 1;

        public DemandForecast(int cells)
        {
            Plan = new NativeArray<long>(cells, Allocator.Persistent);
            Produced = new NativeArray<long>(cells, Allocator.Persistent);
            Forecast = new long[cells];
            StockAtStart = new long[cells];
            for (int c = 0; c < cells; c++) Plan[c] = Fx.OneRaw;
        }

        public void Write(BinaryWriter writer)
        {
            NativeIO.WriteArray(writer, Plan);
            NativeIO.WriteArray(writer, Produced);
            foreach (long v in Forecast) writer.Write(v);
            foreach (long v in StockAtStart) writer.Write(v);
        }

        public void Read(BinaryReader reader)
        {
            NativeIO.ReadArray(reader, Plan);
            NativeIO.ReadArray(reader, Produced);
            for (int c = 0; c < Forecast.Length; c++) Forecast[c] = reader.ReadInt64();
            for (int c = 0; c < StockAtStart.Length; c++) StockAtStart[c] = reader.ReadInt64();
        }

        public void Dispose()
        {
            Plan.DisposeIfCreated();
            Produced.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Quarterly planning; off unless production.forecast_enabled is set (DEC-085, DEC-091). The forecast of a market's
    /// good weighs what consumers would take at settled prices, above all, against what lately left the market. Consumers
    /// are households and, up the chain, the makers they set running: each plan adds its inputs and aids to the demand at
    /// the markets those makers buy from, so what households want reaches raw goods. Makers selling at a market serve it
    /// first, with some cover, less its own stock: stock elsewhere does not feed it until merchants carry it. What
    /// its makers cannot serve, and the demand of markets without makers, falls to the nation's other makers of the good
    /// with capacity to spare, less what its markets hold beyond their own needs.
    /// </summary>
    public sealed class DemandForecastSystem : ISimSystem
    {
        // Enough for the longest chain, tools to bars to ore; tools also feed the mines, which settles within these.
        const int ChainPasses = 6;

        // One kind of maker selling at one market, keyed by the cell of its first output, where ProductionJob reads the plan.
        struct Makers
        {
            public int Cell;
            public FacilityDef Def;
            /// <summary>Raw Fx: recipe runs a quarter at full capacity.</summary>
            public long Runs;
            /// <summary>Where its output cells start in the line list, then its inputs and aids.</summary>
            public int FirstLine;
            public long Plan;
        }

        GameDatabase _db;
        DemandForecast _state;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        OrderTable _orders;
        NodeTable _nodes;
        PurseState _purses;
        HouseholdState _population;
        CurrencyState _currency;
        DemandParams _demand;
        readonly System.Collections.Generic.List<Makers> _makers = new System.Collections.Generic.List<Makers>();
        readonly System.Collections.Generic.List<int> _lines = new System.Collections.Generic.List<int>();
        int[] _cellPurse, _makersAt;
        // Per cell, units a quarter.
        long[] _households, _ordered, _industry, _nextIndustry, _capacity, _need;
        // Per purse and good, units a quarter.
        long[] _unmet, _spare, _surplus;

        public string Id => "agents.demand_forecast";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.DemandForecast;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _state = ctx.World.Get<DemandForecast>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _facilities = ctx.World.Get<FacilityTable>();
            _orders = ctx.World.Get<OrderTable>();
            _nodes = ctx.World.Get<NodeTable>();
            _purses = ctx.World.Get<PurseState>();
            _population = ctx.World.Get<HouseholdState>();
            _currency = ctx.World.Get<CurrencyState>();
            _demand = DemandParams.From(_db.Config);
            int cells = _markets.CellCount, perGood = _purses.Count * _db.GoodCount;
            _cellPurse = new int[cells];
            _makersAt = new int[cells];
            for (int n = 0; n < _nodes.Count; n++)
            {
                int end = _markets.MarketCellStart[n] + _markets.MarketCellCount[n];
                for (int c = _markets.MarketCellStart[n]; c < end; c++) _cellPurse[c] = _purses.NodeFloat[n];
            }
            _households = new long[cells];
            _ordered = new long[cells];
            _industry = new long[cells];
            _nextIndustry = new long[cells];
            _capacity = new long[cells];
            _need = new long[cells];
            _unmet = new long[perGood];
            _spare = new long[perGood];
            _surplus = new long[perGood];
        }

        public void Execute(SimContext ctx)
        {
            if (!_db.Config.ForecastEnabled || ctx.Turn == 0 || ctx.Turn % OrderSystem.QuarterWeeks != 0) return;
            MeasureMarkets();
            ListMakers();
            Array.Clear(_industry, 0, _industry.Length);
            for (int pass = 0; pass < ChainPasses; pass++) PlanMakers();
            foreach (var makers in _makers) _state.Plan[makers.Cell] = makers.Plan;
        }

        void MeasureMarkets()
        {
            var config = _db.Config;
            Array.Clear(_ordered, 0, _ordered.Length);
            for (int n = 0; n < _nodes.Count; n++)
            {
                int level = _nodes.Level[n], end = _markets.MarketCellStart[n] + _markets.MarketCellCount[n];
                long index = _currency.PriceIndex[_nodes.Continent[n]].Raw;
                for (int c = _markets.MarketCellStart[n]; c < end; c++)
                {
                    long stock = _markets.Stock[c];
                    long left = Math.Max(0, _state.StockAtStart[c] + _state.Produced[c] - stock);
                    _state.StockAtStart[c] = stock;
                    _state.Produced[c] = 0;
                    _state.Forecast[c] = (_state.Forecast[c] + left) / 2;
                    _households[c] = HouseholdDemand.AtAnchor(_demand, config, _population, n, level, _db.Goods[_markets.CellGood[c]], index) * OrderSystem.QuarterWeeks;
                }
            }
            for (int r = 0; r < _orders.Count; r++)
                if (_orders.ReadyTurn[r] < 0) _ordered[_orders.Cell[r]] += _orders.Quantity[r];
        }

        void ListMakers()
        {
            _makers.Clear();
            _lines.Clear();
            Array.Fill(_makersAt, -1);
            Array.Clear(_capacity, 0, _capacity.Length);
            var config = _db.Config;
            for (int f = 0; f < _facilities.Count; f++)
            {
                var def = _db.Facilities[_facilities.DefIndex[f]];
                if (_facilities.Owner[f] == OwnerIds.Player || def.Recipe.Outputs.Length == 0) continue; // the player runs their own as they like
                if (def.Tier == IndustryTier.Primary && !config.ForecastPrimary) continue;
                int node = _facilities.Node[f];
                if (!_flows.TryFindNearestMarket(node, def.Recipe.Outputs[0].Good, out _, out int cell)) continue;
                int at = _makersAt[cell];
                if (at < 0)
                {
                    at = _makersAt[cell] = _makers.Count;
                    _makers.Add(new Makers { Cell = cell, Def = def, FirstLine = _lines.Count });
                    AddLines(node, def.Recipe.Outputs);
                    AddLines(node, def.Recipe.Inputs);
                    AddLines(node, def.Recipe.Aids);
                }
                var makers = _makers[at];
                makers.Runs += FacilityExpansion.OutputScale(_facilities.Level[f], config).Raw * OrderSystem.QuarterWeeks;
                _makers[at] = makers;
            }
            foreach (var makers in _makers)
            {
                int line = makers.FirstLine;
                foreach (var output in makers.Def.Recipe.Outputs)
                {
                    int cell = _lines[line++];
                    if (cell >= 0) _capacity[cell] += Fx.MulIntRaw(output.Quantity, makers.Runs);
                }
            }
        }

        void AddLines(int node, RecipeLine[] lines)
        {
            foreach (var line in lines)
                _lines.Add(_flows.TryFindNearestMarket(node, line.Good, out _, out int cell) ? cell : -1);
        }

        // One step up the chain: plans from what is wanted now, and what those plans will want.
        void PlanMakers()
        {
            MeasureNeeds();
            Array.Clear(_nextIndustry, 0, _nextIndustry.Length);
            var config = _db.Config;
            for (int m = 0; m < _makers.Count; m++)
            {
                var makers = _makers[m];
                int line = makers.FirstLine;
                long target = 0, capacity = 0;
                foreach (var output in makers.Def.Recipe.Outputs)
                {
                    int cell = _lines[line++];
                    long share = Fx.MulIntRaw(output.Quantity, makers.Runs);
                    capacity += share;
                    if (cell >= 0) target += Served(cell) * share / Math.Max(1, _capacity[cell]);
                }
                var plan = Fx.Clamp(Fx.FromRatio(target, Math.Max(1, capacity)), config.ForecastMinPlan, Fx.One);
                makers.Plan = plan.Raw;
                _makers[m] = makers;
                long planned = (plan * Fx.FromRaw(makers.Runs)).Raw;
                line = AddIndustry(makers.Def.Recipe.Inputs, line, planned);
                AddIndustry(makers.Def.Recipe.Aids, line, planned);
            }
            (_industry, _nextIndustry) = (_nextIndustry, _industry);
        }

        void MeasureNeeds()
        {
            var config = _db.Config;
            Array.Clear(_unmet, 0, _unmet.Length);
            Array.Clear(_spare, 0, _spare.Length);
            Array.Clear(_surplus, 0, _surplus.Length);
            for (int c = 0; c < _need.Length; c++)
            {
                int good = _markets.CellGood[c], k = _cellPurse[c] * _db.GoodCount + good;
                long forecast = config.ForecastHouseholdWeight.MulInt(_households[c] + _industry[c]) + (Fx.One - config.ForecastHouseholdWeight).MulInt(_state.Forecast[c]);
                long wanted = forecast + (_db.Goods[good].MadeToOrder ? 0 : config.ForecastCover.MulInt(forecast)) + _ordered[c];
                long stock = _markets.Stock[c];
                _need[c] = Math.Max(0, wanted - stock);
                _surplus[k] += Math.Max(0, stock - wanted);
                _unmet[k] += Math.Max(0, _need[c] - _capacity[c]);
                _spare[k] += Math.Max(0, _capacity[c] - _need[c]);
            }
        }

        // What the makers selling at a cell should make: its own need, and their part of what the nation's other markets lack.
        long Served(int cell)
        {
            int k = _cellPurse[cell] * _db.GoodCount + _markets.CellGood[cell];
            long own = Math.Min(_need[cell], _capacity[cell]), spare = _capacity[cell] - own;
            long lacking = Math.Max(0, _unmet[k] - _surplus[k]);
            return _spare[k] <= 0 ? own : own + Math.Min(spare, lacking * spare / _spare[k]);
        }

        int AddIndustry(RecipeLine[] lines, int line, long planned)
        {
            foreach (var input in lines)
            {
                int cell = _lines[line++];
                if (cell >= 0) _nextIndustry[cell] += Fx.MulIntRaw(input.Quantity, planned);
            }
            return line;
        }
    }
}
