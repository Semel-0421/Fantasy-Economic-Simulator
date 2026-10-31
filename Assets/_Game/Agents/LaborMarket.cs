using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// The labor market of each traders' float's area (nation, or a continent outside its nations): its employment
    /// rate, and the real household spending that rate follows (SYS-MARKETS).
    /// </summary>
    public sealed class LaborState : IPersistentComponent
    {
        /// <summary>Raw Fx, per float.</summary>
        public readonly long[] Employment;
        /// <summary>Raw Fx, per float: the long-run real spending per head; zero until first measured.</summary>
        public readonly long[] LongRunActivity;
        public readonly long[] QuarterSpending;

        public string Key => "agents.labor";
        public int Version => 1;

        public LaborState(int floats, Fx baseEmployment)
        {
            Employment = new long[floats];
            LongRunActivity = new long[floats];
            QuarterSpending = new long[floats];
            for (int p = 0; p < floats; p++) Employment[p] = baseEmployment.Raw;
        }

        public Fx Rate(int purse) => Fx.FromRaw(Employment[purse]);

        public void Write(BinaryWriter writer)
        {
            writer.Write(Employment.Length);
            for (int p = 0; p < Employment.Length; p++)
            {
                writer.Write(Employment[p]);
                writer.Write(LongRunActivity[p]);
                writer.Write(QuarterSpending[p]);
            }
        }

        public void Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != Employment.Length) throw new InvalidDataException("Saved purse count differs.");
            for (int p = 0; p < Employment.Length; p++)
            {
                Employment[p] = reader.ReadInt64();
                LongRunActivity[p] = reader.ReadInt64();
                QuarterSpending[p] = reader.ReadInt64();
            }
        }
    }

    /// <summary>
    /// Merchant groups hire on the labor market: each week a group pays a share of its cash above its working
    /// capital in wages, scaled by the employment rate, to the households of its nation by population. Each
    /// quarter the rate moves toward the base rate times real spending per head over its long-run level.
    /// </summary>
    public sealed class LaborMarketSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        AgentTable _agents;
        PurseState _purses;
        LaborState _labor;
        HouseholdState _households;
        CurrencyState _currency;
        WorldState _world;
        long[] _wages;
        long[] _people;

        public string Id => "agents.labor";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Labor;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _agents = ctx.World.Get<AgentTable>();
            _purses = ctx.World.Get<PurseState>();
            _labor = ctx.World.Get<LaborState>();
            _households = ctx.World.Get<HouseholdState>();
            _currency = ctx.World.Get<CurrencyState>();
            _world = ctx.World;
            _wages = new long[_purses.NationCount];
            _people = new long[_purses.Count];
        }

        public void Execute(SimContext ctx)
        {
            PayWages();
            if (ctx.Clock.IsQuarterEnd) UpdateEmployment();
        }

        void PayWages()
        {
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Owner[a] != OwnerIds.Npc) continue;
                long buffer = _config.LaborMerchantBuffer.MulInt(_config.MerchantStartCash[_agents.Scale[a]]);
                long excess = _agents.Cash[a] - buffer;
                if (excess <= 0) continue;
                int node = _agents.Node[a], purse = _purses.NodeFloat[node];
                long wage = _labor.Rate(purse).MulInt(_config.LaborMerchantWage.MulInt(excess));
                _agents.Cash[a] -= wage;
                if (purse < _purses.NationCount) _wages[purse] += wage;
                else _purses.PayHouseholds(node, wage);
            }
            for (int n = 0; n < _wages.Length; n++)
            {
                if (_wages[n] == 0) continue;
                MoneyLedger.PayNationHouseholds(_world, n, _wages[n]);
                _wages[n] = 0;
            }
        }

        void UpdateEmployment()
        {
            Array.Clear(_people, 0, _people.Length);
            for (int node = 0; node < _nodes.Count; node++) _people[_purses.NodeFloat[node]] += _households.Population[node];
            for (int p = 0; p < _people.Length; p++)
            {
                long spent = _labor.QuarterSpending[p];
                _labor.QuarterSpending[p] = 0;
                if (_people[p] <= 0) continue;
                var activity = Fx.FromRatio(spent, _people[p]) / _currency.PriceIndex[_purses.ContinentOf(p)];
                if (_labor.LongRunActivity[p] <= 0)
                {
                    _labor.LongRunActivity[p] = activity.Raw;
                    continue;
                }
                var longRun = Fx.FromRaw(_labor.LongRunActivity[p]);
                if (longRun.Raw <= 0) continue;
                var target = Fx.Clamp(_config.LaborBaseEmployment * (activity / longRun), _config.LaborMinEmployment, _config.LaborMaxEmployment);
                var rate = _labor.Rate(p);
                _labor.Employment[p] = (rate + _config.LaborResponse * (target - rate)).Raw;
                _labor.LongRunActivity[p] = (longRun + _config.LaborLongRunResponse * (activity - longRun)).Raw;
            }
        }
    }
}
