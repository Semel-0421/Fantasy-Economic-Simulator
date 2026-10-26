using System.Collections.Generic;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Simulation
{
    /// <summary>One market cell as presentation sees it: copied values.</summary>
    public readonly struct MarketQuote
    {
        public readonly GoodId Good;
        public readonly int Price;
        public readonly int PrevPrice;
        public readonly int Stock;

        public MarketQuote(GoodId good, int price, int prevPrice, int stock)
        {
            Good = good;
            Price = price;
            PrevPrice = prevPrice;
            Stock = stock;
        }
    }

    public readonly struct RouteInfo
    {
        public readonly NodeId To;
        public readonly int TravelTurns;
        public readonly int CostPerUnit;

        public RouteInfo(NodeId to, int travelTurns, int costPerUnit)
        {
            To = to;
            TravelTurns = travelTurns;
            CostPerUnit = costPerUnit;
        }
    }

    public readonly struct FacilityInfo
    {
        public readonly FacilityId Id;
        public readonly FacilityDef Def;
        public readonly bool PlayerOwned;
        public readonly ControlMode Mode;

        public FacilityInfo(FacilityId id, FacilityDef def, bool playerOwned, ControlMode mode)
        {
            Id = id;
            Def = def;
            PlayerOwned = playerOwned;
            Mode = mode;
        }
    }

    /// <summary>
    /// Read-only queries for presentation. Everything returned is a copy or immutable definition data,
    /// never a component or native array, so UI code cannot change simulation state; changes go
    /// through <see cref="PlayerActions"/>. Read between turns, from the thread that advances them.
    /// </summary>
    public sealed class SimulationView
    {
        readonly SimClock _clock;
        readonly NodeTable _nodes;
        readonly RouteTable _routes;
        readonly MarketStore _markets;
        readonly CurrencyState _currency;
        readonly PlayerState _player;
        readonly GuidanceState _guidance;
        readonly FacilityTable _facilities;

        public readonly GameDatabase Database;

        internal SimulationView(WorldState world, GameDatabase database)
        {
            Database = database;
            _clock = world.Get<SimClock>();
            _nodes = world.Get<NodeTable>();
            _routes = world.Get<RouteTable>();
            _markets = world.Get<MarketStore>();
            _currency = world.Get<CurrencyState>();
            _player = world.Get<PlayerState>();
            _guidance = world.Get<GuidanceState>();
            _facilities = world.Get<FacilityTable>();
        }

        // Clock

        public int Turn => _clock.Turn;
        public int Year => _clock.Year;
        public int Week => _clock.Week;

        // Map

        public int NodeCount => _nodes.Count;
        public string NodeKey(NodeId node) => _nodes.KeyOf(node.Value);
        public string NodeName(NodeId node) => _nodes.NameOf(node.Value);
        public WorldLevel NodeLevel(NodeId node) => _nodes.LevelOf(node.Value);
        public NodeId Parent(NodeId node) => new NodeId(_nodes.Parent[node.Value]);
        /// <summary>The node's continent; also its currency zone.</summary>
        public int ContinentOf(NodeId node) => _nodes.Continent[node.Value];
        public bool TryFindNode(string key, out NodeId node) => _nodes.TryFind(key, out node);

        public void GetChildren(NodeId node, List<NodeId> into)
        {
            into.Clear();
            int start = _nodes.ChildStart[node.Value];
            for (int k = start; k < start + _nodes.ChildCount[node.Value]; k++) into.Add(new NodeId(_nodes.Children[k]));
        }

        public void GetRoutesFrom(NodeId node, List<RouteInfo> into)
        {
            into.Clear();
            int start = _routes.OutStart[node.Value];
            for (int k = start; k < start + _routes.OutCount[node.Value]; k++)
            {
                int r = _routes.RouteIndex[k];
                into.Add(new RouteInfo(new NodeId(_routes.To[r]), _routes.TravelTurns[r], _routes.CostPerUnit[r]));
            }
        }

        // Markets (market index = node index)

        public bool TryGetQuote(NodeId node, GoodId good, out MarketQuote quote)
        {
            if (_markets.TryFindCell(node.Value, good, out int cell))
            {
                quote = Quote(cell);
                return true;
            }
            quote = default;
            return false;
        }

        /// <summary>Every good the node's market carries, sorted by good id.</summary>
        public void GetQuotes(NodeId node, List<MarketQuote> into)
        {
            into.Clear();
            int start = _markets.MarketCellStart[node.Value];
            for (int c = start; c < start + _markets.MarketCellCount[node.Value]; c++) into.Add(Quote(c));
        }

        MarketQuote Quote(int cell) =>
            new MarketQuote(new GoodId(_markets.CellGood[cell]), _markets.Price[cell], _markets.PrevPrice[cell], _markets.Stock[cell]);

        // Facilities

        public void GetFacilities(NodeId node, List<FacilityInfo> into)
        {
            into.Clear();
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Node[f] != node.Value) continue;
                into.Add(new FacilityInfo(new FacilityId(f), Database.Facilities[_facilities.DefIndex[f]],
                    _facilities.Owner[f] == OwnerIds.Player, (ControlMode)_facilities.Mode[f]));
            }
        }

        // Player

        public NodeId PlayerNode => new NodeId(_player.Node);
        public long PlayerCash => _player.Cash;
        public GuidanceAxis DominantGuidance => _guidance.Dominant;
        public Fx GuidanceWeight(GuidanceAxis axis) => _guidance[axis];

        // Currency (per continent)

        public int ContinentCount => _currency.ContinentCount;
        public Fx PriceIndex(int continent) => _currency.PriceIndex[continent];
        public Fx ExchangeRate(int continent) => _currency.ExchangeRate[continent];
        public long CoinSupply(int continent) => _currency.CoinSupply[continent];
    }

    /// <summary>
    /// The player's actions as presentation calls them. Each queues a command that is validated and
    /// applied at the start of the next turn (see <see cref="CommandApplied"/> / <see cref="CommandRejected"/>).
    /// </summary>
    public sealed class PlayerActions
    {
        readonly CommandQueue _queue;

        internal PlayerActions(CommandQueue queue) => _queue = queue;

        public void Travel(NodeId destination) => _queue.Enqueue(new TravelCommand(destination));

        public void BuyFacility(FacilityId facility, long price, GuidanceAxis axis) =>
            _queue.Enqueue(new BuyFacilityCommand(facility, price, axis));

        public void FoundCompany(NodeId headquarters, GuidanceAxis focus, long cost) =>
            _queue.Enqueue(new FoundCompanyCommand(headquarters, focus, cost));

        public void SetFacilityControl(FacilityId facility, ControlMode mode) =>
            _queue.Enqueue(new SetFacilityControlCommand(facility, mode));
    }
}
