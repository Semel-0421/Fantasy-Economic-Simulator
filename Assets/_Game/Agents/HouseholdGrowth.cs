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
    /// necessities and slower, or shrinking, where prices priced them out. Income per head follows the
    /// nation's money per head in real terms, from the last two years' records (DEC-048, [Draft]).
    /// </summary>
    public sealed class HouseholdGrowthSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        NodeBanks _banks;
        HouseholdState _households;
        FinanceState _finance;
        Fx[] _incomeChange;

        public string Id => "agents.households";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 20;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _households = ctx.World.Get<HouseholdState>();
            _finance = ctx.World.Get<FinanceState>();
            _incomeChange = new Fx[_banks.NationCount];
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;

            for (int n = 0; n < _incomeChange.Length; n++) _incomeChange[n] = IncomeChange(n);
            for (int node = 0; node < _households.NodeCount; node++)
            {
                _households.Population[node] += PopulationChange(node).MulInt(_households.Population[node]);
                int nationNode = _nodes.AncestorAt(node, WorldLevel.Nation);
                if (nationNode >= 0)
                {
                    var income = Fx.FromRaw(_households.IncomePerHead[node]);
                    _households.IncomePerHead[node] = (income + income * _incomeChange[_banks.NationIndex(nationNode)]).Raw;
                }
                _households.NecessityWanted[node] = 0;
                _households.NecessityMet[node] = 0;
            }
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

        // The nation's money total over its value a year before, deflated by its continent's price index.
        Fx IncomeChange(int nation)
        {
            var records = _finance.Records;
            if (records.Count < 2) return Fx.Zero;
            var latest = records[records.Count - 1];
            var before = records[0];
            for (int i = records.Count - 2; i >= 0; i--)
            {
                before = records[i];
                if (records[i].Turn <= latest.Turn - SimClock.WeeksPerYear) break;
            }
            if (before.NationTotal[nation] <= 0) return Fx.Zero;
            int continent = _nodes.Continent[_banks.NationNode(nation)];
            var nominal = Fx.FromRatio(latest.NationTotal[nation], before.NationTotal[nation]);
            var prices = Fx.FromRatio(latest.PriceIndex[continent], Math.Max(1, before.PriceIndex[continent]));
            var real = nominal / prices - Fx.One;
            return Clamp(_config.IncomeResponse * real, _config.IncomeMaxChange);
        }

        static Fx Clamp(Fx value, Fx limit) => Fx.Clamp(value, -limit, limit);
    }
}
