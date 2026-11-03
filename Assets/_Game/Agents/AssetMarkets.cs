using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public enum AssetMarketKind : byte
    {
        Stock = 0,
        Property = 1,
    }

    public enum AssetMarketPhase : byte
    {
        Calm = 0,
        Heated = 1,
        Bust = 2,
    }

    /// <summary>DEC-131: a market's price rose past the heated line of its price over the fundamental.</summary>
    public sealed class MarketHeated : ISimEvent
    {
        public readonly AssetMarketKind Kind;
        /// <summary>The nation's node for a stock exchange; the node for a property market.</summary>
        public readonly NodeId Node;
        public readonly Fx Premium;
        public readonly int Turn;

        public MarketHeated(AssetMarketKind kind, NodeId node, Fx premium, int turn)
        {
            Kind = kind;
            Node = node;
            Premium = premium;
            Turn = turn;
        }
    }

    /// <summary>DEC-131: a market burst on its published indicators; its nation's credit stops.</summary>
    public sealed class MarketBurst : ISimEvent
    {
        public readonly AssetMarketKind Kind;
        public readonly NodeId Node;
        /// <summary>The price over the fundamental before the fall.</summary>
        public readonly Fx Premium;
        public readonly int Turn;

        public MarketBurst(AssetMarketKind kind, NodeId node, Fx premium, int turn)
        {
            Kind = kind;
            Node = node;
            Premium = premium;
            Turn = turn;
        }
    }

    /// <summary>
    /// DEC-131: the asset markets, one stock exchange per nation and one property market per node whose households
    /// live there (a class's land trades at its core). Each has a price over its fundamental value (the premium), the
    /// households' margin debt on what they hold there, and the indicators published each quarter.
    /// </summary>
    public sealed class AssetMarkets : IPersistentComponent
    {
        public const int History = 4;

        public readonly int NationCount;
        readonly NodeTable _nodes;
        /// <summary>Per market, stock exchanges first: the price over the fundamental, as a raw <see cref="Fx"/>.</summary>
        public readonly long[] Premium;
        /// <summary>Per market: the last quarter's change of the premium, which buyers expect to go on (raw Fx).</summary>
        public readonly long[] Expectation;
        /// <summary>Per market: the premium at the last quarter's end.</summary>
        public readonly long[] QuarterPremium;
        /// <summary>Per market: what its households owe their nation's bank on what they hold there.</summary>
        public readonly long[] Debt;
        /// <summary>Per market and quarter, the latest at <see cref="HistoryIndex"/>: the debt at each of the last quarters' ends.</summary>
        public readonly long[] DebtHistory;
        public int HistoryIndex;
        public readonly byte[] Phase;
        public readonly int[] Bursts;
        /// <summary>Per market, published each quarter: the premium, leverage (debt over market value) and the year's credit growth, raw Fx.</summary>
        public readonly long[] PublishedPremium, PublishedLeverage, PublishedCreditGrowth;
        /// <summary>Per market: its households' idle money at the last quarter's end; what comes in since buys too.</summary>
        public readonly long[] Idle;
        /// <summary>Per nation: margin debt the households could not pay on a call, written off since the start.</summary>
        public readonly long[] WrittenOff;
        /// <summary>Not saved: nations a market burst in this quarter; the finance records start their credit crunch.</summary>
        public readonly bool[] BurstNation;

        public string Key => "agents.asset_markets";
        public int Version => 1;

        public AssetMarkets(NodeTable nodes, int nationCount)
        {
            _nodes = nodes;
            NationCount = nationCount;
            int count = nationCount + nodes.Count;
            Premium = new long[count];
            Expectation = new long[count];
            QuarterPremium = new long[count];
            Debt = new long[count];
            DebtHistory = new long[count * History];
            Phase = new byte[count];
            Bursts = new int[count];
            PublishedPremium = new long[count];
            PublishedLeverage = new long[count];
            PublishedCreditGrowth = new long[count];
            Idle = new long[count];
            WrittenOff = new long[nationCount];
            BurstNation = new bool[nationCount];
            for (int m = 0; m < count; m++) Premium[m] = QuarterPremium[m] = PublishedPremium[m] = Fx.OneRaw;
        }

        public int Count => Premium.Length;
        public static int Stock(int nation) => nation;
        public int Property(int node) => NationCount + Households(node);
        public int Households(int node) => _nodes.Absorbed ? _nodes.Core[node] : node;
        public bool IsStock(int market) => market < NationCount;

        public Fx StockPremium(int nation) => nation < 0 ? Fx.One : Fx.FromRaw(Premium[nation]);
        public Fx PropertyPremium(int node) => Fx.FromRaw(Premium[Property(node)]);

        /// <summary>A nation's households' margin debt, on its exchange and its towns' land.</summary>
        public long NationDebt(int nation, NodeBanks banks)
        {
            long debt = Debt[nation];
            for (int n = 0; n < _nodes.Count; n++)
                if (Debt[NationCount + n] != 0 && banks.NationOf(n) == nation) debt += Debt[NationCount + n];
            return debt;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Premium.Length);
            writer.Write(HistoryIndex);
            foreach (var values in Longs()) foreach (long v in values) writer.Write(v);
            foreach (long v in DebtHistory) writer.Write(v);
            foreach (long v in WrittenOff) writer.Write(v);
            for (int m = 0; m < Premium.Length; m++)
            {
                writer.Write(Phase[m]);
                writer.Write(Bursts[m]);
            }
        }

        public void Read(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count != Premium.Length) throw new InvalidDataException($"agents.asset_markets: {count} markets, the map has {Premium.Length}.");
            HistoryIndex = reader.ReadInt32();
            foreach (var values in Longs()) for (int i = 0; i < values.Length; i++) values[i] = reader.ReadInt64();
            for (int i = 0; i < DebtHistory.Length; i++) DebtHistory[i] = reader.ReadInt64();
            for (int i = 0; i < WrittenOff.Length; i++) WrittenOff[i] = reader.ReadInt64();
            for (int m = 0; m < count; m++)
            {
                Phase[m] = reader.ReadByte();
                Bursts[m] = reader.ReadInt32();
            }
        }

        IEnumerable<long[]> Longs()
        {
            yield return Premium;
            yield return Expectation;
            yield return QuarterPremium;
            yield return Debt;
            yield return PublishedPremium;
            yield return PublishedLeverage;
            yield return PublishedCreditGrowth;
            yield return Idle;
        }
    }

    /// <summary>
    /// DEC-131: each week the households of each market pay interest on their margin debt, borrow on a rising market up
    /// to the loan-to-value of what they hold at its price, and on a call pay back to it, the rest written off. Credit,
    /// the last quarter's change and a pull back to the fundamental move the premium. Each quarter the indicators are
    /// published and a market heats or bursts on them: no dice, the same state always gives the same burst.
    /// </summary>
    public sealed class AssetMarketSystem : ISimSystem
    {
        GameDatabase _db;
        WorldState _world;
        AssetMarkets _markets;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        FinanceState _finance;
        ShareTable _shares;
        LandState _land;
        long[] _value = Array.Empty<long>();
        long[] _idle = Array.Empty<long>();
        int[] _lender = Array.Empty<int>();

        public string Id => "agents.asset_markets";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.AssetMarkets;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _world = ctx.World;
            _markets = ctx.World.Get<AssetMarkets>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _finance = ctx.World.Get<FinanceState>();
            _shares = ctx.World.Get<ShareTable>();
            _land = ctx.World.Get<LandState>();
        }

        public void Execute(SimContext ctx)
        {
            var config = _db.Config;
            if (config.MarketCreditShare.Raw <= 0 && config.MarketMomentum.Raw <= 0) return;
            Fundamentals(config);
            for (int m = 0; m < _markets.Count; m++)
            {
                if (_lender[m] < 0 || (_value[m] <= 0 && _markets.Debt[m] == 0)) continue;
                long net = Credit(ctx, m, config);
                Reprice(m, net, config);
            }
            if (ctx.Clock.IsQuarterEnd) CloseQuarter(ctx, config);
        }

        // The fundamental value of what each market's households hold: their shares at the facilities' prices (DEC-129),
        // and the plots they still sell at the price by scarcity (DEC-128). And the bank each market borrows from.
        void Fundamentals(SimulationConfig config)
        {
            int count = _markets.Count;
            if (_value.Length != count)
            {
                _value = new long[count];
                _lender = new int[count];
            }
            Array.Clear(_value, 0, count);
            for (int n = 0; n < _markets.NationCount; n++) _lender[n] = n;
            for (int n = 0; n < _nodes.Count; n++) _lender[_markets.NationCount + n] = -1;
            for (int r = 0; r < _shares.Count.Length; r++)
                if (ShareTable.IsHouseholds(_shares.Holder[r], out int nation))
                    _value[nation] += _shares.Count[r] * ShareBook.Fundamental(_world, _db, _shares.Facility[r]);
            for (int n = 0; n < _nodes.Count; n++)
            {
                int m = _markets.Property(n);
                int nation = _banks.NationOf(n);
                if (nation < 0) continue;
                _lender[m] = nation;
                _value[m] += _land.Free(n) * _land.Fundamental(n, config);
            }
        }

        // The week's interest, then a call or new borrowing. Returns the credit that came into the market (less what left it).
        long Credit(SimContext ctx, int m, SimulationConfig config)
        {
            int nation = _lender[m];
            long debt = _markets.Debt[m];
            if (debt > 0)
            {
                // Interest, and a share of the debt paid off, so the room to borrow comes back over the years.
                long interest = Fx.FromRaw(_finance.LoanRate[nation]).MulInt(debt) / SimClock.WeeksPerYear;
                long principal = config.MarketRepayShare.MulInt(debt) / SimClock.WeeksPerYear;
                long paid = Take(ctx, m, interest + principal);
                _bank.Reserve[nation] += paid;
                _markets.Debt[m] += interest - paid;
            }
            var premium = Fx.FromRaw(_markets.Premium[m]);
            long value = premium.MulInt(_value[m]);
            long allowed = config.LoanToValue.MulInt(value);
            debt = _markets.Debt[m];
            if (debt > config.MarginCallShare.MulInt(value))
            {
                long excess = debt - allowed;
                long paid = Take(ctx, m, excess);
                _bank.Reserve[nation] += paid;
                _markets.Debt[m] -= excess;
                _markets.WrittenOff[nation] += excess - paid; // [Draft] what they cannot pay is lost, sold for nothing
                return -excess;
            }
            if (_markets.Phase[m] == (byte)AssetMarketPhase.Bust || _markets.Expectation[m] < 0
                || _finance.EventOf(nation) == FinancialEventKind.CreditCrunch) return 0;
            long draw = Math.Min(config.MarketCreditShare.MulInt(Math.Max(0, allowed - debt)), _bank.Spendable(nation));
            if (draw <= 0) return 0;
            _bank.Reserve[nation] -= draw;
            _markets.Debt[m] += draw;
            if (_markets.IsStock(m)) MoneyLedger.PayNationHouseholds(_world, nation, draw, true);
            else Lend(m - _markets.NationCount, draw);
            return draw;
        }

        void Lend(int households, long amount)
        {
            var cash = _world.Get<HouseholdCash>();
            cash.Cash[households] += amount;
            cash.SavedYear[households] -= amount; // borrowed, not earned
        }

        long Take(SimContext ctx, int m, long amount) =>
            amount <= 0 ? 0
            : _markets.IsStock(m) ? BondDesk.TakeFromHouseholds(_world, _db.Config, ctx.Turn, m, amount)
            : BondDesk.TakeFromNode(_world, _db.Config, ctx.Turn, m - _markets.NationCount, amount);

        // Buyers carry on the last quarter's change, credit coming in or going out moves the price by its share of the
        // fundamental value, and the premium is pulled back toward one.
        void Reprice(int m, long net, SimulationConfig config)
        {
            var premium = Fx.FromRaw(_markets.Premium[m]);
            var change = premium * config.MarketMomentum * Fx.FromRaw(_markets.Expectation[m]) / Fx.FromInt(SimClock.WeeksPerQuarter);
            if (_value[m] > 0 && net != 0) change += config.MarketCreditWeight * Fx.FromRatio(net, _value[m]);
            change -= config.MarketReversion * (premium - Fx.One) / Fx.FromInt(SimClock.WeeksPerYear);
            var most = config.MarketWeeklyMove * premium;
            premium += Fx.Clamp(change, -most, most);
            _markets.Premium[m] = Fx.Clamp(premium, config.MarketPremiumFloor, config.MarketPremiumCap).Raw;
        }

        void CloseQuarter(SimContext ctx, SimulationConfig config)
        {
            Array.Clear(_markets.BurstNation, 0, _markets.BurstNation.Length);
            _markets.HistoryIndex = (_markets.HistoryIndex + 1) % AssetMarkets.History;
            IdleMoney(config);
            for (int m = 0; m < _markets.Count; m++)
            {
                if (_lender[m] < 0) continue;
                var premium = Fx.FromRaw(_markets.Premium[m]);
                long value = premium.MulInt(_value[m]);
                Expect(ctx, m, premium, value, config);
                Publish(m, premium, value, config, out var leverage, out var growth);
                Judge(ctx, m, premium, leverage, growth, config);
            }
        }

        // Buyers expect the last quarter's change to go on, and new idle money looks for something to buy.
        void Expect(SimContext ctx, int m, Fx premium, long value, SimulationConfig config)
        {
            var expectation = premium / Fx.FromRaw(_markets.QuarterPremium[m]) - Fx.One;
            if (ctx.Turn >= SimClock.WeeksPerQuarter && value > 0)
                expectation += (_markets.IsStock(m) ? config.MarketIdleWeight : config.MarketPropertyIdleWeight) * Fx.FromRatio(_idle[m] - _markets.Idle[m], value);
            _markets.Idle[m] = _idle[m];
            var most = config.MarketWeeklyMove * Fx.FromInt(SimClock.WeeksPerQuarter);
            _markets.Expectation[m] = Fx.Clamp(expectation, -most, most).Raw;
            _markets.QuarterPremium[m] = premium.Raw;
        }

        // The quarter's indicators: the price over the fundamental, leverage, and credit growth over the year.
        void Publish(int m, Fx premium, long value, SimulationConfig config, out Fx leverage, out Fx growth)
        {
            int slot = m * AssetMarkets.History + _markets.HistoryIndex;
            long yearAgo = _markets.DebtHistory[slot]; // the oldest, about to be replaced
            long debt = _markets.Debt[m];
            _markets.DebtHistory[slot] = debt;
            if (value > 0) leverage = Fx.FromRatio(debt, value);
            else leverage = debt > 0 ? config.MarketLeverageLimit + Fx.One : Fx.Zero;
            if (yearAgo > 0) growth = Fx.FromRatio(debt - yearAgo, yearAgo);
            else growth = debt > 0 ? Fx.One : Fx.Zero;
            _markets.PublishedPremium[m] = premium.Raw;
            _markets.PublishedLeverage[m] = leverage.Raw;
            _markets.PublishedCreditGrowth[m] = growth.Raw;
        }

        // What households hold above the weeks of spending they keep, as for bonds (DEC-097): a nation's for its
        // exchange, a node's for its land.
        void IdleMoney(SimulationConfig config)
        {
            if (_idle.Length != _markets.Count) _idle = new long[_markets.Count];
            Array.Clear(_idle, 0, _idle.Length);
            var cash = _world.Get<HouseholdCash>();
            var purses = _world.Get<PurseState>();
            for (int n = 0; n < _nodes.Count; n++)
            {
                if (_markets.Households(n) != n) continue;
                long idle = Math.Max(0, cash.Cash[n] - config.BondKeepWeeks * cash.Desired[n]);
                _idle[_markets.Property(n)] = idle;
                int nation = purses.NodeFloat[n];
                if (nation < _markets.NationCount) _idle[nation] += idle;
            }
        }

        // Heated above the first line; bursts above the second, or when heated and credit shrinks on a leveraged market
        // or the bank's cover falls under its floor. The bust ends once the price stops falling.
        void Judge(SimContext ctx, int m, Fx premium, Fx leverage, Fx growth, SimulationConfig config)
        {
            var phase = (AssetMarketPhase)_markets.Phase[m];
            if (phase == AssetMarketPhase.Bust)
            {
                if (_markets.Expectation[m] >= 0) _markets.Phase[m] = (byte)AssetMarketPhase.Calm;
                return;
            }
            var latest = _finance.Latest;
            var cover = latest == null ? Fx.FromInt(1000) : latest.ReserveRatio(_lender[m]);
            bool heated = phase == AssetMarketPhase.Heated;
            bool burst = premium >= config.MarketBurstPremium
                || (heated && growth < Fx.Zero && leverage > config.MarketLeverageLimit)
                || (heated && config.BankRunCover.Raw > 0 && cover < config.BankRunCover);
            if (burst) Burst(ctx, m, premium, config);
            else if (!heated && premium >= config.MarketHeatedPremium)
            {
                _markets.Phase[m] = (byte)AssetMarketPhase.Heated;
                ctx.Events.Publish(new MarketHeated(Kind(m), MarketNode(m), premium, ctx.Turn));
            }
            else if (heated && premium < config.MarketHeatedPremium) _markets.Phase[m] = (byte)AssetMarketPhase.Calm;
        }

        // A burst drops the price at once and turns what buyers expect.
        void Burst(SimContext ctx, int m, Fx premium, SimulationConfig config)
        {
            int nation = _lender[m];
            var latest = _finance.Latest;
            var fallen = premium - config.MarketBurstDrop * premium;
            _markets.Premium[m] = _markets.QuarterPremium[m] = fallen.Raw;
            _markets.Expectation[m] = (-config.MarketBurstDrop).Raw;
            _markets.Phase[m] = (byte)AssetMarketPhase.Bust;
            _markets.Bursts[m]++;
            // A town's land stops its nation's credit only when the bank has lent enough on it (the market's reach).
            if (_markets.IsStock(m) || latest == null || _markets.Debt[m] >= config.MarketCrunchShare.MulInt(latest.NationLent[nation]))
                _markets.BurstNation[nation] = true;
            ctx.Events.Publish(new MarketBurst(Kind(m), MarketNode(m), premium, ctx.Turn));
        }

        AssetMarketKind Kind(int m) => _markets.IsStock(m) ? AssetMarketKind.Stock : AssetMarketKind.Property;

        NodeId MarketNode(int m) => new NodeId(_markets.IsStock(m) ? _banks.NationNode(m) : m - _markets.NationCount);
    }
}
