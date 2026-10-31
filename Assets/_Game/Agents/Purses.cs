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
        public int Order => FinanceOrder.StateSpending;

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
}
