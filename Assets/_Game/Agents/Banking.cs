using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Money held by the state banks (SYS-BANKING): each nation's bank reserve and the player's deposit with
    /// it, in its continent's coin, plus each continent's international bank. Branches of one nation share
    /// one account and one reserve (DEC-032).
    /// </summary>
    public sealed class BankState : IPersistentComponent
    {
        /// <summary>Per nation index: the bank's own coins, deposits included. Loans are paid out of it (DEC-046).</summary>
        public readonly long[] Reserve;
        /// <summary>Per nation index: the player's deposit.</summary>
        public readonly long[] PlayerDeposit;
        /// <summary>Per continent: coins the international bank holds (minting remainders, exchange fees).</summary>
        public readonly long[] InternationalReserve;
        /// <summary>Per continent: coin supply already handed to the banks, so only newly minted coins are distributed.</summary>
        public readonly long[] DistributedSupply;

        public string Key => "agents.banks";
        public int Version => 1;

        public BankState(int nationCount, int continentCount)
        {
            Reserve = new long[nationCount];
            PlayerDeposit = new long[nationCount];
            InternationalReserve = new long[continentCount];
            DistributedSupply = new long[continentCount];
        }

        public void Write(BinaryWriter writer)
        {
            Write(writer, Reserve);
            Write(writer, PlayerDeposit);
            Write(writer, InternationalReserve);
            Write(writer, DistributedSupply);
        }

        public void Read(BinaryReader reader)
        {
            Read(reader, Reserve);
            Read(reader, PlayerDeposit);
            Read(reader, InternationalReserve);
            Read(reader, DistributedSupply);
        }

        static void Write(BinaryWriter writer, long[] values)
        {
            writer.Write(values.Length);
            foreach (long v in values) writer.Write(v);
        }

        static void Read(BinaryReader reader, long[] values)
        {
            int count = reader.ReadInt32();
            if (count != values.Length) throw new InvalidDataException($"Saved bank count {count} != {values.Length}.");
            for (int i = 0; i < count; i++) values[i] = reader.ReadInt64();
        }
    }

    /// <summary>The player's loans (SoA), at most one per nation's bank, in that nation's continent coin.</summary>
    public sealed class LoanTable : NativeTable
    {
        public readonly NativeColumn<int> Nation;
        /// <summary>Everything still owed, interest included.</summary>
        public readonly NativeColumn<long> Balance;
        public readonly NativeColumn<long> WeeklyPayment;
        /// <summary>Owed now: this week's instalment plus arrears.</summary>
        public readonly NativeColumn<long> Due;
        /// <summary>Consecutive weeks that ended with something still due.</summary>
        public readonly NativeColumn<int> OverdueWeeks;

        public override string Key => "agents.loans";
        public override int Version => 1;

        public LoanTable()
        {
            Nation = Column<int>();
            Balance = Column<long>();
            WeeklyPayment = Column<long>();
            Due = Column<long>();
            OverdueWeeks = Column<int>();
        }

        public int Find(int nation)
        {
            for (int r = 0; r < Count; r++)
                if (Nation[r] == nation) return r;
            return -1;
        }

        public void Add(int nation, long balance, long weekly)
        {
            Nation.Add(nation);
            Balance.Add(balance);
            WeeklyPayment.Add(weekly);
            Due.Add(0);
            OverdueWeeks.Add(0);
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>Seized facilities up for auction (SoA, DEC-039). The facility belongs to the bank meanwhile.</summary>
    public sealed class AuctionTable : NativeTable
    {
        public readonly NativeColumn<int> Facility;
        /// <summary>Nation index of the bank selling it.</summary>
        public readonly NativeColumn<int> Nation;
        /// <summary>The auction closes at the end of this turn.</summary>
        public readonly NativeColumn<int> ClosesTurn;
        /// <summary>In the facility's continent coin; an NPC buys at this price when no bid wins.</summary>
        public readonly NativeColumn<long> ReservePrice;
        /// <summary>The player's bid, or 0.</summary>
        public readonly NativeColumn<long> PlayerBid;
        /// <summary>1 when the sale repays the player's loan; 0 for a facility seized from an NPC, whose loan is already closed.</summary>
        public readonly NativeColumn<byte> ForPlayerLoan;

        public const string SectionKey = "agents.auctions";
        public override string Key => SectionKey;
        public override int Version => 2;

        public AuctionTable()
        {
            Facility = Column<int>();
            Nation = Column<int>();
            ClosesTurn = Column<int>();
            ReservePrice = Column<long>();
            PlayerBid = Column<long>();
            ForPlayerLoan = Column<byte>();
        }

        public int Find(FacilityId facility)
        {
            for (int r = 0; r < Count; r++)
                if (Facility[r] == facility.Value) return r;
            return -1;
        }

        public void Add(int facility, int nation, int closesTurn, long reservePrice, bool forPlayerLoan = true)
        {
            Facility.Add(facility);
            Nation.Add(nation);
            ClosesTurn.Add(closesTurn);
            ReservePrice.Add(reservePrice);
            PlayerBid.Add(0);
            ForPlayerLoan.Add((byte)(forPlayerLoan ? 1 : 0));
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>Version 1 auctioned only the player's facilities.</summary>
    public sealed class AuctionTableV1Migration : ISaveMigration
    {
        public string SectionKey => AuctionTable.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write((byte)1);
            }
            return output.ToArray();
        }
    }

    public readonly struct LoanInfo
    {
        public readonly NodeId Nation;
        public readonly long Balance;
        public readonly long WeeklyPayment;
        public readonly long Due;
        public readonly int OverdueWeeks;

        public LoanInfo(NodeId nation, long balance, long weeklyPayment, long due, int overdueWeeks)
        {
            Nation = nation;
            Balance = balance;
            WeeklyPayment = weeklyPayment;
            Due = due;
            OverdueWeeks = overdueWeeks;
        }
    }

    public readonly struct AuctionInfo
    {
        public readonly FacilityId Facility;
        public readonly NodeId Nation;
        public readonly int ClosesTurn;
        public readonly long ReservePrice;
        public readonly long PlayerBid;

        public AuctionInfo(FacilityId facility, NodeId nation, int closesTurn, long reservePrice, long playerBid)
        {
            Facility = facility;
            Nation = nation;
            ClosesTurn = closesTurn;
            ReservePrice = reservePrice;
            PlayerBid = playerBid;
        }
    }

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
        readonly List<CargoItem> _items = new List<CargoItem>();
        readonly List<int> _holders = new List<int>();

        public string Id => "agents.banks";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late;

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

            // Weekly share of the yearly rate; small deposits round down to no interest.
            for (int n = 0; n < _bank.PlayerDeposit.Length; n++)
                _bank.PlayerDeposit[n] += _db.Config.DepositInterestPerYear.MulInt(_bank.PlayerDeposit[n]) / SimClock.WeeksPerYear;

            for (int r = _loans.Count - 1; r >= 0; r--) Service(ctx, r);
            for (int r = _npcLoans.Count - 1; r >= 0; r--) ServiceNpc(ctx, r);
            LendToNpcs(ctx);
            for (int a = _auctions.Count - 1; a >= 0; a--)
                if (ctx.Turn >= _auctions.ClosesTurn[a]) Close(ctx, a);
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
                long take = Math.Min(_bank.PlayerDeposit[n], ToCover(Owed(r), from, continent));
                if (take <= 0) continue;
                // The deposit's coins already sit in its bank's reserve: they move to the lender.
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
                _purses.Purse[nation] -= value; // [Draft] the lending nation's people buy it
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
            if (merchant) _agents.Cash[a] -= paid;
            else _facilities.Cash[borrower] -= paid;
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
                if (!_flows.TryFindNearestMarket(_agents.Node[a], item.Good, out _, out int cell)) continue;
                _cargo.Change(holder, item.Good, -item.Quantity);
                _flows.Sell(cell, item.Quantity);
                // Proceeds beyond the debt go to the bank too (DEC-039).
                _bank.Reserve[nation] += (long)item.Quantity * _markets.Price[cell];
                _purses.Change(_agents.Node[a], -(long)item.Quantity * _markets.Price[cell]);
            }
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
                int nation = NationOf(_facilities.Node[f]);
                if (nation < 0 || _npcLoans.Find(NpcBorrowers.Facility(f)) >= 0) continue;
                long need = -_facilities.Cash[f] + config.NpcLoanBufferWeeks * NpcLending.FacilityUpkeep(_db, _facilities, f);
                long limit = config.LoanToValue.MulInt(NpcLending.FacilityValue(_db, _facilities, f));
                _facilities.Cash[f] += NpcLending.Lend(ctx.World, config, NpcBorrowers.Facility(f), nation, Math.Min(need, limit));
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Cash[a] >= 0 || _agents.Owner[a] != OwnerIds.Npc) continue;
                int node = _agents.Node[a];
                int nation = NationOf(node);
                if (nation < 0 || _npcLoans.Find(NpcBorrowers.Merchant(a)) >= 0) continue;
                long need = -_agents.Cash[a] + config.NpcLoanBufferWeeks * config.MerchantUpkeep[_agents.Scale[a]];
                long limit = config.LoanToValue.MulInt(_sheet.CargoValue(CargoHolders.Merchant(new AgentId(a)), node));
                _agents.Cash[a] += NpcLending.Lend(ctx.World, config, NpcBorrowers.Merchant(a), nation, Math.Min(need, limit));
            }
        }

        int NationOf(int node)
        {
            int nationNode = _nodes.AncestorAt(node, WorldLevel.Nation);
            return nationNode < 0 ? -1 : _banks.NationIndex(nationNode);
        }

        long PendingAuctions(int nation, int continent)
        {
            long sum = 0;
            for (int a = 0; a < _auctions.Count; a++)
                if (_auctions.Nation[a] == nation)
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
                int units = (int)Math.Min(item.Quantity, (needed + price - 1) / price);
                _cargo.Change(holder, item.Good, -units);
                _flows.Sell(cell, units);
                _purses.Change(node, -(long)units * price);
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
            _facilities.Group[f] = won ? _groupTable.PlayerCompany() : _groups.PrivateAt(_facilities.Node[f], _db.Config);
            if (!won)
            {
                // The buyer pays the price, and its new manager's starting cash, from its cash (DEC-061).
                MoneyLedger.PayFromGroup(ctx.World, _facilities.Group[f], _facilities.Node[f], price);
                _facilities.Cash[f] = MoneyLedger.FundNewOperator(ctx.World, _facilities.Group[f], _facilities.Node[f], _facilities.Cash[f],
                    _db.Config.FacilityStartCash[(int)_db.Facilities[_facilities.DefIndex[f]].Tier]);
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
                if (_sheet.NationContinent(n) == continent) deposits += _bank.PlayerDeposit[n];
            if (_player.Coins[continent] + deposits < amount) return false;
            long fromCoins = Math.Min(amount, Math.Max(0, _player.Coins[continent]));
            _player.Coins[continent] -= fromCoins;
            long rest = amount - fromCoins;
            for (int n = 0; n < _bank.PlayerDeposit.Length && rest > 0; n++)
            {
                if (_sheet.NationContinent(n) != continent) continue;
                long take = Math.Min(rest, _bank.PlayerDeposit[n]);
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

    static class PlayerBank
    {
        /// <summary>The nation index of the bank branch where the player stands, or -1 with an error.</summary>
        public static int Branch(SimContext ctx, out string error)
        {
            var player = ctx.World.Get<PlayerState>();
            int nation = player.IsTravelling ? -1 : ctx.World.Get<NodeBanks>().BranchNation(player.Node);
            error = nation < 0 ? "There is no bank branch here." : null;
            return nation;
        }
    }

    /// <summary>
    /// Pays local coins into (positive amount) or takes them out of (negative) the player's account with the
    /// bank of the nation whose branch they are at. Deposits sit in the bank's reserve, so a withdrawal needs
    /// both the balance and the bank's coins.
    /// </summary>
    public sealed class BankTransferCommand : ICommand
    {
        public readonly long Amount;

        public BankTransferCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount == 0 || Amount == long.MinValue) return CommandResult.Fail("Amount must not be zero.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            var bank = ctx.World.Get<BankState>();
            if (Amount > 0) return ctx.World.Get<PlayerState>().LocalCoins >= Amount ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
            if (bank.PlayerDeposit[nation] < -Amount) return CommandResult.Fail("The account does not hold that much.");
            var finance = ctx.World.Get<FinanceState>();
            if (finance.EventOf(nation) == FinancialEventKind.BankRun)
            {
                // During a run the bank pays out a share of the deposit, once a week (DEC-044).
                if (finance.LastWithdrawTurn[nation] == ctx.Turn) return CommandResult.Fail("During a bank run, one withdrawal a week.");
                long cap = ctx.Services.Get<GameDatabase>().Config.BankRunWithdrawShare.MulInt(bank.PlayerDeposit[nation]);
                if (-Amount > cap) return CommandResult.Fail($"During a bank run, at most {cap} can be withdrawn.");
            }
            return bank.Reserve[nation] >= -Amount ? CommandResult.Success : CommandResult.Fail("The bank cannot pay that out now.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var bank = ctx.World.Get<BankState>();
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            bank.PlayerDeposit[nation] += Amount;
            bank.Reserve[nation] += Amount;
            if (Amount < 0) ctx.World.Get<FinanceState>().LastWithdrawTurn[nation] = ctx.Turn;
        }
    }

    public sealed class CoinsExchanged : ISimEvent
    {
        public readonly int FromContinent;
        public readonly int ToContinent;
        public readonly long Paid;
        public readonly long Received;
        public readonly long Fee;

        public CoinsExchanged(int fromContinent, int toContinent, long paid, long received, long fee)
        {
            FromContinent = fromContinent;
            ToContinent = toContinent;
            Paid = paid;
            Received = received;
            Fee = fee;
        }
    }

    /// <summary>
    /// Changes coins at the international bank of the continent the player is in (DEC-031, DEC-036): local
    /// coins into another continent's or back. The rate follows the price indices; the bank keeps a fee.
    /// </summary>
    public sealed class ExchangeCommand : ICommand
    {
        public readonly int FromContinent;
        public readonly int ToContinent;
        public readonly long Amount;

        public ExchangeCommand(int fromContinent, int toContinent, long amount)
        {
            FromContinent = fromContinent;
            ToContinent = toContinent;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            int continents = player.Coins.Length;
            if (FromContinent < 0 || FromContinent >= continents || ToContinent < 0 || ToContinent >= continents || FromContinent == ToContinent)
                return CommandResult.Fail("Pick two different currencies.");
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            if (player.IsTravelling || !ctx.World.Get<NodeBanks>().IsInternationalBank(player.Node))
                return CommandResult.Fail("Coins are exchanged only at an international bank.");
            if (FromContinent != player.LocalContinent && ToContinent != player.LocalContinent)
                return CommandResult.Fail("This bank changes only its own continent's coin.");
            if (player.Coins[FromContinent] < Amount) return CommandResult.Fail("Not enough coins.");
            long received = Quote(ctx, out _);
            if (received <= 0) return CommandResult.Fail("The amount is too small to exchange.");
            return ctx.World.Get<BankState>().InternationalReserve[ToContinent] >= received
                ? CommandResult.Success
                : CommandResult.Fail("The international banks hold too few of those coins now.");
        }

        // The coins handed in go to their continent's international bank and the coins handed out come from
        // theirs, so no continent's money changes (DEC-061); the fee stays with the banks.
        public void Apply(SimContext ctx)
        {
            long received = Quote(ctx, out long fee);
            var player = ctx.World.Get<PlayerState>();
            var bank = ctx.World.Get<BankState>();
            player.Coins[FromContinent] -= Amount;
            bank.InternationalReserve[FromContinent] += Amount;
            player.Coins[ToContinent] += received;
            bank.InternationalReserve[ToContinent] -= received;
            ctx.Events.Publish(new CoinsExchanged(FromContinent, ToContinent, Amount, received, fee));
        }

        long Quote(SimContext ctx, out long fee)
        {
            long gross = ctx.World.Get<CurrencyState>().Convert(Amount, FromContinent, ToContinent);
            fee = ctx.Services.Get<GameDatabase>().Config.ExchangeFee.MulInt(gross);
            return gross - fee;
        }
    }

    /// <summary>
    /// Borrows from the bank whose branch the player is at, in local coins: up to the credit limit on the
    /// collateral and the bank's reserve (DEC-037, DEC-046), unless a credit crunch stops lending. Interest at
    /// the bank's current rate for the term is added up front and the whole is repaid in equal weekly
    /// instalments. One loan per bank at a time.
    /// </summary>
    public sealed class TakeLoanCommand : ICommand
    {
        public readonly long Amount;

        public TakeLoanCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<LoanTable>().Find(nation) >= 0) return CommandResult.Fail("This bank's loan is not repaid yet.");
            if (ctx.World.Get<FinanceState>().EventOf(nation) == FinancialEventKind.CreditCrunch)
                return CommandResult.Fail("A credit crunch: the bank makes no new loans.");
            if (ctx.World.Get<BankState>().Reserve[nation] < Amount) return CommandResult.Fail("The bank has no coins left to lend.");
            var sheet = new PlayerBalanceSheet(ctx.World, ctx.Services.Get<GameDatabase>());
            return sheet.CreditLimit(ctx.World.Get<PlayerState>().LocalContinent) >= Amount
                ? CommandResult.Success
                : CommandResult.Fail("Not enough collateral.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var config = ctx.Services.Get<GameDatabase>().Config;
            var rate = Fx.FromRaw(ctx.World.Get<FinanceState>().LoanRate[nation]);
            long interest = FinanceRules.Interest(rate, Amount, config.LoanTermWeeks);
            long balance = Amount + interest;
            long weekly = (balance + config.LoanTermWeeks - 1) / config.LoanTermWeeks;
            ctx.World.Get<BankState>().Reserve[nation] -= Amount;
            ctx.World.Get<PlayerState>().LocalCoins += Amount;
            ctx.World.Get<LoanTable>().Add(nation, balance, weekly);
            ctx.Events.Publish(new LoanTaken(new NodeId(ctx.World.Get<NodeBanks>().NationNode(nation)), Amount, balance, weekly));
        }
    }

    /// <summary>Pays off part or all of the loan of the bank whose branch the player is at, from local coins.</summary>
    public sealed class RepayLoanCommand : ICommand
    {
        public readonly long Amount;

        public RepayLoanCommand(long amount) => Amount = amount;

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            int nation = PlayerBank.Branch(ctx, out var error);
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<LoanTable>().Find(nation) < 0) return CommandResult.Fail("No loan with this bank.");
            return ctx.World.Get<PlayerState>().LocalCoins >= Amount ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = PlayerBank.Branch(ctx, out _);
            var loans = ctx.World.Get<LoanTable>();
            int r = loans.Find(nation);
            long paid = Math.Min(Amount, loans.Balance[r]);
            ctx.World.Get<PlayerState>().LocalCoins -= paid;
            loans.Balance[r] -= paid;
            loans.Due[r] = Math.Max(0, loans.Due[r] - paid);
            if (loans.Due[r] == 0) loans.OverdueWeeks[r] = 0;
            ctx.World.Get<BankState>().Reserve[nation] += paid;
            if (loans.Balance[r] > 0) return;
            loans.RemoveRow(r);
            ctx.Events.Publish(new LoanRepaid(new NodeId(ctx.World.Get<NodeBanks>().NationNode(nation)), ctx.Turn));
        }
    }

    /// <summary>
    /// Bids for a seized facility at auction (DEC-039). The bid is paid when the auction closes, from the
    /// coins of the facility's continent and then deposits there; a bid that cannot be paid then loses.
    /// </summary>
    public sealed class BidCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly long Amount;

        public BidCommand(FacilityId facility, long amount)
        {
            Facility = facility;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var auctions = ctx.World.Get<AuctionTable>();
            int a = auctions.Find(Facility);
            if (a < 0) return CommandResult.Fail("That facility is not up for auction.");
            if (Amount < auctions.ReservePrice[a]) return CommandResult.Fail("The bid is below the reserve price.");
            return Amount > auctions.PlayerBid[a] ? CommandResult.Success : CommandResult.Fail("The bid must beat the current one.");
        }

        public void Apply(SimContext ctx)
        {
            var auctions = ctx.World.Get<AuctionTable>();
            auctions.PlayerBid[auctions.Find(Facility)] = Amount;
        }
    }
}
