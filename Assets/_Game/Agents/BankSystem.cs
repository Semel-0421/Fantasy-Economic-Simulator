using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class LoanTaken : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly long Amount;
        public readonly long Balance;
        public readonly long WeeklyPayment;

        public LoanTaken(NodeId nation, long amount, long balance, long weeklyPayment)
        {
            Nation = nation;
            Amount = amount;
            Balance = balance;
            WeeklyPayment = weeklyPayment;
        }
    }

    public sealed class LoanRepaid : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly int Turn;

        public LoanRepaid(NodeId nation, int turn)
        {
            Nation = nation;
            Turn = turn;
        }
    }

    public enum CollateralKind : byte
    {
        Deposit = 0,
        WarehouseCargo = 1,
        CartCargo = 2,
        Facility = 3,
        Ship = 4,
    }

    /// <summary>Collateral the bank took for an overdue loan; <see cref="Value"/> is in the lending bank's coin.</summary>
    public sealed class CollateralSeized : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly CollateralKind Kind;
        /// <summary>The facility put up for auction, or the warehouse's facility; otherwise none.</summary>
        public readonly FacilityId Facility;
        public readonly long Value;
        public readonly int Turn;

        public CollateralSeized(NodeId nation, CollateralKind kind, FacilityId facility, long value, int turn)
        {
            Nation = nation;
            Kind = kind;
            Facility = facility;
            Value = value;
            Turn = turn;
        }
    }

    public sealed class AuctionClosed : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly bool WonByPlayer;
        public readonly long Price;
        public readonly int Turn;

        public AuctionClosed(FacilityId facility, bool wonByPlayer, long price, int turn)
        {
            Facility = facility;
            WonByPlayer = wonByPlayer;
            Price = price;
            Turn = turn;
        }
    }

    /// <summary>
    /// Valuation shared by credit limits and seizure: everything the player can pledge (DEC-037, DEC-038),
    /// converted into one continent's coin at the exchange rate.
    /// </summary>
    public sealed class PlayerBalanceSheet
    {
        readonly GameDatabase _db;
        readonly NodeTable _nodes;
        readonly NodeBanks _banks;
        readonly BankState _bank;
        readonly LoanTable _loans;
        readonly FacilityTable _facilities;
        readonly CargoTable _cargo;
        readonly PlayerState _player;
        readonly PlayerShip _ship;
        readonly MarketStore _markets;
        readonly MarketFlows _flows;
        readonly CurrencyState _currency;
        readonly GroupTable _groups;
        readonly List<CargoItem> _items = new List<CargoItem>();
        readonly List<int> _holders = new List<int>();

        public PlayerBalanceSheet(WorldState world, GameDatabase db)
        {
            _db = db;
            _nodes = world.Get<NodeTable>();
            _banks = world.Get<NodeBanks>();
            _bank = world.Get<BankState>();
            _loans = world.Get<LoanTable>();
            _facilities = world.Get<FacilityTable>();
            _cargo = world.Get<CargoTable>();
            _player = world.Get<PlayerState>();
            _ship = world.Get<PlayerShip>();
            _markets = world.Get<MarketStore>();
            _flows = world.Get<MarketFlows>();
            _currency = world.Get<CurrencyState>();
            _groups = world.Get<GroupTable>();
        }

        /// <summary>Deposits, warehouse and cart cargo at market prices, and player facilities, in <paramref name="continent"/>'s coin.</summary>
        public long CollateralValue(int continent)
        {
            long value = 0;
            for (int n = 0; n < _bank.PlayerDeposit.Length; n++)
                value += Convert(_bank.PlayerDeposit[n], NationContinent(n), continent);
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Owner[f] != OwnerIds.Player) continue;
                int at = _facilities.Node[f];
                value += Convert(FacilityValue(f), _nodes.Continent[at], continent);
                value += Convert(CargoValue(CargoHolders.Warehouse(new FacilityId(f)), at), _nodes.Continent[at], continent);
            }
            value += Convert(CargoValue(CargoHolders.PlayerCart, _player.Node), _player.LocalContinent, continent);
            if (_ship.Owned) value += Convert(_db.Config.ShipPrice, _ship.BoughtContinent, continent);
            _cargo.CollectHolders(_holders);
            foreach (int holder in _holders)
            {
                if (!CargoHolders.IsGuildHall(holder, out int guild)) continue;
                int city = _groups.Node[guild];
                value += Convert(CargoValue(holder, city), _nodes.Continent[city], continent);
            }
            return value;
        }

        /// <summary>All loan balances, in <paramref name="continent"/>'s coin.</summary>
        public long Debt(int continent)
        {
            long debt = 0;
            for (int r = 0; r < _loans.Count; r++) debt += Convert(_loans.Balance[r], NationContinent(_loans.Nation[r]), continent);
            return debt;
        }

        /// <summary>How much more the player may borrow, in <paramref name="continent"/>'s coin.</summary>
        public long CreditLimit(int continent) =>
            Math.Max(0, _db.Config.LoanToValue.MulInt(CollateralValue(continent)) - Debt(continent));

        public long FacilityValue(int f) => NpcLending.FacilityValue(_db, _facilities, f);

        /// <summary>Cargo of a holder at the prices of the market nearest to <paramref name="node"/>; goods nobody trades count zero.</summary>
        public long CargoValue(int holder, int node)
        {
            _cargo.Collect(holder, _items);
            long value = 0;
            foreach (var item in _items)
                if (_flows.TryFindNearestMarket(node, item.Good, out _, out int cell)) value += (long)item.Quantity * _markets.Price[cell];
            return value;
        }

        public int NationContinent(int nation) => _nodes.Continent[_banks.NationNode(nation)];

        public long Convert(long amount, int from, int to) => _currency.Convert(amount, from, to);
    }

    /// <summary>
    /// Weekly banking (Finance phase, after the currency): hands newly minted coins from each international
    /// bank to its nations' banks (DEC-042), credits deposit interest, collects loan instalments, seizes
    /// collateral after the grace period, lends to NPCs short of cash and closes auctions.
    /// </summary>
    public sealed class BankSystem : ISimSystem
    {
        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        LoanTable _loans;
        AuctionTable _auctions;
        PlayerState _player;
        PlayerShip _ship;
        FacilityTable _facilities;
        CargoTable _cargo;
        MarketStore _markets;
        MarketFlows _flows;
        CurrencyState _currency;
        PlayerBalanceSheet _sheet;
        GroupDirectory _groups;
        GroupTable _groupTable;
        NpcLoanTable _npcLoans;
        AgentTable _agents;
        PurseState _purses;
        IncomeTaxState _books;
        readonly List<CargoItem> _items = new List<CargoItem>();
        readonly List<int> _holders = new List<int>();

        public string Id => "agents.banks";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Banks;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _loans = ctx.World.Get<LoanTable>();
            _auctions = ctx.World.Get<AuctionTable>();
            _player = ctx.World.Get<PlayerState>();
            _ship = ctx.World.Get<PlayerShip>();
            _facilities = ctx.World.Get<FacilityTable>();
            _cargo = ctx.World.Get<CargoTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _currency = ctx.World.Get<CurrencyState>();
            _sheet = new PlayerBalanceSheet(ctx.World, _db);
            _groupTable = ctx.World.Get<GroupTable>();
            _groups = new GroupDirectory(_groupTable, _nodes);
            _npcLoans = ctx.World.Get<NpcLoanTable>();
            _agents = ctx.World.Get<AgentTable>();
            _purses = ctx.World.Get<PurseState>();
            _books = ctx.World.Get<IncomeTaxState>();
        }

        /// <summary>Starting reserves: a config share of each continent's coin supply, split evenly over its nations.</summary>
        public static void SetUpReserves(BankState bank, NodeBanks banks, NodeTable nodes, CurrencyState currency, SimulationConfig config)
        {
            for (int c = 0; c < currency.ContinentCount; c++)
            {
                Distribute(bank, banks, nodes, c, config.BankInitialReserveShare.MulInt(currency.CoinSupply[c]));
                bank.DistributedSupply[c] = currency.CoinSupply[c];
            }
        }

        public void Execute(SimContext ctx)
        {
            for (int c = 0; c < _currency.ContinentCount; c++)
            {
                long minted = _currency.CoinSupply[c] - _bank.DistributedSupply[c];
                if (minted <= 0) continue;
                Distribute(_bank, _banks, _nodes, c, minted);
                _bank.DistributedSupply[c] = _currency.CoinSupply[c];
            }
            if (_db.Config.InternationalBalance) BalanceInternational();

            // Weekly share of the yearly rate; small deposits round down to no interest.
            for (int n = 0; n < _bank.PlayerDeposit.Length; n++)
                _bank.PlayerDeposit[n] += _db.Config.DepositInterestPerYear.MulInt(_bank.PlayerDeposit[n]) / SimClock.WeeksPerYear;

            for (int r = _loans.Count - 1; r >= 0; r--) Service(ctx, r);
            for (int r = _npcLoans.Count - 1; r >= 0; r--) ServiceNpc(ctx, r);
            LendToNpcs(ctx);
            for (int a = _auctions.Count - 1; a >= 0; a--)
                if (ctx.Turn >= _auctions.ClosesTurn[a]) Close(ctx, a);
        }

        // A surplus on trade leaves one international bank without its own coins and the other with too many, and stops the
        // trade. The nations of a continent refill theirs to its starting share of the coin supply and take back what it
        // holds above twice that (DEC-096).
        void BalanceInternational()
        {
            for (int c = 0; c < _currency.ContinentCount; c++)
            {
                long target = _db.Config.InternationalBankReserveShare.MulInt(_currency.CoinSupply[c]);
                long held = _bank.InternationalReserve[c];
                if (held > 2 * target)
                {
                    _bank.InternationalReserve[c] = 2 * target;
                    Distribute(_bank, _banks, _nodes, c, held - 2 * target);
                    continue;
                }
                long need = target - held;
                for (int n = 0; n < _banks.NationCount && need > 0; n++)
                {
                    if (_nodes.Continent[_banks.NationNode(n)] != c) continue;
                    long take = Math.Min(need, _bank.Spendable(n));
                    _bank.Reserve[n] -= take;
                    _bank.InternationalReserve[c] += take;
                    need -= take;
                }
            }
        }

        // The international bank hands coins on to the nations of its continent; the remainder stays with it.
        static void Distribute(BankState bank, NodeBanks banks, NodeTable nodes, int continent, long amount)
        {
            int first = -1, count = 0;
            for (int n = 0; n < banks.NationCount; n++)
            {
                if (nodes.Continent[banks.NationNode(n)] != continent) continue;
                if (first < 0) first = n;
                count++;
            }
            if (count == 0)
            {
                bank.InternationalReserve[continent] += amount;
                return;
            }
            long share = amount / count;
            for (int n = first; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent) bank.Reserve[n] += share;
            bank.InternationalReserve[continent] += amount - share * count;
        }

        // Instalments come out of the deposit with the lending bank first, then the coins carried (DEC-037).
        void Service(SimContext ctx, int r)
        {
            int nation = _loans.Nation[r];
            int continent = _sheet.NationContinent(nation);
            _loans.Due[r] = Math.Min(_loans.Balance[r], _loans.Due[r] + _loans.WeeklyPayment[r]);
            long fromDeposit = Math.Min(_loans.Due[r], _bank.PlayerDeposit[nation]);
            _bank.PlayerDeposit[nation] -= fromDeposit;
            _bank.Reserve[nation] -= fromDeposit; // Repay puts it back: the deposit already sat in the reserve
            long fromCoins = Math.Min(_loans.Due[r] - fromDeposit, Math.Max(0, _player.Coins[continent]));
            _player.Coins[continent] -= fromCoins;
            Repay(r, fromDeposit + fromCoins);

            if (_loans.Balance[r] == 0)
            {
                _loans.RemoveRow(r);
                ctx.Events.Publish(new LoanRepaid(new NodeId(_banks.NationNode(nation)), ctx.Turn));
                return;
            }
            _loans.OverdueWeeks[r] = _loans.Due[r] > 0 ? _loans.OverdueWeeks[r] + 1 : 0;
            if (_loans.OverdueWeeks[r] > _db.Config.LoanGraceWeeks) Seize(ctx, r);
        }

        void Repay(int r, long amount)
        {
            _loans.Balance[r] -= amount;
            _loans.Due[r] = Math.Max(0, _loans.Due[r] - amount);
            _bank.Reserve[_loans.Nation[r]] += amount;
        }

        /// <summary>
        /// Takes collateral until the whole balance is covered: deposits, then warehouse cargo, then the cart
        /// when the player is at a branch of the lending nation, then the ship, then facilities, which go to
        /// auction ([Draft] order, Claude's proposal).
        /// </summary>
        void Seize(SimContext ctx, int r)
        {
            int nation = _loans.Nation[r];
            int continent = _sheet.NationContinent(nation);
            var nationNode = new NodeId(_banks.NationNode(nation));

            for (int n = 0; n < _bank.PlayerDeposit.Length && Owed(r) > 0; n++)
            {
                int from = _sheet.NationContinent(n);
                long take = Math.Min(_bank.PayableDeposit(n), ToCover(Owed(r), from, continent));
                if (take <= 0) continue;
                // The deposit's coins sit in its bank's reserve, as far as it has not lent them: they move to the lender.
                _bank.PlayerDeposit[n] -= take;
                _bank.Reserve[n] -= take;
                long value = _sheet.Convert(take, from, continent);
                MoneyLedger.AcrossContinents(_bank, from, take, continent, value);
                long paid = Math.Min(value, _loans.Balance[r]);
                Repay(r, paid);
                _bank.Reserve[nation] += value - paid;
                ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.Deposit, FacilityId.None, value, ctx.Turn));
            }

            for (int f = 0; f < _facilities.Count && Owed(r) > 0; f++)
            {
                if (_facilities.Owner[f] != OwnerIds.Player) continue;
                long value = SellCargo(r, CargoHolders.Warehouse(new FacilityId(f)), _facilities.Node[f]);
                if (value > 0) ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.WarehouseCargo, new FacilityId(f), value, ctx.Turn));
            }

            _cargo.CollectHolders(_holders);
            foreach (int holder in _holders)
            {
                if (Owed(r) <= 0) break;
                if (!CargoHolders.IsGuildHall(holder, out int guild)) continue;
                long value = SellCargo(r, holder, _groupTable.Node[guild]);
                if (value > 0) ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.WarehouseCargo, FacilityId.None, value, ctx.Turn));
            }

            // A moving cart has no fixed place: it is taken when the player is next at one of the lender's branches.
            if (Owed(r) > 0 && !_player.IsTravelling && _banks.BranchNation(_player.Node) == nation)
            {
                long value = SellCargo(r, CargoHolders.PlayerCart, _player.Node);
                if (value > 0) ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.CartCargo, FacilityId.None, value, ctx.Turn));
            }

            // A ship is sold to the bank at the auction share of its price ([Draft]).
            if (Owed(r) > 0 && _ship.Owned)
            {
                long value = _sheet.Convert(_db.Config.AuctionReserveShare.MulInt(_db.Config.ShipPrice), _ship.BoughtContinent, continent);
                _ship.Owned = false;
                _purses.Float[nation] -= value; // [Draft] the lending nation's traders buy it
                long paid = Math.Min(value, Owed(r));
                Repay(r, paid);
                _bank.Reserve[nation] += value - paid;
                ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.Ship, FacilityId.None, value, ctx.Turn));
            }

            for (int f = 0; f < _facilities.Count && Owed(r) - PendingAuctions(nation, continent) > 0; f++)
            {
                if (_facilities.Owner[f] != OwnerIds.Player) continue;
                long reserve = _db.Config.AuctionReserveShare.MulInt(_sheet.FacilityValue(f));
                _facilities.Owner[f] = OwnerIds.Bank;
                _facilities.Mode[f] = (byte)ControlMode.Automated;
                _auctions.Add(f, nation, ctx.Turn + _db.Config.AuctionWeeks, reserve);
                long value = _sheet.Convert(reserve, _nodes.Continent[_facilities.Node[f]], continent);
                ctx.Events.Publish(new CollateralSeized(nationNode, CollateralKind.Facility, new FacilityId(f), value, ctx.Turn));
            }
        }

        long Owed(int r) => _loans.Balance[r];

        // Instalments come out of the borrower's cash; after the grace period the bank takes the facility, or
        // a merchant group's cargo, and writes off what that does not cover (DEC-037, DEC-038).
        void ServiceNpc(SimContext ctx, int r)
        {
            int borrower = _npcLoans.Borrower[r];
            bool merchant = NpcBorrowers.IsMerchant(borrower, out int a);
            long cash = merchant ? _agents.Cash[a] : _facilities.Cash[borrower];
            _npcLoans.Due[r] = Math.Min(_npcLoans.Balance[r], _npcLoans.Due[r] + _npcLoans.WeeklyPayment[r]);
            long paid = Math.Min(_npcLoans.Due[r], Math.Max(0, cash));
            if (merchant)
            {
                _agents.Cash[a] -= paid;
                _books.MoveMerchantCapital(a, -paid);
                ctx.World.Get<MerchantTripStats>().SpentLoans += paid;
            }
            else
            {
                _facilities.Cash[borrower] -= paid;
                _books.MoveFacilityCapital(borrower, -paid);
            }
            _npcLoans.Balance[r] -= paid;
            _npcLoans.Due[r] -= paid;
            _bank.Reserve[_npcLoans.Nation[r]] += paid;
            if (_npcLoans.Balance[r] == 0)
            {
                _npcLoans.RemoveRow(r);
                return;
            }
            _npcLoans.OverdueWeeks[r] = _npcLoans.Due[r] > 0 ? _npcLoans.OverdueWeeks[r] + 1 : 0;
            if (_npcLoans.OverdueWeeks[r] <= _db.Config.LoanGraceWeeks) return;
            if (!merchant)
            {
                NpcLending.SeizeFacility(ctx, borrower);
                return;
            }

            int nation = _npcLoans.Nation[r];
            int holder = CargoHolders.Merchant(new AgentId(a));
            _cargo.Collect(holder, _items);
            foreach (var item in _items)
            {
                if (_nodes.Continent[_agents.Node[a]] != _nodes.Continent[_agents.Home[a]]) break; // out of the bank's reach abroad
                if (!_flows.TryFindNearestMarket(_agents.Node[a], item.Good, out _, out int cell)) continue;
                // Only what the traders there can pay for is sold; the rest stays on board (DEC-061).
                int units = _purses.Sellable(_agents.Node[a], item.Quantity, _markets.Price[cell]);
                if (units <= 0) continue;
                _cargo.Change(holder, item.Good, -units);
                _flows.Sell(cell, units);
                // Proceeds beyond the debt go to the bank too (DEC-039).
                _bank.Reserve[nation] += (long)units * _markets.Price[cell];
                _purses.ChangeFloat(_agents.Node[a], -(long)units * _markets.Price[cell]);
            }
            _agents.LoadCost[a] = 0;
            _npcLoans.RemoveRow(r);
            ctx.Events.Publish(new NpcLoanDefaulted(FacilityId.None, new AgentId(a), new NodeId(_banks.NationNode(nation)), ctx.Turn));
        }

        // NPCs whose cash ran out borrow the shortfall plus a few weeks of upkeep, up to the loan-to-value share
        // of their collateral: a facility's value, or the cargo a merchant group carries (DEC-037, DEC-038).
        void LendToNpcs(SimContext ctx)
        {
            var config = _db.Config;
            if (config.NpcLoanMaxReserveShare.Raw <= 0) return;
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Cash[f] >= 0 || _facilities.Owner[f] != OwnerIds.Npc) continue;
                int nation = _banks.NationOf(_facilities.Node[f]);
                if (nation < 0 || _npcLoans.Find(NpcBorrowers.Facility(f)) >= 0) continue;
                long need = -_facilities.Cash[f] + config.NpcLoanBufferWeeks * NpcLending.FacilityUpkeep(_db, _facilities, f);
                long limit = config.LoanToValue.MulInt(NpcLending.FacilityValue(_db, _facilities, f));
                long lent = NpcLending.Lend(ctx.World, config, NpcBorrowers.Facility(f), nation, Math.Min(need, limit));
                _facilities.Cash[f] += lent;
                _books.MoveFacilityCapital(f, lent);
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Cash[a] >= 0 || _agents.Owner[a] != OwnerIds.Npc) continue;
                int node = MoneyLedger.MerchantSettlesAt(_nodes, _agents, a);
                int nation = _banks.NationOf(node);
                if (nation < 0 || _npcLoans.Find(NpcBorrowers.Merchant(a)) >= 0) continue;
                long need = -_agents.Cash[a] + config.NpcLoanBufferWeeks * config.MerchantUpkeep[_agents.Scale[a]];
                long limit = config.LoanToValue.MulInt(_sheet.CargoValue(CargoHolders.Merchant(new AgentId(a)), node));
                long lent = NpcLending.Lend(ctx.World, config, NpcBorrowers.Merchant(a), nation, Math.Min(need, limit));
                _agents.Cash[a] += lent;
                _books.MoveMerchantCapital(a, lent);
            }
        }

        long PendingAuctions(int nation, int continent)
        {
            long sum = 0;
            for (int a = 0; a < _auctions.Count; a++)
                if (_auctions.Nation[a] == nation && _auctions.ForPlayerLoan[a] != 0)
                    sum += _sheet.Convert(_auctions.ReservePrice[a], _nodes.Continent[_facilities.Node[_auctions.Facility[a]]], continent);
            return sum;
        }

        // An amount of `from` coins worth at least `owed` lender coins (at least one coin); rounding makes the
        // plain conversion fall short by a little, so step up until it covers.
        long ToCover(long owed, int from, int to)
        {
            long amount = Math.Max(1, _sheet.Convert(owed, to, from));
            while (_sheet.Convert(amount, from, to) < owed) amount += Math.Max(1, amount / 1000);
            return amount;
        }

        // Sells a holder's goods at the market nearest to `node` until the loan is covered; proceeds repay it.
        long SellCargo(int r, int holder, int node)
        {
            int continent = _sheet.NationContinent(_loans.Nation[r]);
            long total = 0;
            _cargo.Collect(holder, _items);
            foreach (var item in _items)
            {
                if (Owed(r) <= 0) break;
                if (!_flows.TryFindNearestMarket(node, item.Good, out _, out int cell)) continue;
                int price = _markets.Price[cell];
                long needed = ToCover(Owed(r), _nodes.Continent[node], continent);
                int units = _purses.Sellable(node, (int)Math.Min(item.Quantity, (needed + price - 1) / price), price);
                if (units <= 0) continue;
                _cargo.Change(holder, item.Good, -units);
                _flows.Sell(cell, units);
                _purses.ChangeFloat(node, -(long)units * price);
                long value = _sheet.Convert((long)units * price, _nodes.Continent[node], continent);
                MoneyLedger.AcrossContinents(_bank, _nodes.Continent[node], (long)units * price, continent, value);
                // Proceeds beyond the debt go to the bank, not the former owner (DEC-039).
                long paid = Math.Min(value, Owed(r));
                Repay(r, paid);
                _bank.Reserve[_loans.Nation[r]] += value - paid;
                total += value;
            }
            return total;
        }

        // Without a winning bid the facility goes to a new NPC operator at the reserve price (DEC-039).
        void Close(SimContext ctx, int a)
        {
            int f = _auctions.Facility[a];
            int nation = _auctions.Nation[a];
            bool forPlayerLoan = _auctions.ForPlayerLoan[a] != 0;
            int facilityContinent = _nodes.Continent[_facilities.Node[f]];
            long bid = _auctions.PlayerBid[a];
            bool won = bid >= _auctions.ReservePrice[a] && bid > 0 && Pay(facilityContinent, bid);
            long price = won ? bid : _auctions.ReservePrice[a];
            _auctions.RemoveRow(a);

            _facilities.Owner[f] = won ? OwnerIds.Player : OwnerIds.Npc;
            _facilities.Mode[f] = (byte)(won ? ControlMode.Manual : ControlMode.Automated);
            // [Draft] Without a winning bid a local family buys it.
            _facilities.Group[f] = won ? _groupTable.PlayerCompany() : MoneyLedger.FamilyAt(ctx.World, _groups, _db.Config, _facilities.Node[f]);
            if (!won)
            {
                // The buyer pays the price, and its new manager's starting cash, from its cash (DEC-061).
                MoneyLedger.PayFromGroup(ctx.World, _facilities.Group[f], _facilities.Node[f], price);
                _facilities.Cash[f] = MoneyLedger.FundNewOperator(ctx.World, _facilities.Group[f], _facilities.Node[f], _facilities.Cash[f],
                    _db.Config.FacilityStartCash[(int)_db.Facilities[_facilities.DefIndex[f]].Tier]);
                ctx.World.Get<IncomeTaxState>().RestartFacility(f, _facilities.Cash[f]);
                _facilities.NegativeCashWeeks[f] = 0;
            }

            long value = _sheet.Convert(price, facilityContinent, _sheet.NationContinent(nation));
            MoneyLedger.AcrossContinents(_bank, facilityContinent, price, _sheet.NationContinent(nation), value);
            int r = forPlayerLoan ? _loans.Find(nation) : -1;
            long paid = r < 0 ? 0 : Math.Min(value, _loans.Balance[r]);
            if (paid > 0) Repay(r, paid);
            _bank.Reserve[nation] += value - paid;
            if (r >= 0 && _loans.Balance[r] == 0)
            {
                _loans.RemoveRow(r);
                ctx.Events.Publish(new LoanRepaid(new NodeId(_banks.NationNode(nation)), ctx.Turn));
            }
            ctx.Events.Publish(new AuctionClosed(new FacilityId(f), won, price, ctx.Turn));
        }

        // A winning bid is paid from the coins of the facility's continent, then deposits there.
        bool Pay(int continent, long amount)
        {
            long deposits = 0;
            for (int n = 0; n < _bank.PlayerDeposit.Length; n++)
                if (_sheet.NationContinent(n) == continent) deposits += _bank.PayableDeposit(n);
            if (_player.Coins[continent] + deposits < amount) return false;
            long fromCoins = Math.Min(amount, Math.Max(0, _player.Coins[continent]));
            _player.Coins[continent] -= fromCoins;
            long rest = amount - fromCoins;
            for (int n = 0; n < _bank.PlayerDeposit.Length && rest > 0; n++)
            {
                if (_sheet.NationContinent(n) != continent) continue;
                long take = Math.Min(rest, _bank.PayableDeposit(n));
                _bank.PlayerDeposit[n] -= take;
                _bank.Reserve[n] -= take;
                rest -= take;
            }
            return true;
        }

        public static void CollectLoans(LoanTable loans, NodeBanks banks, List<LoanInfo> into)
        {
            into.Clear();
            for (int r = 0; r < loans.Count; r++)
                into.Add(new LoanInfo(new NodeId(banks.NationNode(loans.Nation[r])), loans.Balance[r], loans.WeeklyPayment[r],
                    loans.Due[r], loans.OverdueWeeks[r]));
        }

        public static void CollectAuctions(AuctionTable auctions, NodeBanks banks, List<AuctionInfo> into)
        {
            into.Clear();
            for (int a = 0; a < auctions.Count; a++)
                into.Add(new AuctionInfo(new FacilityId(auctions.Facility[a]), new NodeId(banks.NationNode(auctions.Nation[a])),
                    auctions.ClosesTurn[a], auctions.ReservePrice[a], auctions.PlayerBid[a]));
        }
    }
}
