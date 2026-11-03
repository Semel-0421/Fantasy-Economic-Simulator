using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// DEC-132: trading ventures, companies with no facility. Each is based at a port with a sea route to another continent;
    /// it raises its capital from the households of the port's nation, who hold its shares with the player, buys a cargo at
    /// the far port, sails for some weeks and sells it at home, and pays its profit out by share.
    /// </summary>
    public sealed class VentureTable : NativeTable
    {
        public readonly NativeColumn<int> Continent;
        public readonly NativeColumn<int> Port;
        /// <summary>The far port it buys at.</summary>
        public readonly NativeColumn<int> Far;
        public readonly NativeColumn<long> Cash;
        /// <summary>What the cargo at sea or still unsold cost, in the home coin; zero in port.</summary>
        public readonly NativeColumn<long> Stake;
        /// <summary>What the cargo has sold for so far.</summary>
        public readonly NativeColumn<long> Proceeds;
        /// <summary>The turn the voyage comes home, or -1 in port.</summary>
        public readonly NativeColumn<int> SailUntil;
        public readonly NativeColumn<int> Good;
        public readonly NativeColumn<int> Units;
        public readonly NativeColumn<byte> Lost;
        public readonly NativeColumn<int> PlayerShares;
        /// <summary>The profit a year that holders expect, from the voyages so far.</summary>
        public readonly NativeColumn<long> ProfitYear;
        public readonly NativeColumn<int> Voyages;
        public readonly NativeColumn<int> Wrecks;

        public override string Key => "agents.ventures";
        public override int Version => 1;

        public VentureTable()
        {
            Continent = Column<int>();
            Port = Column<int>();
            Far = Column<int>();
            Cash = Column<long>();
            Stake = Column<long>();
            Proceeds = Column<long>();
            SailUntil = Column<int>();
            Good = Column<int>();
            Units = Column<int>();
            Lost = Column<byte>();
            PlayerShares = Column<int>();
            ProfitYear = Column<long>();
            Voyages = Column<int>();
            Wrecks = Column<int>();
        }

        public void Add(int continent, int port, int far)
        {
            Continent.Add(continent);
            Port.Add(port);
            Far.Add(far);
            Cash.Add(0);
            Stake.Add(0);
            Proceeds.Add(0);
            SailUntil.Add(-1);
            Good.Add(-1);
            Units.Add(0);
            Lost.Add(0);
            PlayerShares.Add(0);
            ProfitYear.Add(0);
            Voyages.Add(0);
            Wrecks.Add(0);
        }

        public bool AtSea(int v, int turn) => SailUntil[v] >= 0 && turn < SailUntil[v];

        /// <summary>[Draft] Up to <see cref="SimulationConfig.VenturesPerContinent"/> on each continent, at its ports with a route to another continent, in order.</summary>
        public static void Found(VentureTable ventures, NodeTable nodes, RouteTable routes, NodeBanks banks, SimulationConfig config)
        {
            for (int c = 0; c < nodes.ContinentCount; c++)
            {
                int made = 0;
                for (int r = 0; r < routes.Count && made < config.VenturesPerContinent; r++)
                {
                    int from = routes.From[r], to = routes.To[r];
                    if (nodes.Continent[from] != c || nodes.Continent[to] == c || banks.NationOf(from) < 0) continue;
                    ventures.Add(c, from, to);
                    made++;
                }
            }
        }
    }

    /// <summary>DEC-132: what a venture share is worth.</summary>
    public static class VentureBook
    {
        /// <summary>[Draft] Its cash, the cargo at cost, and some years of the profit holders expect, over its shares.</summary>
        public static long Fundamental(VentureTable ventures, SimulationConfig config, int v) =>
            Math.Max(1, (ventures.Cash[v] + ventures.Stake[v] + config.VentureValueYears * Math.Max(0, ventures.ProfitYear[v])) / config.VentureShares);

        /// <summary>The market price: the fundamental times the premium of the continental exchange (DEC-131).</summary>
        public static long Price(WorldState world, SimulationConfig config, int v)
        {
            var ventures = world.Get<VentureTable>();
            return Math.Max(1, world.Get<AssetMarkets>().VenturePremium(ventures.Continent[v]).MulInt(Fundamental(ventures, config, v)));
        }

        public static int HouseholdShares(VentureTable ventures, SimulationConfig config, int v) => config.VentureShares - ventures.PlayerShares[v];

        /// <summary>The nation whose households hold the venture: its port's.</summary>
        public static int Nation(WorldState world, int v) => world.Get<NodeBanks>().NationOf(world.Get<VentureTable>().Port[v]);

        /// <summary>The bank's sale of the player's venture shares to the households, as far as they can pay (DEC-130). Returns the coins raised.</summary>
        public static long SellForPlayer(WorldState world, SimulationConfig config, int turn, int v, int count)
        {
            var ventures = world.Get<VentureTable>();
            count = Math.Min(count, ventures.PlayerShares[v]);
            long price = Price(world, config, v);
            int nation = Nation(world, v);
            long taken = BondDesk.TakeFromHouseholds(world, config, turn, nation, count * price);
            int sold = (int)(taken / price);
            if (taken > sold * price) MoneyLedger.PayNationHouseholds(world, nation, taken - sold * price);
            ventures.PlayerShares[v] -= sold;
            return sold * price;
        }
    }

    /// <summary>
    /// DEC-132: the week of each venture. Home from a voyage, it sells the cargo at its port as far as the traders there can
    /// pay and, once all is sold, pays the profit to its holders. In port, it raises its capital from its households and
    /// fits out a voyage: the far port's good dearest at home against its price there, bought with the stake.
    /// </summary>
    public sealed class VentureSystem : ISimSystem
    {
        static readonly uint WreckStream = DetHash.Of("agents.ventures.wreck");

        GameDatabase _db;
        WorldState _world;
        VentureTable _ventures;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        MarketStore _store;
        MarketFlows _flows;
        PurseState _purses;
        CurrencyState _currency;

        public string Id => "agents.ventures";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Ventures;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _world = ctx.World;
            _ventures = ctx.World.Get<VentureTable>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _store = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _purses = ctx.World.Get<PurseState>();
            _currency = ctx.World.Get<CurrencyState>();
        }

        public void Execute(SimContext ctx)
        {
            for (int v = 0; v < _ventures.Count; v++)
            {
                if (_ventures.AtSea(v, ctx.Turn)) continue;
                if (_ventures.SailUntil[v] >= 0) SellCargo(ctx, v);
                else FitOut(ctx, v);
            }
        }

        void SellCargo(SimContext ctx, int v)
        {
            if (_ventures.Lost[v] != 0) _ventures.Units[v] = 0;
            int port = _ventures.Port[v];
            if (_ventures.Units[v] > 0 && _flows.TryFindNearestMarket(port, new GoodId(_ventures.Good[v]), out _, out int cell))
            {
                int price = _db.Config.TraderOffer(_store.Price[cell]);
                int units = _purses.Sellable(port, _ventures.Units[v], price);
                if (units > 0)
                {
                    _flows.Sell(cell, units);
                    _purses.ChangeFloat(port, -(long)units * price);
                    _ventures.Cash[v] += (long)units * price;
                    _ventures.Proceeds[v] += (long)units * price;
                    _ventures.Units[v] -= units;
                }
            }
            if (_ventures.Units[v] > 0) return;
            Close(ctx, v);
        }

        // The voyage is over: holders expect the next to earn as the last ones did, and get this one's profit.
        void Close(SimContext ctx, int v)
        {
            var config = _db.Config;
            long profit = _ventures.Proceeds[v] - _ventures.Stake[v];
            long yearly = profit * SimClock.WeeksPerYear / Math.Max(1, config.VentureVoyageWeeks);
            _ventures.ProfitYear[v] = _ventures.Voyages[v] == 0 ? yearly : (_ventures.ProfitYear[v] + yearly) / 2;
            _ventures.Voyages[v]++;
            if (_ventures.Lost[v] != 0) _ventures.Wrecks[v]++;
            _ventures.Stake[v] = _ventures.Proceeds[v] = 0;
            _ventures.SailUntil[v] = -1;
            _ventures.Good[v] = -1;
            _ventures.Lost[v] = 0;
            if (profit > 0) PayOut(v, Math.Min(profit, _ventures.Cash[v]));
        }

        void PayOut(int v, long amount)
        {
            var config = _db.Config;
            int nation = VentureBook.Nation(_world, v);
            long toPlayer = amount * _ventures.PlayerShares[v] / config.VentureShares;
            long toHouseholds = amount * VentureBook.HouseholdShares(_ventures, config, v) / config.VentureShares;
            _ventures.Cash[v] -= toPlayer + toHouseholds;
            MoneyLedger.PayNationHouseholds(_world, nation, toHouseholds);
            _bank.PlayerDeposit[nation] += toPlayer;
            _bank.Reserve[nation] += toPlayer;
        }

        void FitOut(SimContext ctx, int v)
        {
            var config = _db.Config;
            int nation = VentureBook.Nation(_world, v);
            if (_ventures.Cash[v] < config.VentureCapital)
                _ventures.Cash[v] += BondDesk.TakeFromHouseholds(_world, config, ctx.Turn, nation, config.VentureCapital - _ventures.Cash[v]);
            if (_ventures.Cash[v] < config.VentureCapital / 2) return;
            int home = _ventures.Continent[v], far = _ventures.Far[v];
            int farContinent = _nodes.Continent[far];
            int market = far;
            while (market >= 0 && _store.MarketCellCount[market] == 0) market = _nodes.Parent[market];
            if (market < 0 || !Best(_ventures.Port[v], market, home, farContinent, out int cell)) return;
            long budget = _currency.Convert(_ventures.Cash[v], home, farContinent);
            int units = (int)Math.Min(_store.Stock[cell], budget / Math.Max(1, _store.Price[cell]));
            if (units <= 0) return;
            long cost = (long)units * _store.Price[cell];
            long stake = Math.Min(_ventures.Cash[v], Math.Max(1, _currency.Convert(cost, farContinent, home)));
            _ventures.Cash[v] -= stake;
            MoneyLedger.AcrossContinents(_bank, home, stake, farContinent, cost);
            _purses.ChangeFloat(far, cost);
            _flows.Buy(cell, units);
            _ventures.Stake[v] = stake;
            _ventures.Good[v] = _store.CellGood[cell];
            _ventures.Units[v] = units;
            _ventures.SailUntil[v] = ctx.Turn + config.VentureVoyageWeeks;
            _ventures.Lost[v] = (byte)(DetRandom.For(ctx.Seed, ctx.Turn, WreckStream, v).ChancePermille(config.VentureLossPermille) ? 1 : 0);
        }

        // The far market's good in stock that sells at home for the most over its price there, by at least the margin.
        bool Best(int port, int market, int home, int farContinent, out int best)
        {
            best = -1;
            var config = _db.Config;
            var need = Fx.One + config.VentureMinMargin;
            var bestRatio = Fx.Zero;
            int start = _store.MarketCellStart[market];
            for (int cell = start; cell < start + _store.MarketCellCount[market]; cell++)
            {
                if (_store.Stock[cell] <= 0 || _store.Price[cell] <= 0) continue;
                if (!_flows.TryFindNearestMarket(port, new GoodId(_store.CellGood[cell]), out _, out int homeCell)) continue;
                long there = _currency.Convert(config.TraderOffer(_store.Price[homeCell]), home, farContinent);
                var ratio = Fx.FromRatio(there, _store.Price[cell]);
                if (ratio < need || ratio <= bestRatio) continue;
                bestRatio = ratio;
                best = cell;
            }
            return best >= 0;
        }
    }

    /// <summary>Venture shares trade at the continental exchange: the international bank, else the continent's seat (DEC-132).</summary>
    static class ContinentalExchange
    {
        public static string Check(SimContext ctx, int venture)
        {
            var ventures = ctx.World.Get<VentureTable>();
            if (venture < 0 || venture >= ventures.Count) return "Unknown venture.";
            var player = ctx.World.Get<PlayerState>();
            int exchange = ctx.World.Get<AssetMarkets>().ExchangeNode(ctx.World.Get<NodeBanks>(), ventures.Continent[venture]);
            return !player.IsTravelling && exchange >= 0 && player.Node == exchange ? null : "Venture shares trade at the continent's exchange.";
        }
    }

    /// <summary>The player buys venture shares from its households at the price (DEC-132).</summary>
    public sealed class BuyVentureShareCommand : ICommand
    {
        public readonly int Venture;
        public readonly int Count;

        public BuyVentureShareCommand(int venture, int count)
        {
            Venture = venture;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = ContinentalExchange.Check(ctx, Venture);
            if (error != null) return CommandResult.Fail(error);
            var config = ctx.Services.Get<GameDatabase>().Config;
            if (VentureBook.HouseholdShares(ctx.World.Get<VentureTable>(), config, Venture) < Count) return CommandResult.Fail("Not so many shares are for sale.");
            var player = ctx.World.Get<PlayerState>();
            return player.CoinsAt(player.Node) >= Count * VentureBook.Price(ctx.World, config, Venture) ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var config = ctx.Services.Get<GameDatabase>().Config;
            long cost = Count * VentureBook.Price(ctx.World, config, Venture);
            var player = ctx.World.Get<PlayerState>();
            player.CoinsAt(player.Node) -= cost;
            MoneyLedger.PayNationHouseholds(ctx.World, VentureBook.Nation(ctx.World, Venture), cost);
            ctx.World.Get<VentureTable>().PlayerShares[Venture] += Count;
            ShortRules.Traded(ctx.World, true, Venture, ctx.Turn);
        }
    }

    /// <summary>The player sells venture shares back to its households, as far as their idle money goes (DEC-132).</summary>
    public sealed class SellVentureShareCommand : ICommand
    {
        public readonly int Venture;
        public readonly int Count;

        public SellVentureShareCommand(int venture, int count)
        {
            Venture = venture;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = ContinentalExchange.Check(ctx, Venture);
            if (error != null) return CommandResult.Fail(error);
            return ctx.World.Get<VentureTable>().PlayerShares[Venture] >= Count ? CommandResult.Success : CommandResult.Fail("The player holds fewer shares.");
        }

        public void Apply(SimContext ctx)
        {
            var config = ctx.Services.Get<GameDatabase>().Config;
            var player = ctx.World.Get<PlayerState>();
            player.CoinsAt(player.Node) += VentureBook.SellForPlayer(ctx.World, config, ctx.Turn, Venture, Count);
            ShortRules.Traded(ctx.World, true, Venture, ctx.Turn);
        }
    }
}
