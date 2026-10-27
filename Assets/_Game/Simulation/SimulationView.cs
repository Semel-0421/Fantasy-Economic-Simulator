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
        public readonly int BaseFee;

        public RouteInfo(NodeId to, int travelTurns, int costPerUnit, int baseFee)
        {
            To = to;
            TravelTurns = travelTurns;
            CostPerUnit = costPerUnit;
            BaseFee = baseFee;
        }
    }

    public readonly struct FacilityInfo
    {
        public readonly FacilityId Id;
        public readonly FacilityDef Def;
        public readonly bool PlayerOwned;
        public readonly ControlMode Mode;
        /// <summary>Coins of the facility's continent.</summary>
        public readonly long Cash;

        public FacilityInfo(FacilityId id, FacilityDef def, bool playerOwned, ControlMode mode, long cash)
        {
            Id = id;
            Def = def;
            PlayerOwned = playerOwned;
            Mode = mode;
            Cash = cash;
        }
    }

    /// <summary>
    /// Read-only queries for presentation. Everything returned is a copy or immutable definition data,
    /// never a component or native array, so UI code cannot change simulation state; changes go
    /// through <see cref="PlayerActions"/>. Read between turns, from the thread that advances them.
    /// </summary>
    public sealed class SimulationView
    {
        readonly WorldState _world;
        readonly SimClock _clock;
        readonly NodeTable _nodes;
        readonly RouteTable _routes;
        readonly MarketStore _markets;
        readonly CurrencyState _currency;
        readonly PlayerState _player;
        readonly GuidanceState _guidance;
        readonly FacilityTable _facilities;
        readonly CargoTable _cargo;
        readonly NodeSpecialties _specialties;
        readonly LicenseTable _licenses;

        public readonly GameDatabase Database;

        internal SimulationView(WorldState world, GameDatabase database)
        {
            Database = database;
            _world = world;
            _clock = world.Get<SimClock>();
            _nodes = world.Get<NodeTable>();
            _routes = world.Get<RouteTable>();
            _markets = world.Get<MarketStore>();
            _currency = world.Get<CurrencyState>();
            _player = world.Get<PlayerState>();
            _guidance = world.Get<GuidanceState>();
            _facilities = world.Get<FacilityTable>();
            _cargo = world.Get<CargoTable>();
            _specialties = world.Get<NodeSpecialties>();
            _licenses = world.Get<LicenseTable>();
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

        /// <summary>The node's specialty base goods (keys, sorted by index): made better, carried always, sold cheaper.</summary>
        public void GetSpecialties(NodeId node, List<string> into)
        {
            into.Clear();
            for (int k = _specialties.Start[node.Value]; k < _specialties.Start[node.Value] + _specialties.Count[node.Value]; k++)
                into.Add(Database.Good(GoodId.From(_specialties.BaseGood[k], QualityGrade.Mid)).BaseKey);
        }

        public bool IsSpecialty(NodeId node, GoodId good) => _specialties.IsSpecialty(node.Value, good.BaseIndex);

        /// <summary>
        /// The fee and toll of travelling from the player's node to <paramref name="destination"/> with the
        /// current cart; false when no route leads there.
        /// </summary>
        public bool TryQuoteTrip(NodeId destination, out TripQuote quote) => PlayerTrip.TryQuote(_world, Database, destination, out quote);

        public void GetRoutesFrom(NodeId node, List<RouteInfo> into)
        {
            into.Clear();
            int start = _routes.OutStart[node.Value];
            for (int k = start; k < start + _routes.OutCount[node.Value]; k++)
            {
                int r = _routes.RouteIndex[k];
                into.Add(new RouteInfo(new NodeId(_routes.To[r]), _routes.TravelTurns[r], _routes.CostPerUnit[r], _routes.BaseFee[r]));
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
                    _facilities.Owner[f] == OwnerIds.Player, (ControlMode)_facilities.Mode[f], _facilities.Cash[f]));
            }
        }

        /// <summary>The warehouse of a facility, sorted by good id. Holds goods while the player owns it.</summary>
        public void GetWarehouse(FacilityId facility, List<CargoItem> into) => _cargo.Collect(CargoHolders.Warehouse(facility), into);
        public CargoLoad WarehouseLoad(FacilityId facility) =>
            _cargo.LoadOf(CargoHolders.Warehouse(facility), _markets.GoodWeight, _markets.GoodSize);
        public CargoCapacity WarehouseCapacity => new CargoCapacity(Database.Config.WarehouseMaxWeight, Database.Config.WarehouseMaxSize);

        // Player

        /// <summary>Where the player is; while travelling, the node they left.</summary>
        public NodeId PlayerNode => new NodeId(_player.Node);
        public bool IsTravelling => _player.IsTravelling;
        /// <summary>The trip's destination, or <see cref="NodeId.None"/>.</summary>
        public NodeId TravelDestination => new NodeId(_player.TravelDestination);
        public int ArrivalTurn => _player.ArrivalTurn;
        /// <summary>Coins of the continent the player is in: what trades here are paid with.</summary>
        public long PlayerCash => _player.LocalCoins;
        public long PlayerCoins(int continent) => _player.Coins[continent];

        /// <summary>The player's licenses; each covers every grade of a base good inside one nation.</summary>
        public void GetLicenses(List<LicenseInfo> into) => LicenseSystem.Collect(_licenses, into);

        /// <summary>What a license for the good costs, in the coin of the nation that issues it.</summary>
        public long LicensePrice(GoodId good) => LicenseRules.Price(Database, good.BaseIndex);

        /// <summary>Whether the player may sell the good at their node now (no license needed, or a valid one held).</summary>
        public bool MaySell(GoodId good) => LicenseRules.PlayerMaySell(_world, Database, good, _player.Node, _clock.Turn);

        /// <summary>The cart's contents, sorted by good id.</summary>
        public void GetCart(List<CargoItem> into) => _cargo.Collect(CargoHolders.PlayerCart, into);
        public CargoLoad CartLoad => _cargo.LoadOf(CargoHolders.PlayerCart, _markets.GoodWeight, _markets.GoodSize);
        public CargoCapacity CartCapacity => new CargoCapacity(Database.Config.CartMaxWeight, Database.Config.CartMaxSize);
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

        /// <summary>Buys at the player's market; fills partially when stock, cart room or coins run short.</summary>
        public void BuyGoods(GoodId good, int quantity) => _queue.Enqueue(new BuyGoodsCommand(good, quantity));

        /// <summary>Sells from the cart; licensed goods need the license of the nation the player is in.</summary>
        public void SellGoods(GoodId good, int quantity) => _queue.Enqueue(new SellGoodsCommand(good, quantity));

        /// <summary>Buys, or renews for a year, the license for a good from the nation the player is in.</summary>
        public void BuyLicense(GoodId good) => _queue.Enqueue(new BuyLicenseCommand(good));

        public void BuyFacility(FacilityId facility, long price, GuidanceAxis axis) =>
            _queue.Enqueue(new BuyFacilityCommand(facility, price, axis));

        public void FoundCompany(NodeId headquarters, GuidanceAxis focus, long cost) =>
            _queue.Enqueue(new FoundCompanyCommand(headquarters, focus, cost));

        /// <summary>Loads (toWarehouse false) or unloads goods at a player-owned facility the player is at.</summary>
        public void MoveCargo(FacilityId facility, GoodId good, int quantity, bool toWarehouse) =>
            _queue.Enqueue(new MoveCargoCommand(facility, good, quantity, toWarehouse));

        /// <summary>Pays coins into (positive) or takes them out of (negative) a player-owned facility the player is at.</summary>
        public void MoveFacilityCash(FacilityId facility, long amount) => _queue.Enqueue(new MoveFacilityCashCommand(facility, amount));

        public void SetFacilityControl(FacilityId facility, ControlMode mode) =>
            _queue.Enqueue(new SetFacilityControlCommand(facility, mode));
    }
}
