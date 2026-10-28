using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class HouseholdsUpdated : ISimEvent
    {
        /// <summary>The year whose conditions set the change.</summary>
        public readonly int Year;

        public HouseholdsUpdated(int year) => Year = year;
    }

    /// <summary>
    /// Population and income change once a year with economic conditions (DEC-050), after the year's
    /// statistics are published. Population grows by a base rate, faster where households could afford their
    /// necessities and slower, or shrinking, where prices priced them out. Income per head moves toward what
    /// each node's households measurably received over the year (P13c): wages, dividends and public wages.
    /// </summary>
    public sealed class HouseholdGrowthSystem : ISimSystem
    {
        SimulationConfig _config;
        HouseholdState _households;
        HouseholdCash _cash;

        public string Id => "agents.households";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 20;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _households = ctx.World.Get<HouseholdState>();
            _cash = ctx.World.Get<HouseholdCash>();
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;

            for (int node = 0; node < _households.NodeCount; node++)
            {
                _households.IncomePerHead[node] = Income(node).Raw;
                _households.Population[node] += PopulationChange(node).MulInt(_households.Population[node]);
                _households.NecessityWanted[node] = 0;
                _households.NecessityMet[node] = 0;
            }
            _cash.StartYear();
            ctx.Events.Publish(new HouseholdsUpdated(ctx.Clock.Year - 1));
        }

        // Base growth plus a share of how far the year's necessity demand stood above or below the need at
        // anchor prices.
        Fx PopulationChange(int node)
        {
            long wanted = _households.NecessityWanted[node];
            var change = _config.PopulationBaseGrowth;
            if (wanted > 0) change += _config.PopulationGrowthResponse * (Fx.FromRatio(_households.NecessityMet[node], wanted) - Fx.One);
            return Clamp(change, _config.PopulationMaxChange);
        }

        // A share of the way from last year's income per head to the year's measured income per head and week,
        // by at most the cap (of the larger of the two) either way. Nobody's income is below zero.
        Fx Income(int node)
        {
            var income = Fx.FromRaw(_households.IncomePerHead[node]);
            long people = _households.Population[node];
            if (people <= 0) return income;
            var measured = Fx.FromRatio(Math.Max(0, _cash.YearIncome(node)), people * SimClock.WeeksPerYear);
            var limit = _config.IncomeMaxChange * Fx.Max(income, measured);
            return income + Fx.Clamp(_config.IncomeResponse * (measured - income), -limit, limit);
        }

        static Fx Clamp(Fx value, Fx limit) => Fx.Clamp(value, -limit, limit);
    }
}
