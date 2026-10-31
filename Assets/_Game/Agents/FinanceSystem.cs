using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class FinancialEventStarted : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly FinancialEventKind Kind;
        public readonly int Turn;

        public FinancialEventStarted(NodeId nation, FinancialEventKind kind, int turn)
        {
            Nation = nation;
            Kind = kind;
            Turn = turn;
        }
    }

    public sealed class FinancialEventEnded : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly FinancialEventKind Kind;
        public readonly int Turn;

        public FinancialEventEnded(NodeId nation, FinancialEventKind kind, int turn)
        {
            Nation = nation;
            Kind = kind;
            Turn = turn;
        }
    }

    public sealed class StatisticsPublished : ISimEvent
    {
        public readonly int Year;

        public StatisticsPublished(int year) => Year = year;
    }

    /// <summary>
    /// Records, rates, statistics and financial events (Finance phase, after the banks): at each quarter's end
    /// the money totals are recorded (DEC-043), loan rates follow the reserve ratio (DEC-042) and events start or
    /// end (DEC-044, DEC-047); at each new year the statistics of the year before are published.
    /// </summary>
    public sealed class FinanceSystem : ISimSystem
    {
        static readonly uint EventStream = DetHash.Of("agents.finance.events");

        SimulationConfig _config;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        LoanTable _loans;
        NpcLoanTable _npcLoans;
        FinanceState _finance;
        MoneyTotals _totals;
        CurrencyState _currency;

        public string Id => "agents.finance";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Records;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _loans = ctx.World.Get<LoanTable>();
            _npcLoans = ctx.World.Get<NpcLoanTable>();
            _finance = ctx.World.Get<FinanceState>();
            _totals = ctx.World.Get<MoneyTotals>();
            _currency = ctx.World.Get<CurrencyState>();
        }

        public void Execute(SimContext ctx)
        {
            if (ctx.Clock.IsNewYear && ctx.Turn > 0) Publish(ctx);
            if (!ctx.Clock.IsQuarterEnd) return;

            _totals.Recompute(ctx.World);
            var record = Record(ctx.Turn);
            for (int n = 0; n < _finance.NationCount; n++)
            {
                UpdateEvent(ctx, n, record.ReserveRatio(n));
                SetRate(n, record.ReserveRatio(n));
            }
        }

        FinanceRecord Record(int turn)
        {
            var record = new FinanceRecord(_finance.NationCount, _finance.ContinentCount) { Turn = turn };
            for (int n = 0; n < _finance.NationCount; n++)
            {
                record.NationTotal[n] = _totals.NationTotal(n);
                record.NationCash[n] = _totals.NationCash[n];
                record.NationReserve[n] = _totals.NationReserve[n];
                record.NationDeposits[n] = _totals.NationDeposits[n];
            }
            for (int r = 0; r < _npcLoans.Count; r++) record.NationLent[_npcLoans.Nation[r]] += _npcLoans.Balance[r];
            for (int r = 0; r < _loans.Count; r++) record.NationLent[_loans.Nation[r]] += _loans.Balance[r];
            for (int c = 0; c < _finance.ContinentCount; c++)
            {
                record.ContinentTotal[c] = _totals.ContinentTotal[c];
                record.PriceIndex[c] = _currency.PriceIndex[c].Raw;
            }
            _finance.Records.Add(record);
            while (_finance.Records.Count > _config.FinanceHistoryQuarters) _finance.Records.RemoveAt(0);
            return record;
        }

        // An event lasts at least a quarter and ends once the reserve ratio has recovered (DEC-047); a nation
        // without one may draw a new one at random (DEC-044).
        void UpdateEvent(SimContext ctx, int n, Fx ratio)
        {
            var nation = new NodeId(_banks.NationNode(n));
            var current = _finance.EventOf(n);
            if (current != FinancialEventKind.None)
            {
                if (ctx.Turn - _finance.EventStart[n] < SimClock.WeeksPerQuarter || ratio < _config.FinancialEventEndReserveRatio) return;
                _finance.Event[n] = (byte)FinancialEventKind.None;
                ctx.Events.Publish(new FinancialEventEnded(nation, current, ctx.Turn));
                return;
            }
            var rng = DetRandom.For(ctx.Seed, ctx.Turn, EventStream, n);
            if (!rng.ChancePermille(_config.FinancialEventChancePermille)) return;
            var kind = (FinancialEventKind)(1 + rng.NextInt(3));
            _finance.Event[n] = (byte)kind;
            _finance.EventStart[n] = ctx.Turn;
            // [Draft] A run starts with the public taking a share of the bank's coins out.
            if (kind == FinancialEventKind.BankRun)
            {
                // The withdrawn coins go to the nation's households (DEC-061).
                long drain = _config.BankRunDrain.MulInt(Math.Max(0, _bank.Reserve[n]));
                _bank.Reserve[n] -= drain;
                MoneyLedger.PayNationHouseholds(ctx.World, n, drain);
            }
            ctx.Events.Publish(new FinancialEventStarted(nation, kind, ctx.Turn));
        }

        void SetRate(int n, Fx ratio) => _finance.LoanRate[n] = FinanceRules.LoanRate(_config, ratio, _finance.EventOf(n)).Raw;

        // The statistics of the year that just ended, from its last quarter's record (DEC-043, DEC-044).
        void Publish(SimContext ctx)
        {
            var record = _finance.Latest;
            if (record == null) return;
            _finance.PublishedYear = ctx.Clock.Year - 1;
            for (int c = 0; c < _finance.ContinentCount; c++)
            {
                _finance.PublishedContinentTotal[c] = record.ContinentTotal[c];
                _currency.MoneyTotal[c] = record.ContinentTotal[c]; // prices follow it from now on (DEC-062)
                var previous = Fx.FromRaw(_finance.PublishedPriceIndex[c]);
                var index = Fx.FromRaw(record.PriceIndex[c]);
                _finance.PublishedInflation[c] = (index / previous - Fx.One).Raw;
                _finance.PublishedPriceIndex[c] = index.Raw;
            }
            for (int n = 0; n < _finance.NationCount; n++)
            {
                _finance.PublishedNationTotal[n] = record.NationTotal[n];
                _finance.PublishedReserveRatio[n] = record.ReserveRatio(n).Raw;
                _finance.PublishedLoanRate[n] = _finance.LoanRate[n];
            }
            ctx.Events.Publish(new StatisticsPublished(_finance.PublishedYear));
        }
    }
}
