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
        public readonly RouteTerrain Terrain;
        /// <summary>Paving condition in permille (0 = unpaved).</summary>
        public readonly int RoadCondition;
        public readonly bool RoadUnderConstruction;

        public RouteInfo(NodeId to, int travelTurns, int costPerUnit, int baseFee, RouteTerrain terrain, int roadCondition,
            bool roadUnderConstruction)
        {
            To = to;
            TravelTurns = travelTurns;
            CostPerUnit = costPerUnit;
            BaseFee = baseFee;
            Terrain = terrain;
            RoadCondition = roadCondition;
            RoadUnderConstruction = roadUnderConstruction;
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
        /// <summary>The interest group owning it, or -1.</summary>
        public readonly int Group;

        public FacilityInfo(FacilityId id, FacilityDef def, bool playerOwned, ControlMode mode, long cash, int group)
        {
            Id = id;
            Def = def;
            PlayerOwned = playerOwned;
            Mode = mode;
            Cash = cash;
            Group = group;
        }
    }

    public readonly struct GroupInfo
    {
        public readonly int Id;
        public readonly GroupKind Kind;
        public readonly NodeId Home;
        /// <summary>A guild's trade, or -1.</summary>
        public readonly int Category;
        public readonly long Cash;
        public readonly bool IsPlayer;

        public GroupInfo(int id, GroupKind kind, NodeId home, int category, long cash, bool isPlayer)
        {
            Id = id;
            Kind = kind;
            Home = home;
            Category = category;
            Cash = cash;
            IsPlayer = isPlayer;
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
        readonly NodeBanks _banks;
        readonly BankState _bank;
        readonly LoanTable _loans;
        readonly AuctionTable _auctions;

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
            _banks = world.Get<NodeBanks>();
            _bank = world.Get<BankState>();
            _loans = world.Get<LoanTable>();
            _auctions = world.Get<AuctionTable>();
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
        public bool TryQuoteTrip(NodeId destination, out TripQuote quote) =>
            PlayerTrip.TryQuote(_world, Database, destination, TripOptions.None, out quote);

        /// <summary>The same with guards and/or guild insurance bought for the trip.</summary>
        public bool TryQuoteTrip(NodeId destination, bool guards, bool insurance, out TripQuote quote) =>
            PlayerTrip.TryQuote(_world, Database, destination, new TripOptions(guards, insurance, false), out quote);

        public bool OwnsShip => _world.Get<PlayerShip>().Owned;
        /// <summary>The port where the ship waits, or <see cref="NodeId.None"/>.</summary>
        public NodeId ShipDockedAt => new NodeId(OwnsShip ? _world.Get<PlayerShip>().DockedAt : -1);
        public bool IsPort(NodeId node) => _world.Get<NodePorts>().Contains(node.Value);
        public long ShipPrice => Database.Config.ShipPrice;

        public void GetRoutesFrom(NodeId node, List<RouteInfo> into)
        {
            into.Clear();
            int start = _routes.OutStart[node.Value];
            for (int k = start; k < start + _routes.OutCount[node.Value]; k++)
            {
                int r = _routes.RouteIndex[k];
                var roads = _world.Get<RoadState>();
                into.Add(new RouteInfo(new NodeId(_routes.To[r]), _routes.TravelTurns[r], _routes.CostPerUnit[r], _routes.BaseFee[r],
                    (RouteTerrain)_routes.Terrain[r], roads.Condition[r], roads.BuildDoneTurn[r] >= 0));
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
                    _facilities.Owner[f] == OwnerIds.Player, (ControlMode)_facilities.Mode[f], _facilities.Cash[f], _facilities.Group[f]));
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
        public long LicensePrice(GoodId good)
        {
            int nation = _nodes.AncestorAt(_player.Node, WorldLevel.Nation);
            return nation < 0 ? LicenseRules.Price(Database, good.BaseIndex)
                : LicenseRules.PlayerPrice(_world, Database, good.BaseIndex, nation, _clock.Turn);
        }

        /// <summary>Whether the player may sell the good at their node now (no license needed, or a valid one held).</summary>
        public bool MaySell(GoodId good) => LicenseRules.PlayerMaySell(_world, Database, good, _player.Node, _clock.Turn);

        /// <summary>The cart's contents, sorted by good id.</summary>
        public void GetCart(List<CargoItem> into) => _cargo.Collect(CargoHolders.PlayerCart, into);
        public CargoLoad CartLoad => _cargo.LoadOf(CargoHolders.PlayerCart, _markets.GoodWeight, _markets.GoodSize);
        public CargoCapacity CartCapacity => new CargoCapacity(Database.Config.CartMaxWeight, Database.Config.CartMaxSize);
        public GuidanceAxis DominantGuidance => _guidance.Dominant;
        public Fx GuidanceWeight(GuidanceAxis axis) => _guidance[axis];

        // Interest groups

        public GroupInfo Group(int id)
        {
            var groups = _world.Get<GroupTable>();
            return new GroupInfo(id, groups.KindOf(id), new NodeId(groups.Node[id]), groups.Category[id], groups.Cash[id], groups.IsPlayer[id] != 0);
        }

        public int GroupCount => _world.Get<GroupTable>().Count;

        /// <summary>The player's company, or -1 before it is founded.</summary>
        public int PlayerCompany => _world.Get<GroupTable>().PlayerCompany();

        /// <summary>The guilds of a city (one per trade), by id.</summary>
        public void GetGuildsAt(NodeId city, List<GroupInfo> into)
        {
            into.Clear();
            var groups = _world.Get<GroupTable>();
            for (int g = 0; g < groups.Count; g++)
                if (groups.KindOf(g) == GroupKind.Guild && groups.Node[g] == city.Value) into.Add(Group(g));
        }

        public bool IsGuildMember(int guild) => _world.Get<GuildMembershipTable>().IsMember(guild, _clock.Turn);

        public void GetGuildHall(int guild, List<CargoItem> into) => _cargo.Collect(CargoHolders.GuildHall(guild), into);

        // Banks (a nation is given by its node)

        /// <summary>Whether the node has a branch of its nation's bank (city level and above).</summary>
        public bool IsBankBranch(NodeId node) => _banks.IsBranch(node.Value) && _banks.BranchNation(node.Value) >= 0;
        public bool IsInternationalBank(NodeId node) => _banks.IsInternationalBank(node.Value);
        /// <summary>The continent's international bank, or <see cref="NodeId.None"/>.</summary>
        public NodeId InternationalBankOf(int continent) => new NodeId(_banks.InternationalBankOf(continent));
        public long PlayerDeposit(NodeId nation) => _bank.PlayerDeposit[_banks.NationIndex(nation.Value)];
        public long BankReserve(NodeId nation) => _bank.Reserve[_banks.NationIndex(nation.Value)];
        public void GetLoans(List<LoanInfo> into) => BankSystem.CollectLoans(_loans, _banks, into);
        public void GetAuctions(List<AuctionInfo> into) => BankSystem.CollectAuctions(_auctions, _banks, into);

        /// <summary>What the player may still borrow, in the coin of the continent they are in.</summary>
        public long CreditLimit => new PlayerBalanceSheet(_world, Database).CreditLimit(_player.LocalContinent);
        /// <summary>Everything the player could pledge, in the coin of the continent they are in.</summary>
        public long CollateralValue => new PlayerBalanceSheet(_world, Database).CollateralValue(_player.LocalContinent);

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

        public void Travel(NodeId destination, bool guards = false, bool insurance = false) =>
            _queue.Enqueue(new TravelCommand(destination, guards, insurance));

        /// <summary>Buys a ship in a city: it replaces passage on water routes and carries more there.</summary>
        public void BuyShip() => _queue.Enqueue(new BuyShipCommand());

        /// <summary>Sells the ship at the port where it is docked.</summary>
        public void SellShip() => _queue.Enqueue(new SellShipCommand());

        /// <summary>Pays to pave the land route to <paramref name="destination"/> and back; the road is the player's to maintain.</summary>
        public void BuildRoad(NodeId destination) => _queue.Enqueue(new BuildRoadCommand(destination));

        /// <summary>Buys at the player's market; fills partially when stock, cart room or coins run short.</summary>
        public void BuyGoods(GoodId good, int quantity, bool payFromDeposit = false) =>
            _queue.Enqueue(new BuyGoodsCommand(good, quantity, payFromDeposit));

        /// <summary>Sells from the cart; licensed goods need the license of the nation the player is in.</summary>
        public void SellGoods(GoodId good, int quantity) => _queue.Enqueue(new SellGoodsCommand(good, quantity));

        /// <summary>Pays local coins into the account with the bank of the branch the player is at.</summary>
        public void Deposit(long amount) => _queue.Enqueue(new BankTransferCommand(amount));

        public void Withdraw(long amount) => _queue.Enqueue(new BankTransferCommand(-amount));

        /// <summary>Changes coins at the international bank the player is at; one side must be its own continent's coin.</summary>
        public void Exchange(int fromContinent, int toContinent, long amount) =>
            _queue.Enqueue(new ExchangeCommand(fromContinent, toContinent, amount));

        /// <summary>Borrows from the bank of the branch the player is at, against everything they own.</summary>
        public void TakeLoan(long amount) => _queue.Enqueue(new TakeLoanCommand(amount));

        public void RepayLoan(long amount) => _queue.Enqueue(new RepayLoanCommand(amount));

        /// <summary>Bids for a seized facility; paid when the auction closes.</summary>
        public void Bid(FacilityId facility, long amount) => _queue.Enqueue(new BidCommand(facility, amount));

        /// <summary>Joins, or renews for a year, a guild in the city the player is in.</summary>
        public void JoinGuild(int guild) => _queue.Enqueue(new JoinGuildCommand(guild));

        /// <summary>Moves goods between the cart and a member's guild hall in its city.</summary>
        public void MoveGuildHallCargo(int guild, GoodId good, int quantity, bool toHall) =>
            _queue.Enqueue(new MoveGuildHallCargoCommand(guild, good, quantity, toHall));

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
