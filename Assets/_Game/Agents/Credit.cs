using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>Who holds a bill: a merchant group as it stands, or one of these codes.</summary>
    public static class BillHolders
    {
        public const int Bank = -1;
        public const int Households = -2;
        const int FacilityBase = -3;

        public static int Facility(int facility) => FacilityBase - facility;
        public static bool IsFacility(int holder, out int facility)
        {
            facility = FacilityBase - holder;
            return holder <= FacilityBase;
        }
    }

    /// <summary>
    /// Bills of exchange (DEC-097): what a nation's traders owe for goods they took without the coins to pay. Goods reach
    /// the market at once; the bill falls due later, and the bank discounts it for the seller as far as it has room.
    /// </summary>
    public sealed class BillTable : NativeTable
    {
        public readonly NativeColumn<int> Holder;
        /// <summary>The float that owes it.</summary>
        public readonly NativeColumn<int> Purse;
        public readonly NativeColumn<long> Face;
        public readonly NativeColumn<int> DueTurn;

        public override string Key => "agents.bills";
        public override int Version => 1;

        public BillTable()
        {
            Holder = Column<int>();
            Purse = Column<int>();
            Face = Column<long>();
            DueTurn = Column<int>();
        }

        public void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>What this week's sellers may still be given in bills, and what the bank may still discount; set each week by <see cref="CreditSystem"/>.</summary>
    public sealed class BillRoom : IPersistentComponent, IDisposable
    {
        /// <summary>Per float: bills it may still give.</summary>
        public NativeArray<long> Room;
        /// <summary>Per float: coins its bank may still pay out on bills; floats outside any nation have no bank.</summary>
        public NativeArray<long> BankRoom;
        /// <summary>Per float: coins its bank paid out on bills since they last came off its reserve.</summary>
        public NativeArray<long> BankSpent;

        public BillRoom(int floats)
        {
            Room = new NativeArray<long>(floats, Allocator.Persistent);
            BankRoom = new NativeArray<long>(floats, Allocator.Persistent);
            BankSpent = new NativeArray<long>(floats, Allocator.Persistent);
        }

        public string Key => "agents.bill_room";
        public int Version => 1;

        public void Write(BinaryWriter writer)
        {
            NativeIO.WriteArray(writer, Room);
            NativeIO.WriteArray(writer, BankRoom);
            NativeIO.WriteArray(writer, BankSpent);
        }

        public void Read(BinaryReader reader)
        {
            NativeIO.ReadArray(reader, Room);
            NativeIO.ReadArray(reader, BankRoom);
            NativeIO.ReadArray(reader, BankSpent);
        }

        public void SettleBank(BankState bank)
        {
            for (int n = 0; n < bank.Reserve.Length && n < BankSpent.Length; n++)
            {
                bank.Reserve[n] -= BankSpent[n];
                BankSpent[n] = 0;
            }
        }

        public void Dispose()
        {
            Room.DisposeIfCreated();
            BankRoom.DisposeIfCreated();
            BankSpent.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Government and corporate bonds (DEC-097), one row per issuer and holder. Each week the issuer pays the simple interest
    /// and a part of the face, as far as it can; a company too long behind defaults and its holders lose what is left.
    /// </summary>
    public sealed class BondTable : NativeTable
    {
        /// <summary>A group, or <see cref="OfState"/> of a nation.</summary>
        public readonly NativeColumn<int> Issuer;
        /// <summary>The households of a node, <see cref="OfGroup"/> of a group, or <see cref="PlayerHolder"/>.</summary>
        public readonly NativeColumn<int> Holder;
        public readonly NativeColumn<long> Face;
        public readonly NativeColumn<long> Interest;
        /// <summary>Consecutive weeks the issuer did not pay all that fell due.</summary>
        public readonly NativeColumn<int> BehindWeeks;

        readonly Dictionary<long, int> _rowOf = new Dictionary<long, int>();
        bool _indexed;

        public override string Key => "agents.bonds";
        public override int Version => 1;

        public static int OfState(int nation) => ~nation;
        public static bool IsState(int issuer) => issuer < 0;
        public static int OfGroup(int group) => ~group;
        public static bool IsGroup(int holder) => holder < 0;
        /// <summary>The player's holding (DEC-102); what it earns goes to their account with the issuer's bank.</summary>
        public const int PlayerHolder = int.MinValue;

        public long Held(int issuer, int holder)
        {
            BuildIndex();
            return _rowOf.TryGetValue(Pair(issuer, holder), out int row) ? Face[row] : 0;
        }

        public void Reduce(int issuer, int holder, long face)
        {
            BuildIndex();
            Face[_rowOf[Pair(issuer, holder)]] -= face;
        }

        public BondTable()
        {
            Issuer = Column<int>();
            Holder = Column<int>();
            Face = Column<long>();
            Interest = Column<long>();
            BehindWeeks = Column<int>();
        }

        protected override void OnRead() => _indexed = false;

        static long Pair(int issuer, int holder) => ((long)issuer << 32) ^ (uint)holder;

        void BuildIndex()
        {
            if (_indexed) return;
            _rowOf.Clear();
            for (int r = 0; r < Count; r++) _rowOf[Pair(Issuer[r], Holder[r])] = r;
            _indexed = true;
        }

        public void Add(int issuer, int holder, long face)
        {
            BuildIndex();
            if (_rowOf.TryGetValue(Pair(issuer, holder), out int row))
            {
                Face[row] += face;
                return;
            }
            Issuer.Add(issuer);
            Holder.Add(holder);
            Face.Add(face);
            Interest.Add(0);
            BehindWeeks.Add(0);
            _rowOf[Pair(issuer, holder)] = Count - 1;
        }

        public void RemoveRow(int row)
        {
            BuildIndex();
            _rowOf.Remove(Pair(Issuer[row], Holder[row]));
            int last = Count - 1;
            if (row != last) _rowOf[Pair(Issuer[last], Holder[last])] = row;
            RemoveRowSwapBack(row);
        }

        public long DebtOf(int issuer)
        {
            long debt = 0;
            for (int r = 0; r < Count; r++)
                if (Issuer[r] == issuer) debt += Face[r];
            return debt;
        }
    }

    /// <summary>What credit did, for reading balance.</summary>
    public sealed class CreditStats : IWorldComponent
    {
        public long BillsIssued, BillsDiscounted, BillsPaid, BillsLost, StateRaised, CompanyRaised, GroupsToppedUp, BondsPaid, BondsLost, Defaults;
    }

    /// <summary>What holders would still pay for bonds this week; made on first asking and forgotten when the week's credit is done.</summary>
    public sealed class BondOffers : IWorldComponent
    {
        public int Turn = -1;
        public long[] Houses = new long[0], Groups = new long[0];
    }

    public sealed class BondDefaulted : ISimEvent
    {
        public readonly int Group;
        public readonly int Turn;

        public BondDefaulted(int group, int turn)
        {
            Group = group;
            Turn = turn;
        }
    }

    /// <summary>
    /// Sells bonds to those holding idle money: a nation's households beyond their spending and its groups beyond their
    /// reserve, each offering a share of that a week. Coins only move between purses.
    /// </summary>
    public static class BondDesk
    {
        static BondOffers OffersOf(WorldState world, SimulationConfig config, int turn)
        {
            var offers = world.Get<BondOffers>();
            if (offers.Turn == turn) return offers;
            offers.Turn = turn;
            var cash = world.Get<HouseholdCash>();
            var groups = world.Get<GroupTable>();
            if (offers.Houses.Length != cash.Cash.Length) offers.Houses = new long[cash.Cash.Length];
            if (offers.Groups.Length < groups.Count) offers.Groups = new long[groups.Count + 64];
            for (int n = 0; n < offers.Houses.Length; n++)
                offers.Houses[n] = config.BondBuyShare.MulInt(Math.Max(0, cash.Cash[n] - config.BondKeepWeeks * cash.Desired[n]));
            for (int g = 0; g < groups.Count; g++)
            {
                long keep = config.BondGroupKeepTimes * config.GroupStartCash[groups.Kind[g]];
                offers.Groups[g] = groups.IsPlayer[g] != 0 ? 0 : config.BondBuyShare.MulInt(Math.Max(0, groups.Cash[g] - keep));
            }
            return offers;
        }

        /// <summary>What a nation's holders would pay for this week's bonds, apart from the issuer itself.</summary>
        public static long Offered(WorldState world, SimulationConfig config, int turn, int nation, int issuer)
        {
            var offers = OffersOf(world, config, turn);
            var purses = world.Get<PurseState>();
            var groups = world.Get<GroupTable>();
            long offered = 0;
            foreach (int node in purses.NationNodes[nation]) offered += offers.Houses[node];
            for (int g = 0; g < groups.Count; g++)
                if (g != issuer && purses.NodeFloat[groups.Node[g]] == nation) offered += offers.Groups[g];
            return offered;
        }

        /// <summary>Sells up to <paramref name="amount"/> of bonds, in proportion to what holders offer. Returns the coins raised, which the caller receives.</summary>
        public static long Raise(WorldState world, SimulationConfig config, int turn, int issuer, int nation, long amount)
        {
            long offered = Offered(world, config, turn, nation, BondTable.IsState(issuer) ? -1 : issuer);
            long want = Math.Min(amount, offered);
            if (want <= 0) return 0;
            var purses = world.Get<PurseState>();
            var groups = world.Get<GroupTable>();
            var cash = world.Get<HouseholdCash>();
            var bonds = world.Get<BondTable>();
            var offers = world.Get<BondOffers>();
            long raised = 0;
            foreach (int node in purses.NationNodes[nation])
            {
                long part = want / offered * offers.Houses[node] + want % offered * offers.Houses[node] / offered;
                if (part <= 0) continue;
                offers.Houses[node] -= part;
                cash.Cash[node] -= part;
                cash.SavedYear[node] += part;
                bonds.Add(issuer, node, part);
                raised += part;
            }
            for (int g = 0; g < groups.Count; g++)
            {
                if (g == issuer || purses.NodeFloat[groups.Node[g]] != nation) continue;
                long part = want / offered * offers.Groups[g] + want % offered * offers.Groups[g] / offered;
                if (part <= 0) continue;
                offers.Groups[g] -= part;
                groups.Cash[g] -= part;
                bonds.Add(issuer, BondTable.OfGroup(g), part);
                raised += part;
            }
            return raised;
        }

        /// <summary>How much more a nation may owe: a share of its part of the continent's coins.</summary>
        public static long StateRoom(WorldState world, SimulationConfig config, int nation)
        {
            var nodes = world.Get<NodeTable>();
            var banks = world.Get<NodeBanks>();
            int continent = nodes.Continent[banks.NationNode(nation)], nations = 0;
            for (int n = 0; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent) nations++;
            long limit = config.StateDebtShare.MulInt(world.Get<CurrencyState>().CoinSupply[continent] / Math.Max(1, nations));
            return Math.Max(0, limit - world.Get<BondTable>().DebtOf(BondTable.OfState(nation)));
        }

        /// <summary>How much more a group may owe: a share of the value of its facilities.</summary>
        public static long CompanyRoom(WorldState world, GameDatabase db, int group)
        {
            var facilities = world.Get<FacilityTable>();
            long value = 0;
            for (int f = 0; f < facilities.Count; f++)
                if (facilities.Group[f] == group && facilities.Owner[f] == OwnerIds.Npc) value += NpcLending.FacilityValue(db, facilities, f);
            return Math.Max(0, db.Config.CompanyDebtShare.MulInt(value) - world.Get<BondTable>().DebtOf(group));
        }
    }

    /// <summary>
    /// The week's credit (DEC-097): traders pay the bills due within a share of their float; issuers pay interest and part of
    /// their bonds; groups keep their facilities in working cash, from their own money and then from corporate bonds; and a
    /// nation whose towns cannot afford their necessities sells bonds to the idle money and pays the towns with it.
    /// </summary>
    public sealed class CreditSystem : ISimSystem
    {
        GameDatabase _db;
        WorldState _world;
        NodeTable _nodes;
        NodeBanks _banks;
        PurseState _purses;
        BankState _bank;
        CurrencyState _currency;
        HouseholdCash _cash;
        GroupTable _groups;
        FacilityTable _facilities;
        AgentTable _agents;
        MarketStore _markets;
        MarketFlows _flows;
        BillTable _bills;
        BillRoom _room;
        BondTable _bonds;
        CreditStats _stats;
        FinanceState _finance;
        bool[] _stateBehind;
        long[] _floatPot;
        int[] _nationsOnContinent;

        public string Id => "agents.credit";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Credit;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _world = ctx.World;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _purses = ctx.World.Get<PurseState>();
            _bank = ctx.World.Get<BankState>();
            _currency = ctx.World.Get<CurrencyState>();
            _cash = ctx.World.Get<HouseholdCash>();
            _groups = ctx.World.Get<GroupTable>();
            _facilities = ctx.World.Get<FacilityTable>();
            _agents = ctx.World.Get<AgentTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _bills = ctx.World.Get<BillTable>();
            _room = ctx.World.Get<BillRoom>();
            _bonds = ctx.World.Get<BondTable>();
            _stats = ctx.World.Get<CreditStats>();
            _finance = ctx.World.Get<FinanceState>();
            _stateBehind = new bool[_banks.NationCount];
            _floatPot = new long[_purses.Count];
            _nationsOnContinent = new int[_nodes.ContinentCount];
            for (int n = 0; n < _banks.NationCount; n++) _nationsOnContinent[_nodes.Continent[_banks.NationNode(n)]]++;
            RefreshRoom();
        }

        public void Execute(SimContext ctx)
        {
            PayBills(ctx);
            ServiceBonds(ctx);
            KeepFacilitiesInCash(ctx);
            SellStateBonds(ctx);
            RefreshRoom();
            _world.Get<BondOffers>().Turn = -1;
        }

        void PayBills(SimContext ctx)
        {
            var config = _db.Config;
            for (int p = 0; p < _floatPot.Length; p++)
                _floatPot[p] = config.BillRepayShare.MulInt(Math.Max(0, _purses.Float[p]));
            for (int r = _bills.Count - 1; r >= 0; r--)
            {
                if (_bills.DueTurn[r] > ctx.Turn) continue;
                int purse = _bills.Purse[r];
                long paid = Math.Min(_bills.Face[r], _floatPot[purse]);
                if (paid > 0)
                {
                    _floatPot[purse] -= paid;
                    _purses.Float[purse] -= paid;
                    PayHolder(_bills.Holder[r], purse, paid);
                    _bills.Face[r] -= paid;
                    _stats.BillsPaid += paid;
                }
                if (_bills.Face[r] <= 0) _bills.RemoveRow(r);
                else if (ctx.Turn - _bills.DueTurn[r] >= config.BillDefaultWeeks)
                {
                    // Protested: the holder is left with the loss, as the traders never paid.
                    _stats.BillsLost += _bills.Face[r];
                    _bills.RemoveRow(r);
                }
            }
        }

        void PayHolder(int holder, int purse, long coins)
        {
            if (holder >= 0) _agents.Cash[holder] += coins;
            else if (holder == BillHolders.Bank) _bank.Reserve[purse] += coins;
            else if (holder == BillHolders.Households) MoneyLedger.PayNationHouseholds(_world, purse, coins);
            else if (BillHolders.IsFacility(holder, out int facility)) _facilities.Cash[facility] += coins;
        }

        void ServiceBonds(SimContext ctx)
        {
            var config = _db.Config;
            Array.Clear(_stateBehind, 0, _stateBehind.Length);
            for (int r = _bonds.Count - 1; r >= 0; r--)
            {
                int issuer = _bonds.Issuer[r];
                bool state = BondTable.IsState(issuer);
                var rate = state ? config.StateBondInterest : config.CompanyBondInterest;
                int weeks = state ? config.StateBondWeeks : config.CompanyBondWeeks;
                _bonds.Interest[r] += rate.MulInt(_bonds.Face[r]) / SimClock.WeeksPerYear;
                long due = _bonds.Interest[r] + (_bonds.Face[r] <= 0 ? 0 : Math.Max(1, _bonds.Face[r] / weeks));
                long funds = state ? _bank.Spendable(~issuer) : Math.Max(0, _groups.Cash[issuer]);
                long paid = Math.Min(due, funds);
                if (state) _bank.Reserve[~issuer] -= paid;
                else _groups.Cash[issuer] -= paid;
                long toInterest = Math.Min(paid, _bonds.Interest[r]);
                _bonds.Interest[r] -= toInterest;
                long toFace = paid - toInterest;
                _bonds.Face[r] -= toFace;
                PayBondHolder(_bonds.Holder[r], issuer, paid, toFace);
                _stats.BondsPaid += paid;
                if (paid >= due) _bonds.BehindWeeks[r] = 0;
                else
                {
                    _bonds.BehindWeeks[r]++;
                    if (state) _stateBehind[~issuer] = true;
                }
                if (_bonds.Face[r] <= 0 && _bonds.Interest[r] <= 0) _bonds.RemoveRow(r);
            }
            for (int r = _bonds.Count - 1; r >= 0; r--)
            {
                if (BondTable.IsState(_bonds.Issuer[r]) || _bonds.BehindWeeks[r] < config.CompanyDefaultWeeks) continue;
                int group = _bonds.Issuer[r];
                for (int k = _bonds.Count - 1; k >= 0; k--)
                {
                    if (_bonds.Issuer[k] != group) continue;
                    _stats.BondsLost += _bonds.Face[k];
                    _bonds.RemoveRow(k);
                }
                _stats.Defaults++;
                ctx.Events.Publish(new BondDefaulted(group, ctx.Turn));
                r = _bonds.Count;
            }
        }

        void PayBondHolder(int holder, int issuer, long paid, long face)
        {
            if (holder == BondTable.PlayerHolder)
            {
                int nation = BondTable.IsState(issuer) ? ~issuer : _purses.NodeFloat[_groups.Node[issuer]];
                _bank.PlayerDeposit[nation] += paid;
                _bank.Reserve[nation] += paid;
                return;
            }
            if (BondTable.IsGroup(holder))
            {
                _groups.Cash[~holder] += paid;
                return;
            }
            _cash.Cash[holder] += paid;
            _cash.SavedYear[holder] -= face; // interest is income, the rest was theirs
        }

        // A group feeds a facility that cannot pay for its next weeks, if its goods are worth making: from its own money
        // above its reserve, then from bonds, as far as buyers and its debt limit allow.
        void KeepFacilitiesInCash(SimContext ctx)
        {
            var config = _db.Config;
            if (config.WorkingWeeks <= 0) return;
            var books = _world.Get<IncomeTaxState>();
            for (int f = 0; f < _facilities.Count; f++)
            {
                int group = _facilities.Group[f];
                if (group < 0 || _facilities.Owner[f] != OwnerIds.Npc || _groups.IsPlayer[group] != 0) continue;
                var def = _db.Facilities[_facilities.DefIndex[f]];
                long inputs = 0, outputs = 0;
                foreach (var line in def.Recipe.Inputs) inputs += line.Quantity * PriceOf(_facilities.Node[f], line.Good);
                foreach (var line in def.Recipe.Outputs) outputs += line.Quantity * PriceOf(_facilities.Node[f], line.Good);
                if (outputs <= inputs) continue;
                long gap = config.WorkingWeeks * (NpcLending.FacilityUpkeep(_db, _facilities, f) + inputs * _facilities.Level[f]) - _facilities.Cash[f];
                if (gap <= 0) continue;
                long own = Math.Min(gap, Math.Max(0, _groups.Cash[group] - config.GroupStartCash[_groups.Kind[group]]));
                if (own > 0)
                {
                    _groups.Cash[group] -= own;
                    _facilities.Cash[f] += own;
                    books.MoveFacilityCapital(f, own);
                    gap -= own;
                    _stats.GroupsToppedUp += own;
                }
                if (gap <= 0 || config.CompanyDebtShare.Raw <= 0) continue;
                int nation = _purses.NodeFloat[_groups.Node[group]];
                if (nation >= _purses.NationCount || _finance.EventOf(nation) == FinancialEventKind.CreditCrunch) continue;
                long raised = BondDesk.Raise(_world, config, ctx.Turn, group, nation, Math.Min(gap, BondDesk.CompanyRoom(_world, _db, group)));
                _facilities.Cash[f] += raised;
                books.MoveFacilityCapital(f, raised);
                _stats.CompanyRaised += raised;
            }
        }

        long PriceOf(int node, GoodId good) => _flows.TryFindNearestMarket(node, good, out _, out int cell) ? _markets.Price[cell] : 0;

        void SellStateBonds(SimContext ctx)
        {
            var config = _db.Config;
            if (config.StateDebtShare.Raw <= 0) return;
            for (int n = 0; n < _banks.NationCount; n++)
            {
                if (_stateBehind[n] || _finance.EventOf(n) == FinancialEventKind.CreditCrunch) continue;
                long room = BondDesk.StateRoom(_world, config, n);
                if (room <= 0) continue;
                long shortfall = 0;
                foreach (int node in _purses.NationNodes[n]) shortfall += Math.Max(0, MoneyLedger.TownShortfall(_nodes, _cash, config, node));
                if (shortfall <= 0) continue;
                long raised = BondDesk.Raise(_world, config, ctx.Turn, BondTable.OfState(n), n, Math.Min(shortfall, room));
                if (raised <= 0) continue;
                MoneyLedger.PayNationTownWages(_world, config, n, raised);
                _stats.StateRaised += raised;
            }
        }

        // Traders may owe weeks of what households wish to buy, and the bank may discount down to a floor under its target.
        void RefreshRoom()
        {
            var config = _db.Config;
            for (int p = 0; p < _room.Room.Length; p++)
            {
                _room.Room[p] = 0;
                _room.BankRoom[p] = 0;
            }
            if (config.BillCapWeeks <= 0) return;
            for (int n = 0; n < _banks.NationCount; n++)
            {
                long wishes = 0;
                foreach (int node in _purses.NationNodes[n]) wishes += _cash.Desired[node];
                _room.Room[n] = config.BillCapWeeks * wishes;
                if (_finance.EventOf(n) == FinancialEventKind.CreditCrunch) continue;
                int continent = _nodes.Continent[_banks.NationNode(n)];
                long target = config.StateReserveTarget.MulInt(config.BankInitialReserveShare.MulInt(_currency.CoinSupply[continent]) / Math.Max(1, _nationsOnContinent[continent]));
                _room.BankRoom[n] = Math.Max(0, _bank.Spendable(n) - config.BillBankFloor.MulInt(target));
            }
            for (int r = 0; r < _bills.Count; r++) _room.Room[_bills.Purse[r]] = Math.Max(0, _room.Room[_bills.Purse[r]] - _bills.Face[r]);
        }
    }
}
