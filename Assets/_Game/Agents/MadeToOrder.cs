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
    /// What each market's factories plan to make (DEC-085). Every quarter it measures how much left each market
    /// beyond what came in, forecasts the next quarter from that and the last forecast, and sets a share of capacity:
    /// the forecast, plus open orders and, for goods made to stock, a quarter's cover, less the stock. Primary producers do not plan.
    /// </summary>
    public sealed class DemandForecast : IPersistentComponent, IDisposable
    {
        /// <summary>Per cell, raw Fx: the share of capacity factories selling there run at.</summary>
        public NativeArray<long> Plan;
        /// <summary>Per cell: units factories made this quarter.</summary>
        public NativeArray<long> Produced;
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

    /// <summary>Quarterly factory planning; off unless production.forecast_enabled is set (DEC-085).</summary>
    public sealed class DemandForecastSystem : ISimSystem
    {
        GameDatabase _db;
        DemandForecast _state;
        MarketStore _markets;
        MarketFlows _flows;
        FacilityTable _facilities;
        OrderTable _orders;
        long[] _capacity;
        long[] _ordered;

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
            _capacity = new long[_markets.CellCount];
            _ordered = new long[_markets.CellCount];
        }

        public void Execute(SimContext ctx)
        {
            var config = _db.Config;
            if (!config.ForecastEnabled || ctx.Turn == 0 || ctx.Turn % OrderSystem.QuarterWeeks != 0) return;
            Array.Clear(_capacity, 0, _capacity.Length);
            Array.Clear(_ordered, 0, _ordered.Length);
            for (int f = 0; f < _facilities.Count; f++)
            {
                var def = _db.Facilities[_facilities.DefIndex[f]];
                if (def.Tier == IndustryTier.Primary || def.Recipe.Outputs.Length == 0) continue;
                var line = def.Recipe.Outputs[0];
                if (!_flows.TryFindNearestMarket(_facilities.Node[f], line.Good, out _, out int cell)) continue;
                _capacity[cell] += FacilityExpansion.OutputScale(_facilities.Level[f], config).MulInt(line.Quantity * OrderSystem.QuarterWeeks);
            }
            for (int r = 0; r < _orders.Count; r++)
                if (_orders.ReadyTurn[r] < 0) _ordered[_orders.Cell[r]] += _orders.Quantity[r];
            for (int c = 0; c < _capacity.Length; c++)
            {
                long stock = _markets.Stock[c];
                long left = Math.Max(0, _state.StockAtStart[c] + _state.Produced[c] - stock);
                _state.StockAtStart[c] = stock;
                _state.Produced[c] = 0;
                _state.Forecast[c] = (_state.Forecast[c] + left) / 2;
                if (_capacity[c] <= 0) continue;
                long cover = _db.Goods[_markets.CellGood[c]].MadeToOrder ? 0 : _state.Forecast[c];
                long target = _state.Forecast[c] + cover + _ordered[c] - stock;
                var plan = Fx.Clamp(Fx.FromRatio(Math.Max(0, target), _capacity[c]), config.ForecastMinPlan, Fx.One);
                _state.Plan[c] = plan.Raw;
            }
        }
    }
}
