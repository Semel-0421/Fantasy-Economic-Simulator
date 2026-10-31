using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

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
        public int Order => SimOrder.Late + 6;

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

    public sealed class IncomeTaxCollected : ISimEvent
    {
        /// <summary>The year taxed.</summary>
        public readonly int Year;
        public readonly long Households;
        public readonly long Companies;

        public IncomeTaxCollected(int year, long households, long companies)
        {
            Year = year;
            Households = households;
            Companies = companies;
        }
    }

    /// <summary>
    /// What the income tax needs to know of each company: its cash when the tax year began, and the dividends a
    /// facility has paid since. A new operator starts a new book, so its starting cash is not profit.
    /// </summary>
    public sealed class IncomeTaxState : IPersistentComponent, IDisposable
    {
        public NativeList<long> FacilityStart;
        public NativeList<long> FacilityDividends;
        public NativeList<long> MerchantStart;
        /// <summary>False until the books are opened: a new game, or a save from before the tax.</summary>
        public bool Opened;
        /// <summary>Per continent: last year's collections.</summary>
        public readonly long[] LastHouseholds;
        public readonly long[] LastCompanies;

        public string Key => "agents.income_tax";
        public int Version => 1;

        public IncomeTaxState(int continents)
        {
            FacilityStart = new NativeList<long>(Allocator.Persistent);
            FacilityDividends = new NativeList<long>(Allocator.Persistent);
            MerchantStart = new NativeList<long>(Allocator.Persistent);
            LastHouseholds = new long[continents];
            LastCompanies = new long[continents];
        }

        /// <summary>Companies founded since the last call start their books with their cash.</summary>
        public void EnsureRows(FacilityTable facilities, AgentTable agents)
        {
            for (int f = FacilityStart.Length; f < facilities.Count; f++)
            {
                FacilityStart.Add(facilities.Cash[f]);
                FacilityDividends.Add(0);
            }
            for (int a = MerchantStart.Length; a < agents.Count; a++) MerchantStart.Add(agents.Cash[a]);
        }

        public void RestartFacility(int f, long cash)
        {
            if (f >= FacilityStart.Length) return;
            FacilityStart[f] = cash;
            FacilityDividends[f] = 0;
        }

        public void RestartMerchant(int a, long cash)
        {
            if (a < MerchantStart.Length) MerchantStart[a] = cash;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Opened);
            WriteList(writer, FacilityStart);
            WriteList(writer, FacilityDividends);
            WriteList(writer, MerchantStart);
            for (int c = 0; c < LastHouseholds.Length; c++)
            {
                writer.Write(LastHouseholds[c]);
                writer.Write(LastCompanies[c]);
            }
        }

        public void Read(BinaryReader reader)
        {
            Opened = reader.ReadBoolean();
            ReadList(reader, FacilityStart);
            ReadList(reader, FacilityDividends);
            ReadList(reader, MerchantStart);
            for (int c = 0; c < LastHouseholds.Length; c++)
            {
                LastHouseholds[c] = reader.ReadInt64();
                LastCompanies[c] = reader.ReadInt64();
            }
        }

        static void WriteList(BinaryWriter writer, NativeList<long> list)
        {
            writer.Write(list.Length);
            for (int i = 0; i < list.Length; i++) writer.Write(list[i]);
        }

        static void ReadList(BinaryReader reader, NativeList<long> list)
        {
            list.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) list.Add(reader.ReadInt64());
        }

        public void Dispose()
        {
            if (FacilityStart.IsCreated) FacilityStart.Dispose();
            if (FacilityDividends.IsCreated) FacilityDividends.Dispose();
            if (MerchantStart.IsCreated) MerchantStart.Dispose();
        }
    }

    /// <summary>
    /// Once a year every node's households pay income tax on what they measurably received, and every facility
    /// and merchant group on its profit: the change in its cash plus the dividends it paid. A loss carries no
    /// relief, and nobody pays beyond the cash they hold. The money goes to the nation's bank reserve (or the
    /// international bank outside a nation), and the state spends it through its reserve rule.
    /// A bank-held facility pays nothing. Interest groups are not taxed: their income is taxed facilities'
    /// profit, and it is taxed again in households when passed on. Runs before the year's books close.
    /// </summary>
    public sealed class IncomeTaxSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        HouseholdCash _households;
        FacilityTable _facilities;
        AgentTable _agents;
        IncomeTaxState _books;
        WorldState _world;

        public string Id => "agents.income_tax";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 15;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _households = ctx.World.Get<HouseholdCash>();
            _facilities = ctx.World.Get<FacilityTable>();
            _agents = ctx.World.Get<AgentTable>();
            _books = ctx.World.Get<IncomeTaxState>();
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            _books.EnsureRows(_facilities, _agents);
            if (!_books.Opened)
            {
                OpenBooks();
                return;
            }
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;

            Array.Clear(_books.LastHouseholds, 0, _books.LastHouseholds.Length);
            Array.Clear(_books.LastCompanies, 0, _books.LastCompanies.Length);
            long households = 0, companies = 0;
            for (int node = 0; node < _nodes.Count; node++)
            {
                long paid = Math.Min(_config.TaxHousehold.MulInt(Math.Max(0, _households.YearIncome(node))), Math.Max(0, _households.Cash[node]));
                if (paid <= 0) continue;
                _households.Cash[node] -= paid;
                MoneyLedger.ToState(_world, node, paid);
                _books.LastHouseholds[_nodes.Continent[node]] += paid;
                households += paid;
            }
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Owner[f] == OwnerIds.Bank) continue;
                long profit = _facilities.Cash[f] - _books.FacilityStart[f] + _books.FacilityDividends[f];
                long paid = Pay(_facilities.Node[f], profit, _facilities.Cash[f]);
                _facilities.Cash[f] -= paid;
                companies += paid;
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                long paid = Pay(_agents.Node[a], _agents.Cash[a] - _books.MerchantStart[a], _agents.Cash[a]);
                _agents.Cash[a] -= paid;
                companies += paid;
            }
            OpenBooks();
            ctx.Events.Publish(new IncomeTaxCollected(ctx.Clock.Year - 1, households, companies));
        }

        long Pay(int node, long profit, long cash)
        {
            long paid = Math.Min(_config.TaxCompany.MulInt(Math.Max(0, profit)), Math.Max(0, cash));
            if (paid <= 0) return 0;
            MoneyLedger.ToState(_world, node, paid);
            _books.LastCompanies[_nodes.Continent[node]] += paid;
            return paid;
        }

        void OpenBooks()
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                _books.FacilityStart[f] = _facilities.Cash[f];
                _books.FacilityDividends[f] = 0;
            }
            for (int a = 0; a < _agents.Count; a++) _books.MerchantStart[a] = _agents.Cash[a];
            _books.Opened = true;
        }
    }
}
