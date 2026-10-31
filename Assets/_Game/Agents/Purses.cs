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
    /// The traders' floats (DEC-061, P13b): one per nation, in its continent's coin, plus one per continent for
    /// nodes outside any nation. Markets pay sellers only out of the float where the seller is, never more than
    /// it holds, and buyers pay into it, households included. Wages and fees go to the households of a node
    /// (<see cref="HouseholdCash"/>).
    /// </summary>
    public sealed class PurseState : IPersistentComponent, IDisposable
    {
        public readonly int NationCount;
        public readonly int ContinentCount;
        /// <summary>Per float: nations first, then one per continent. Payouts never take it below zero; rare settlements may.</summary>
        public NativeArray<long> Float;
        /// <summary>Per node: its float. Static, rebuilt from the map.</summary>
        public NativeArray<int> NodeFloat;
        /// <summary>Per nation: its nodes, in node order. Static, rebuilt from the map.</summary>
        public readonly int[][] NationNodes;
        readonly int[] _continentOfFloat;
        readonly HouseholdCash _households;

        public string Key => "agents.purses";
        public int Version => 2;

        public PurseState(NodeTable nodes, NodeBanks banks, HouseholdCash households)
        {
            _households = households;
            NationCount = banks.NationCount;
            ContinentCount = nodes.ContinentCount;
            Float = new NativeArray<long>(NationCount + ContinentCount, Allocator.Persistent);
            NodeFloat = new NativeArray<int>(nodes.Count, Allocator.Persistent);
            _continentOfFloat = new int[NationCount + ContinentCount];
            for (int n = 0; n < NationCount; n++) _continentOfFloat[n] = nodes.Continent[banks.NationNode(n)];
            for (int c = 0; c < ContinentCount; c++) _continentOfFloat[NationCount + c] = c;
            for (int node = 0; node < nodes.Count; node++)
            {
                int nationNode = nodes.AncestorAt(node, WorldLevel.Nation);
                NodeFloat[node] = nationNode >= 0 ? banks.NationIndex(nationNode) : NationCount + nodes.Continent[node];
            }
            var lists = new System.Collections.Generic.List<int>[NationCount];
            for (int n = 0; n < NationCount; n++) lists[n] = new System.Collections.Generic.List<int>();
            for (int node = 0; node < nodes.Count; node++)
                if (NodeFloat[node] < NationCount) lists[NodeFloat[node]].Add(node);
            NationNodes = new int[NationCount][];
            for (int n = 0; n < NationCount; n++) NationNodes[n] = lists[n].ToArray();
        }

        public int Count => Float.Length;

        public int ContinentOf(int purse) => _continentOfFloat[purse];

        /// <summary>Money paid into the traders' float of <paramref name="node"/> (negative: paid out of it).</summary>
        public void ChangeFloat(int node, long amount) => Float[NodeFloat[node]] += amount;

        /// <summary>Wages, fees and other money paid to the households of <paramref name="node"/> (negative: paid by them).</summary>
        public void PayHouseholds(int node, long amount) => _households.Cash[node] += amount;

        /// <summary>How much of <paramref name="value"/> the float of <paramref name="node"/> can pay a seller.</summary>
        public long Payable(int node, long value) => Math.Min(value, Math.Max(0, Float[NodeFloat[node]]));

        /// <summary>How many of <paramref name="quantity"/> units at <paramref name="price"/> the float of <paramref name="node"/> can pay for.</summary>
        public int Sellable(int node, int quantity, long price) =>
            price <= 0 ? quantity : (int)Math.Min(quantity, Math.Max(0, Float[NodeFloat[node]]) / price);

        public void Write(BinaryWriter writer) => NativeIO.WriteArray(writer, Float);

        public void Read(BinaryReader reader) => NativeIO.ReadArray(reader, Float);

        public void Dispose()
        {
            Float.DisposeIfCreated();
            NodeFloat.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Version 1 (P13a) purses held households' and traders' money together; they become the traders' floats.
    /// [Draft] Such a save gets the freshly seeded household cash of a new game, so its money grows once.
    /// </summary>
    public sealed class PurseStateV1Migration : ISaveMigration
    {
        public string SectionKey => "agents.purses";
        public int FromVersion => 1;
        public byte[] Migrate(byte[] section) => section;
    }

    /// <summary>
    /// Hands what households paid for their consumption this turn to the traders' float of their nation
    /// (DEC-061). Runs right after pricing, where the payments happen.
    /// </summary>
    public sealed class HouseholdSpendingSystem : ISimSystem
    {
        HouseholdCash _households;
        PurseState _purses;
        LaborState _labor;

        public string Id => "agents.household_spending";
        public SimPhase Phase => SimPhase.Markets;
        public int Order => SimOrder.Late + 1;

        public void Initialize(SimContext ctx)
        {
            _households = ctx.World.Get<HouseholdCash>();
            _purses = ctx.World.Get<PurseState>();
            _labor = ctx.World.Get<LaborState>();
        }

        public void Execute(SimContext ctx)
        {
            var spent = _households.Spent;
            for (int node = 0; node < spent.Length; node++)
            {
                if (spent[node] == 0) continue;
                _purses.Float[_purses.NodeFloat[node]] += spent[node];
                _labor.QuarterSpending[_purses.NodeFloat[node]] += spent[node];
                _households.SpentYear[node] += spent[node];
                spent[node] = 0;
            }
        }
    }

    /// <summary>
    /// The state spends a share of its reserve above the target as public wages, paid to the nation's
    /// households by population (DEC-061, P13c), so minted coins and collected tolls return to the economy.
    /// The player's deposits are never spent. Runs after the bank's week, before the quarterly records.
    /// </summary>
    public sealed class StateSpendingSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        NodeBanks _banks;
        BankState _bank;
        CurrencyState _currency;
        HouseholdState _population;
        HouseholdCash _households;
        PurseState _purses;
        int[] _nationsOnContinent;

        public string Id => "agents.state_spending";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Late + 5;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _bank = ctx.World.Get<BankState>();
            _currency = ctx.World.Get<CurrencyState>();
            _population = ctx.World.Get<HouseholdState>();
            _households = ctx.World.Get<HouseholdCash>();
            _purses = ctx.World.Get<PurseState>();
            _nationsOnContinent = new int[_nodes.ContinentCount];
            for (int n = 0; n < _banks.NationCount; n++) _nationsOnContinent[_nodes.Continent[_banks.NationNode(n)]]++;
        }

        public void Execute(SimContext ctx)
        {
            for (int n = 0; n < _banks.NationCount; n++)
            {
                long spending = _config.StateSpendingShare.MulInt(Math.Max(0, Excess(n)));
                if (spending > 0) PayWages(n, spending);
            }
        }

        /// <summary>The reserve above the nation's target and the player's deposits.</summary>
        public long Excess(int nation)
        {
            int continent = _nodes.Continent[_banks.NationNode(nation)];
            return MoneyLedger.StateExcess(_bank, _config, _currency, nation, continent, _nationsOnContinent[continent]);
        }

        void PayWages(int nation, long amount)
        {
            var nodes = _purses.NationNodes[nation];
            long people = 0;
            foreach (int node in nodes) people += _population.Population[node];
            if (people <= 0) return;
            _bank.Reserve[nation] -= amount;
            long given = 0;
            foreach (int node in nodes)
            {
                long wage = amount * _population.Population[node] / people;
                _households.Cash[node] += wage;
                given += wage;
            }
            _households.Cash[_banks.NationNode(nation)] += amount - given;
        }
    }

    /// <summary>
    /// Where money goes when it changes hands outside a market (DEC-061), and the conservation audit: a
    /// continent's money changes only by minting.
    /// </summary>
    public static class MoneyLedger
    {
        /// <summary>A nation's reserve above its target and the player's deposits.</summary>
        public static long StateExcess(BankState bank, SimulationConfig config, CurrencyState currency, int nation, int continent, int nationsOnContinent)
        {
            long share = config.BankInitialReserveShare.MulInt(currency.CoinSupply[continent]) / Math.Max(1, nationsOnContinent);
            return bank.Reserve[nation] - bank.PlayerDeposit[nation] - config.StateReserveTarget.MulInt(share);
        }

        public static long StateExcess(WorldState world, SimulationConfig config, int nation)
        {
            var nodes = world.Get<NodeTable>();
            var banks = world.Get<NodeBanks>();
            int continent = nodes.Continent[banks.NationNode(nation)], nations = 0;
            for (int n = 0; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent) nations++;
            return StateExcess(world.Get<BankState>(), config, world.Get<CurrencyState>(), nation, continent, nations);
        }

        /// <summary>
        /// Every coin of a continent, wherever it is, counting debts (negative cash) as they stand: the
        /// player's coins, facilities, merchant groups and interest groups on the continent, its bank
        /// reserves (deposits sit inside them), its international bank, its traders' floats and its households.
        /// </summary>
        public static long ContinentMoney(WorldState world, int continent)
        {
            var nodes = world.Get<NodeTable>();
            var banks = world.Get<NodeBanks>();
            var bank = world.Get<BankState>();
            var purses = world.Get<PurseState>();
            long money = world.Get<PlayerState>().Coins[continent] + bank.InternationalReserve[continent];
            var facilities = world.Get<FacilityTable>();
            for (int f = 0; f < facilities.Count; f++)
                if (nodes.Continent[facilities.Node[f]] == continent) money += facilities.Cash[f];
            var agents = world.Get<AgentTable>();
            for (int a = 0; a < agents.Count; a++)
                if (nodes.Continent[agents.Node[a]] == continent) money += agents.Cash[a];
            var groups = world.Get<GroupTable>();
            for (int g = 0; g < groups.Count; g++)
                if (nodes.Continent[groups.Node[g]] == continent) money += groups.Cash[g];
            for (int n = 0; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent) money += bank.Reserve[n];
            for (int p = 0; p < purses.Count; p++)
                if (purses.ContinentOf(p) == continent) money += purses.Float[p];
            var households = world.Get<HouseholdCash>();
            for (int node = 0; node < nodes.Count; node++)
                if (nodes.Continent[node] == continent) money += households.Cash[node] + households.Spent[node];
            return money;
        }

        /// <summary>
        /// Gives each continent's international bank its starting share of the coin supply, and the rest of
        /// the supply nobody else holds to its traders' floats (a share, split by the population of their
        /// nations) and its households (by population), so that the continent's money equals its coin supply
        /// (DEC-061).
        /// </summary>
        public static void Seed(WorldState world, SimulationConfig config)
        {
            var nodes = world.Get<NodeTable>();
            var purses = world.Get<PurseState>();
            var population = world.Get<HouseholdState>().Population;
            var households = world.Get<HouseholdCash>();
            var currency = world.Get<CurrencyState>();
            var bank = world.Get<BankState>();
            // Traders work in nations; a continent's own float (nodes above nation level, where little changes
            // hands) starts empty unless the continent has no nations.
            var floatPopulation = new long[purses.Count];
            for (int node = 0; node < nodes.Count; node++) floatPopulation[purses.NodeFloat[node]] += population[node];
            for (int p = purses.NationCount; p < purses.Count; p++)
                for (int n = 0; n < purses.NationCount; n++)
                    if (purses.ContinentOf(n) == purses.ContinentOf(p)) floatPopulation[p] = 0;

            for (int c = 0; c < currency.ContinentCount; c++)
            {
                bank.InternationalReserve[c] += config.InternationalBankReserveShare.MulInt(currency.CoinSupply[c]);
                long rest = currency.CoinSupply[c] - ContinentMoney(world, c);
                long floats = config.TraderFloatShare.MulInt(rest);
                Split(floats, purses.Count, p => purses.ContinentOf(p) == c ? floatPopulation[p] : 0, purses.Float);
                Split(rest - floats, nodes.Count, node => nodes.Continent[node] == c ? population[node] : 0, households.Cash);
            }
            households.StartYear();
        }

        // Adds `amount` over the entries with weight, in proportion; the rounding remainder goes to the first.
        static void Split(long amount, int count, Func<int, long> weight, NativeArray<long> into)
        {
            long total = 0;
            int first = -1;
            for (int i = 0; i < count; i++)
            {
                long w = weight(i);
                if (w <= 0) continue;
                total += w;
                if (first < 0) first = i;
            }
            if (first < 0) return;
            long given = 0;
            for (int i = 0; i < count; i++)
            {
                long w = weight(i);
                if (w <= 0) continue;
                long share = amount * w / total;
                into[i] += share;
                given += share;
            }
            into[first] += amount - given;
        }

        /// <summary>
        /// Money for the households around <paramref name="node"/>: its nation's, by population, or the node's
        /// own outside any nation. Negative amounts are charged the same way.
        /// </summary>
        public static void PayLocalHouseholds(WorldState world, int node, long amount)
        {
            var purses = world.Get<PurseState>();
            int nation = purses.NodeFloat[node];
            if (nation < purses.NationCount) PayNationHouseholds(world, nation, amount);
            else purses.PayHouseholds(node, amount);
        }

        /// <summary>Money for a nation's households, split by the population of its nodes (DEC-061).</summary>
        public static void PayNationHouseholds(WorldState world, int nation, long amount)
        {
            var nodes = world.Get<PurseState>().NationNodes[nation];
            var population = world.Get<HouseholdState>().Population;
            var cash = world.Get<HouseholdCash>().Cash;
            long people = 0;
            int first = -1;
            foreach (int node in nodes)
            {
                if (population[node] <= 0) continue;
                people += population[node];
                if (first < 0) first = node;
            }
            if (first < 0) return;
            long given = 0;
            foreach (int node in nodes)
            {
                if (population[node] <= 0) continue;
                long share = amount * population[node] / people;
                cash[node] += share;
                given += share;
            }
            cash[first] += amount - given;
        }

        /// <summary>
        /// Toll and license money (DEC-061): the bank reserve of the nation of <paramref name="node"/>, or
        /// its continent's international bank outside any nation.
        /// </summary>
        public static void ToState(WorldState world, int node, long amount)
        {
            var nodes = world.Get<NodeTable>();
            int nationNode = nodes.AncestorAt(node, WorldLevel.Nation);
            var bank = world.Get<BankState>();
            if (nationNode >= 0) bank.Reserve[world.Get<NodeBanks>().NationIndex(nationNode)] += amount;
            else bank.InternationalReserve[nodes.Continent[node]] += amount;
        }

        /// <summary>
        /// Moves value between continents through their international banks when a payment in one coin settles
        /// a debt in another: the paying continent's bank takes <paramref name="paid"/>, the receiving
        /// continent's bank pays out <paramref name="received"/>.
        /// </summary>
        public static void AcrossContinents(BankState bank, int fromContinent, long paid, int toContinent, long received)
        {
            if (fromContinent == toContinent) return;
            bank.InternationalReserve[fromContinent] += paid;
            bank.InternationalReserve[toContinent] -= received;
        }

        /// <summary>
        /// A new operator takes over an asset at <paramref name="node"/> with <paramref name="cash"/> (DEC-061):
        /// the old operator's cash settles with the nation's households ([Draft]: arrears are wages and bills
        /// they never get; anything left goes with the old operator to them), and the starting cash comes from
        /// the owning group's cash, then from those households. Spread over the nation (P13d), so one city's
        /// households do not carry every failure of the firms based there.
        /// </summary>
        public static long FundNewOperator(WorldState world, int group, int node, long oldCash, long cash)
        {
            PayLocalHouseholds(world, node, oldCash);
            PayFromGroup(world, group, node, cash);
            return cash;
        }

        /// <summary>A group pays <paramref name="amount"/> from its cash; what it lacks comes from the households around <paramref name="node"/>.</summary>
        public static void PayFromGroup(WorldState world, int group, int node, long amount)
        {
            long fromGroup = 0;
            if (group >= 0)
            {
                var groups = world.Get<GroupTable>();
                fromGroup = Math.Min(amount, Math.Max(0, groups.Cash[group]));
                groups.Cash[group] -= fromGroup;
            }
            if (amount > fromGroup) PayLocalHouseholds(world, node, -(amount - fromGroup));
        }

        /// <summary>
        /// Insurance premiums go to a guild of the trip's origin city (DEC-061): [Draft] the lowest-numbered
        /// one, or the households there when the city has none. Returns the guild, or -1.
        /// </summary>
        public static int InsuranceGuild(WorldState world, int originNode)
        {
            var groups = world.Get<GroupTable>();
            int city = new GroupDirectory(groups, world.Get<NodeTable>()).CityOf(originNode);
            if (city < 0) return -1;
            for (int g = 0; g < groups.Count; g++)
                if (groups.KindOf(g) == GroupKind.Guild && groups.Node[g] == city) return g;
            return -1;
        }
    }
}
