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
    /// The money of each nation's households and traders taken together (DEC-061, P13a): one purse per
    /// nation, in its continent's coin, plus one per continent for nodes outside any nation. Markets pay
    /// sellers out of the purse where the seller is and buyers pay into it; wages, fees and spending land
    /// in it. P13b splits it into household cash per node and traders' floats.
    /// </summary>
    public sealed class PurseState : IPersistentComponent, IDisposable
    {
        public readonly int NationCount;
        public readonly int ContinentCount;
        /// <summary>Per purse: nations first, then one per continent. May go below zero until P13b caps payouts.</summary>
        public NativeArray<long> Purse;
        /// <summary>Per node: its purse. Static, rebuilt from the map.</summary>
        public NativeArray<int> NodePurse;
        readonly int[] _continentOfPurse;

        public string Key => "agents.purses";
        public int Version => 1;

        public PurseState(NodeTable nodes, NodeBanks banks)
        {
            NationCount = banks.NationCount;
            ContinentCount = nodes.ContinentCount;
            Purse = new NativeArray<long>(NationCount + ContinentCount, Allocator.Persistent);
            NodePurse = new NativeArray<int>(nodes.Count, Allocator.Persistent);
            _continentOfPurse = new int[NationCount + ContinentCount];
            for (int n = 0; n < NationCount; n++) _continentOfPurse[n] = nodes.Continent[banks.NationNode(n)];
            for (int c = 0; c < ContinentCount; c++) _continentOfPurse[NationCount + c] = c;
            for (int node = 0; node < nodes.Count; node++)
            {
                int nationNode = nodes.AncestorAt(node, WorldLevel.Nation);
                NodePurse[node] = nationNode >= 0 ? banks.NationIndex(nationNode) : NationCount + nodes.Continent[node];
            }
        }

        public int Count => Purse.Length;

        public int ContinentOf(int purse) => _continentOfPurse[purse];

        /// <summary>Money paid into the purse of <paramref name="node"/> (negative: paid out of it).</summary>
        public void Change(int node, long amount) => Purse[NodePurse[node]] += amount;

        public void Write(BinaryWriter writer) => NativeIO.WriteArray(writer, Purse);

        public void Read(BinaryReader reader) => NativeIO.ReadArray(reader, Purse);

        public void Dispose()
        {
            Purse.DisposeIfCreated();
            NodePurse.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Where money goes when it changes hands outside a market (DEC-061), and the conservation audit: a
    /// continent's money changes only by minting.
    /// </summary>
    public static class MoneyLedger
    {
        /// <summary>
        /// Every coin of a continent, wherever it is, counting debts (negative cash) as they stand: the
        /// player's coins, facilities, merchant groups and interest groups on the continent, its bank
        /// reserves (deposits sit inside them), its international bank and its purses.
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
                if (purses.ContinentOf(p) == continent) money += purses.Purse[p];
            return money;
        }

        /// <summary>
        /// Gives each continent's international bank its starting share of the coin supply and its purses the
        /// rest of the supply nobody else holds, split by population, so that the continent's money equals
        /// its coin supply (DEC-061).
        /// </summary>
        public static void Seed(WorldState world, SimulationConfig config)
        {
            var nodes = world.Get<NodeTable>();
            var purses = world.Get<PurseState>();
            var households = world.Get<HouseholdState>();
            var currency = world.Get<CurrencyState>();
            var bank = world.Get<BankState>();
            // Nations' people hold the money; a continent's own purse (nodes above nation level, where little
            // changes hands) starts empty unless the continent has no nations.
            var population = new long[purses.Count];
            for (int node = 0; node < nodes.Count; node++) population[purses.NodePurse[node]] += households.Population[node];
            for (int p = purses.NationCount; p < purses.Count; p++)
            {
                int c = purses.ContinentOf(p);
                for (int n = 0; n < purses.NationCount; n++)
                    if (purses.ContinentOf(n) == c) population[p] = 0;
            }
            for (int c = 0; c < currency.ContinentCount; c++)
            {
                bank.InternationalReserve[c] += config.InternationalBankReserveShare.MulInt(currency.CoinSupply[c]);
                long rest = currency.CoinSupply[c] - ContinentMoney(world, c);
                long people = 0;
                int first = -1;
                for (int p = 0; p < purses.Count; p++)
                {
                    if (purses.ContinentOf(p) != c) continue;
                    people += population[p];
                    if (first < 0) first = p;
                }
                if (first < 0) continue;
                long given = 0;
                for (int p = 0; p < purses.Count; p++)
                {
                    if (purses.ContinentOf(p) != c || people == 0) continue;
                    long share = rest * population[p] / people;
                    purses.Purse[p] += share;
                    given += share;
                }
                purses.Purse[first] += rest - given;
            }
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
        /// the old operator's cash settles with the purse there ([Draft]: arrears are wages and bills the
        /// households never get; anything left goes with the old operator to them), and the starting cash
        /// comes from the owning group's cash, then from the purse.
        /// </summary>
        public static long FundNewOperator(WorldState world, int group, int node, long oldCash, long cash)
        {
            world.Get<PurseState>().Change(node, oldCash);
            PayFromGroup(world, group, node, cash);
            return cash;
        }

        /// <summary>A group pays <paramref name="amount"/> from its cash; what it lacks comes out of the purse at <paramref name="node"/>.</summary>
        public static void PayFromGroup(WorldState world, int group, int node, long amount)
        {
            long fromGroup = 0;
            if (group >= 0)
            {
                var groups = world.Get<GroupTable>();
                fromGroup = Math.Min(amount, Math.Max(0, groups.Cash[group]));
                groups.Cash[group] -= fromGroup;
            }
            world.Get<PurseState>().Change(node, -(amount - fromGroup));
        }

        /// <summary>
        /// Insurance premiums go to a guild of the trip's origin city (DEC-061): [Draft] the lowest-numbered
        /// one, or the purse there when the city has none. Returns the guild, or -1.
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
