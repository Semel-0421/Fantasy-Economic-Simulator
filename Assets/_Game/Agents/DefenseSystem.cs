using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    /// <summary>
    /// A nation's defense (standing army, mercenaries and their upkeep) costs a fixed share of its yearly budget, the
    /// reserve at the year's start without the player's deposits, plus maintenance on top. It is bought every few weeks
    /// from the stock of the nation's markets at current prices, so the money reaches its traders' float at once.
    /// The army makes the nation's routes safer in proportion to what it costs per route, up to a limit.
    /// </summary>
    public sealed class DefenseSystem : ISimSystem, IDisposable
    {
        SimulationConfig _config;
        BankState _bank;
        PurseState _purses;
        MarketStore _markets;
        DevelopmentState _development;
        NativeArray<long> _spend;
        NativeArray<long> _paid;
        NativeArray<long> _value;
        int[] _nationRoutes;

        public string Id => "agents.defense";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Defense;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _bank = ctx.World.Get<BankState>();
            _purses = ctx.World.Get<PurseState>();
            _markets = ctx.World.Get<MarketStore>();
            _development = ctx.World.Get<DevelopmentState>();
            int nations = _development.YearBudget.Length;
            _spend = new NativeArray<long>(nations, Allocator.Persistent);
            _paid = new NativeArray<long>(nations, Allocator.Persistent);
            _value = new NativeArray<long>(nations, Allocator.Persistent);
            _nationRoutes = new int[nations];
            var routes = ctx.World.Get<RouteTable>();
            for (int r = 0; r < routes.Count; r++)
                if (_purses.NodeFloat[routes.From[r]] < nations) _nationRoutes[_purses.NodeFloat[routes.From[r]]]++;
        }

        public void Execute(SimContext ctx)
        {
            for (int n = 0; n < _spend.Length; n++)
            {
                if (ctx.Clock.IsNewYear)
                {
                    _development.YearBudget[n] = _bank.Spendable(n);
                    _development.DefenseSpent[n] = 0;
                }
                long yearly = _config.DefenseShare.MulInt(_development.YearBudget[n]);
                yearly += _config.DefenseMaintenance.MulInt(yearly);
                if (ctx.Clock.IsNewYear)
                    _development.Security[n] = Fx.Min(_config.DefenseMaxSafety,
                        Fx.FromRatio(yearly, Math.Max(1, _nationRoutes[n] * _config.DefenseSafetyCostPerRoute))).Raw;
                _spend[n] = Math.Min(yearly * _config.DefenseIntervalWeeks / SimClock.WeeksPerYear, _bank.Spendable(n));
            }
            if (ctx.Turn % _config.DefenseIntervalWeeks != 0) return;
            new DefensePurchaseJob
            {
                NodeFloat = _purses.NodeFloat,
                MarketCellStart = _markets.MarketCellStart,
                MarketCellCount = _markets.MarketCellCount,
                Price = _markets.Price,
                Stock = _markets.Stock,
                Demand = _markets.Demand,
                Spend = _spend,
                Paid = _paid,
                Value = _value,
            }.Schedule().Complete();
            for (int n = 0; n < _paid.Length; n++)
            {
                _bank.Reserve[n] -= _paid[n];
                _purses.Float[n] += _paid[n];
                _development.DefenseSpent[n] += _paid[n];
            }
        }

        public void Dispose()
        {
            _spend.DisposeIfCreated();
            _paid.DisposeIfCreated();
            _value.DisposeIfCreated();
        }
    }

    /// <summary>Each nation's weekly purchases, across all its stock in proportion to value, as far as there is stock to buy.</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct DefensePurchaseJob : IJob
    {
        [ReadOnly] public NativeArray<int> NodeFloat;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> Price;
        [ReadOnly] public NativeArray<long> Spend;
        public NativeArray<int> Stock;
        public NativeArray<int> Demand;
        public NativeArray<long> Paid;
        /// <summary>Scratch, per nation: the value of its stock.</summary>
        public NativeArray<long> Value;

        public void Execute()
        {
            for (int n = 0; n < Value.Length; n++)
            {
                Value[n] = 0;
                Paid[n] = 0;
            }
            for (int node = 0; node < NodeFloat.Length; node++)
            {
                int nation = NodeFloat[node];
                if (nation >= Value.Length || Spend[nation] <= 0) continue;
                int end = MarketCellStart[node] + MarketCellCount[node];
                for (int c = MarketCellStart[node]; c < end; c++) Value[nation] += (long)Stock[c] * Price[c];
            }
            for (int node = 0; node < NodeFloat.Length; node++)
            {
                int nation = NodeFloat[node];
                if (nation >= Value.Length || Spend[nation] <= 0 || Value[nation] <= 0) continue;
                long spend = Spend[nation], value = Value[nation];
                int end = MarketCellStart[node] + MarketCellCount[node];
                for (int c = MarketCellStart[node]; c < end; c++)
                {
                    long units = value <= spend ? Stock[c] : Stock[c] * spend / value;
                    if (units <= 0) continue;
                    Stock[c] -= (int)units;
                    Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + units);
                    Paid[nation] += units * Price[c];
                }
            }
        }
    }
}
