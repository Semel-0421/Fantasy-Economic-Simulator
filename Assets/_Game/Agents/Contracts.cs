using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class SupplyContractSigned : ISimEvent
    {
        public readonly int Seller, Buyer, Quantity;
        public readonly long UnitPrice;

        public SupplyContractSigned(int seller, int buyer, int quantity, long unitPrice)
        {
            Seller = seller;
            Buyer = buyer;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }
    }

    public sealed class SupplyContractBroken : ISimEvent
    {
        public readonly int Seller, Buyer;

        public SupplyContractBroken(int seller, int buyer)
        {
            Seller = seller;
            Buyer = buyer;
        }
    }

    /// <summary>A contract party: a group's row, or a merchant's as its complement.</summary>
    public static class ContractParty
    {
        public static int OfGroup(int group) => group;
        public static int OfMerchant(int agent) => ~agent;
        public static bool IsMerchant(int party) => party < 0;
    }

    /// <summary>Supply contracts in force (DEC-087). Sellers and buyers are groups or merchants, see <see cref="ContractParty"/>.</summary>
    public sealed class ContractTable : NativeTable
    {
        public readonly NativeColumn<int> Seller;
        public readonly NativeColumn<int> Buyer;
        public readonly NativeColumn<int> FromCell;
        public readonly NativeColumn<int> ToCell;
        public readonly NativeColumn<int> FromNode;
        public readonly NativeColumn<int> ToNode;
        public readonly NativeColumn<int> Quantity;
        public readonly NativeColumn<long> UnitPrice;
        public readonly NativeColumn<int> EndTurn;
        /// <summary>What is left of the deposit the buyer paid the seller at signing; deliveries draw it down.</summary>
        public readonly NativeColumn<long> Prepaid;

        public override string Key => "agents.contracts";
        public override int Version => 2;

        public ContractTable()
        {
            Seller = Column<int>();
            Buyer = Column<int>();
            FromCell = Column<int>();
            ToCell = Column<int>();
            FromNode = Column<int>();
            ToNode = Column<int>();
            Quantity = Column<int>();
            UnitPrice = Column<long>();
            EndTurn = Column<int>();
            Prepaid = Column<long>();
        }

        public void Add(int seller, int buyer, in ContractRoute route, int quantity, long unitPrice, int endTurn, long prepaid = 0)
        {
            Seller.Add(seller);
            Buyer.Add(buyer);
            FromCell.Add(route.FromCell);
            ToCell.Add(route.ToCell);
            FromNode.Add(route.FromNode);
            ToNode.Add(route.ToNode);
            Quantity.Add(quantity);
            UnitPrice.Add(unitPrice);
            EndTurn.Add(endTurn);
            Prepaid.Add(prepaid);
        }

        internal void RemoveRow(int row) => RemoveRowSwapBack(row);
    }

    /// <summary>The market cells a contract's goods leave and reach, and their nodes.</summary>
    public readonly struct ContractRoute
    {
        public readonly int FromCell, ToCell, FromNode, ToNode;

        public ContractRoute(int fromCell, int toCell, int fromNode, int toNode)
        {
            FromCell = fromCell;
            ToCell = toCell;
            FromNode = fromNode;
            ToNode = toNode;
        }
    }

    /// <summary>
    /// Supply contracts (DEC-087). Each year in each nation, the widest gaps between a market with stock and the settled
    /// price of one short of a good are signed, person to person, group to person or group to group: the volume is part
    /// of what the buyer's market takes, and the larger it is, the further the price is below the settled price, but
    /// above the seller's. A buyer must hold some weeks of the bill to sign. Every week the seller buys the units
    /// from its market, the buyer pays the contract price and sells them into its own market. The buyer must pay for
    /// what the seller can deliver for the whole term, whatever the market does; one that cannot pay breaks the
    /// contract. Payments pass between parties and traders' floats, so no coins appear. Off unless
    /// contract.per_nation is set.
    /// </summary>
    public sealed class ContractSystem : ISimSystem
    {
        GameDatabase _db;
        ContractTable _contracts;
        MarketStore _markets;
        PurseState _purses;
        GroupTable _groups;
        AgentTable _agents;
        FacilityTable _facilities;
        NodeBanks _banks;
        NodeTable _nodes;
        HouseholdState _population;
        CurrencyState _currency;
        DemandForecast _forecast;
        DemandParams _demand;
        readonly System.Collections.Generic.HashSet<long> _refused = new System.Collections.Generic.HashSet<long>();

        public string Id => "agents.contracts";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Contracts;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _contracts = ctx.World.Get<ContractTable>();
            _markets = ctx.World.Get<MarketStore>();
            _purses = ctx.World.Get<PurseState>();
            _groups = ctx.World.Get<GroupTable>();
            _agents = ctx.World.Get<AgentTable>();
            _facilities = ctx.World.Get<FacilityTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _nodes = ctx.World.Get<NodeTable>();
            _population = ctx.World.Get<HouseholdState>();
            _currency = ctx.World.Get<CurrencyState>();
            _forecast = ctx.World.Get<DemandForecast>();
            _demand = DemandParams.From(_db.Config);
        }

        public void Execute(SimContext ctx)
        {
            if (_db.Config.ContractsPerNation <= 0) return;
            Deliver(ctx);
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;
            _refused.Clear();
            for (int n = 0; n < _banks.NationCount; n++)
                for (int k = 0; k < _db.Config.ContractsPerNation; k++)
                    if (!Sign(ctx, n)) break;
        }

        void Deliver(SimContext ctx)
        {
            var config = _db.Config;
            for (int r = _contracts.Count - 1; r >= 0; r--)
            {
                if (ctx.Turn >= _contracts.EndTurn[r])
                {
                    // What the seller did not deliver goes back, as far as it holds. A buyer who broke off forfeits instead.
                    long back = Math.Min(_contracts.Prepaid[r], Math.Max(0, Cash(_contracts.Seller[r])));
                    Pay(_contracts.Seller[r], -back);
                    Pay(_contracts.Buyer[r], back);
                    _contracts.RemoveRow(r);
                    continue;
                }
                int from = _contracts.FromCell[r], to = _contracts.ToCell[r], seller = _contracts.Seller[r], buyer = _contracts.Buyer[r];
                long unit = _contracts.UnitPrice[r], buy = _markets.Price[from], sell = _markets.Price[to];
                long units = Math.Min(_contracts.Quantity[r], _markets.Stock[from]);
                if (buy > 0) units = Math.Min(units, Math.Max(0, Cash(seller)) / buy);
                if (units <= 0) continue;
                long fromDeposit = Math.Min(_contracts.Prepaid[r], config.ContractDepositShare.MulInt(units * unit));
                if (Cash(buyer) + units * sell < units * unit - fromDeposit)
                {
                    ctx.Events.Publish(new SupplyContractBroken(seller, buyer));
                    _contracts.RemoveRow(r);
                    continue;
                }
                // What the buyer's traders cannot pay for stays with the seller's market.
                long floatThere = Math.Max(0, _purses.Float[_purses.NodeFloat[_contracts.ToNode[r]]]);
                if (sell > 0) units = Math.Min(units, floatThere / sell);
                if (units <= 0) continue;
                fromDeposit = Math.Min(_contracts.Prepaid[r], config.ContractDepositShare.MulInt(units * unit));
                _contracts.Prepaid[r] -= fromDeposit;
                _markets.Stock[from] -= (int)units;
                _markets.Supply[to] = MarketStore.ClampToNonNegativeInt(_markets.Supply[to] + units);
                _purses.ChangeFloat(_contracts.FromNode[r], units * buy);
                _purses.ChangeFloat(_contracts.ToNode[r], -units * sell);
                Pay(seller, units * (unit - buy) - fromDeposit);
                Pay(buyer, units * (sell - unit) + fromDeposit);
            }
        }

        long Cash(int party) => ContractParty.IsMerchant(party) ? _agents.Cash[~party] : _groups.Cash[party];

        void Pay(int party, long amount)
        {
            if (ContractParty.IsMerchant(party)) _agents.Cash[~party] += amount;
            else _groups.Cash[party] += amount;
        }

        // What a market takes a week: its households at the settled price, or what left it lately if more.
        long WeeklyDemand(int node, int cell)
        {
            long households = HouseholdDemand.AtAnchor(_demand, _db.Config, _population, node, _nodes.Level[node], _db.Goods[_markets.CellGood[cell]], IndexOf(node));
            return Math.Max(households, _forecast.Forecast[cell] / OrderSystem.QuarterWeeks);
        }

        long IndexOf(int node) => _currency.PriceIndex[_nodes.Continent[node]].Raw;

        // A nation's markets share a continent, so no market there settles above this anchor.
        long AnchorOf(int node, int cell) => Fx.RoundShift(_markets.GoodBasePrice[_markets.CellGood[cell]] * IndexOf(node));

        // The price a market settles at: its anchor, or less while it sells below it. A spike from a passing shortage does not count.
        long SettledPrice(int node, int cell) =>
            Math.Min(_markets.Price[cell], Fx.RoundShift(_markets.GoodBasePrice[_markets.CellGood[cell]] * IndexOf(node)));

        // The widest gap in the nation not yet under contract, among goods with stock on one side.
        bool Sign(SimContext ctx, int nation)
        {
            if (!WidestGap(nation, out var route)) return false;
            var traded = new GoodId(_markets.CellGood[route.FromCell]);
            int seller = SellerAt(route.FromNode, traded), buyer = BuyerAt(route.ToNode, ctx.Turn / SimClock.WeeksPerYear + route.FromCell);
            bool parties = seller != int.MinValue && buyer != int.MinValue && seller != buyer;
            if (!parties || !AgreeTerms(route, buyer, out int quantity, out long unit, out int term))
            {
                _refused.Add(RefuseKey(route.FromCell, route.ToCell));
                return true;
            }
            long deposit = _db.Config.ContractDepositShare.MulInt(quantity * unit * term);
            Pay(buyer, -deposit);
            Pay(seller, deposit);
            _contracts.Add(seller, buyer, route, quantity, unit, ctx.Turn + term, deposit);
            ctx.Events.Publish(new SupplyContractSigned(seller, buyer, quantity, unit));
            return true;
        }

        static long RefuseKey(int fromCell, int toCell) => ((long)fromCell << 32) | (uint)toCell;

        bool WidestGap(int nation, out ContractRoute route)
        {
            var nodes = _purses.NationNodes[nation];
            route = default;
            long bestGap = 0;
            foreach (int a in nodes)
            {
                int end = _markets.MarketCellStart[a] + _markets.MarketCellCount[a];
                for (int c = _markets.MarketCellStart[a]; c < end; c++)
                    if (_markets.Stock[c] >= _db.Config.ContractMinStock && AnchorOf(a, c) - _markets.Price[c] > bestGap)
                        WidestGapFrom(a, c, nodes, ref bestGap, ref route);
            }
            return bestGap > 0;
        }

        void WidestGapFrom(int a, int c, int[] nodes, ref long bestGap, ref ContractRoute route)
        {
            var good = new GoodId(_markets.CellGood[c]);
            foreach (int b in nodes)
            {
                if (a == b || !_markets.TryFindCell(b, good, out int d)) continue;
                long gap = SettledPrice(b, d) - _markets.Price[c];
                if (gap <= bestGap || _markets.Stock[d] >= _db.Config.ContractMinStock || Contracted(c, d)) continue;
                if (_refused.Contains(RefuseKey(c, d)) || WeeklyDemand(b, d) < 2) continue;
                bestGap = gap;
                route = new ContractRoute(c, d, a, b);
            }
        }

        // The term fits both sides: as long as the seller's stock lasts and the buyer's money pays for it. False when no terms suit them.
        bool AgreeTerms(in ContractRoute route, int buyer, out int quantity, out long unit, out int term)
        {
            var config = _db.Config;
            int stock = _markets.Stock[route.FromCell];
            quantity = (int)Math.Min(stock / config.ContractStockWeeks, config.ContractDemandShare.MulInt(WeeklyDemand(route.ToNode, route.ToCell)));
            quantity = Math.Max(1, quantity);
            var discount = Fx.Min(config.ContractMaxDiscount, config.ContractBaseDiscount + config.ContractDiscountPerUnit * Fx.FromInt(quantity));
            long sell = SettledPrice(route.ToNode, route.ToCell);
            unit = Math.Max(_markets.Price[route.FromCell] + 1L, sell - discount.MulInt(sell));
            long weekly = quantity * unit;
            long afford = Math.Max(0, Cash(buyer)) / Math.Max(1, config.ContractDepositShare.MulInt(weekly));
            term = (int)Math.Min(Math.Min(stock / quantity, afford), config.ContractMaxWeeks);
            return unit < sell && term >= config.ContractMinWeeks && Cash(buyer) >= weekly * config.ContractCreditWeeks;
        }

        bool Contracted(int from, int to)
        {
            for (int r = 0; r < _contracts.Count; r++)
                if (_contracts.FromCell[r] == from && _contracts.ToCell[r] == to) return true;
            return false;
        }

        // The group making the good there, else the richest merchant based there.
        int SellerAt(int node, GoodId good)
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Node[f] != node || _facilities.Owner[f] != OwnerIds.Npc || _facilities.Group[f] < 0) continue;
                foreach (var line in _db.Facilities[_facilities.DefIndex[f]].Recipe.Outputs)
                    if (line.Good.Value == good.Value) return ContractParty.OfGroup(_facilities.Group[f]);
            }
            return RichestMerchant(node);
        }

        // By turns a merchant based there or the group with the most cash that owns something there.
        int BuyerAt(int node, int pick)
        {
            int merchant = RichestMerchant(node), best = -1;
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Node[f] != node || _facilities.Owner[f] != OwnerIds.Npc || _facilities.Group[f] < 0) continue;
                int g = _facilities.Group[f];
                if (best < 0 || _groups.Cash[g] > _groups.Cash[best]) best = g;
            }
            if (best < 0) return merchant;
            return merchant != int.MinValue && pick % 2 == 0 ? merchant : ContractParty.OfGroup(best);
        }

        int RichestMerchant(int node)
        {
            int best = -1;
            for (int a = 0; a < _agents.Count; a++)
                if (_agents.Home[a] == node && _agents.Owner[a] == OwnerIds.Npc && (best < 0 || _agents.Cash[a] > _agents.Cash[best])) best = a;
            return best < 0 ? int.MinValue : ContractParty.OfMerchant(best);
        }
    }

    /// <summary>Version 1 had no deposits.</summary>
    public sealed class ContractTableV1Migration : ISaveMigration
    {
        public string SectionKey => "agents.contracts";
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            int rows = new System.IO.BinaryReader(new System.IO.MemoryStream(section)).ReadInt32();
            using var output = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(0L);
            }
            return output.ToArray();
        }
    }

    /// <summary>
    /// Once a year each nation grants a share of its reserve above target to the groups running its strategic
    /// producers, by their facility levels, to grow those industries (DEC-087). Off unless state.strategic_subsidy_permille is set.
    /// </summary>
    public sealed class StrategicSubsidySystem : ISimSystem
    {
        GameDatabase _db;
        FacilityTable _facilities;
        GroupTable _groups;
        BankState _bank;
        NodeBanks _banks;
        PurseState _purses;
        WorldState _world;
        long[] _levels;

        public string Id => "agents.strategic_subsidy";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.StrategicSubsidy;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _facilities = ctx.World.Get<FacilityTable>();
            _groups = ctx.World.Get<GroupTable>();
            _bank = ctx.World.Get<BankState>();
            _banks = ctx.World.Get<NodeBanks>();
            _purses = ctx.World.Get<PurseState>();
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0 || _db.Config.StrategicSubsidyShare.Raw <= 0) return;
            if (_levels == null || _levels.Length != _groups.Count) _levels = new long[_groups.Count];
            for (int n = 0; n < _banks.NationCount; n++) Grant(n);
        }

        void Grant(int nation)
        {
            long total = CountStrategicLevels(nation);
            if (total <= 0) return;
            long budget = _db.Config.StrategicSubsidyShare.MulInt(Math.Max(0, MoneyLedger.StateExcess(_world, _db.Config, nation)));
            long paid = 0;
            for (int g = 0; g < _levels.Length; g++)
            {
                long grant = budget / total * _levels[g] + budget % total * _levels[g] / total;
                _groups.Cash[g] += grant;
                paid += grant;
            }
            _bank.Reserve[nation] -= paid;
        }

        long CountStrategicLevels(int nation)
        {
            Array.Clear(_levels, 0, _levels.Length);
            long total = 0;
            for (int f = 0; f < _facilities.Count; f++)
            {
                int g = _facilities.Group[f];
                if (g < 0 || _facilities.Owner[f] != OwnerIds.Npc || _purses.NodeFloat[_facilities.Node[f]] != nation) continue;
                if (!_db.Facilities[_facilities.DefIndex[f]].Strategic) continue;
                _levels[g] += _facilities.Level[f];
                total += _facilities.Level[f];
            }
            return total;
        }
    }
}
