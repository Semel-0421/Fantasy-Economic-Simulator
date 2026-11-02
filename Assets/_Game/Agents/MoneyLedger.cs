using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>
    /// Where money goes when it changes hands outside a market (DEC-061), and the conservation audit: a
    /// continent's money changes only by minting.
    /// </summary>
    public static class MoneyLedger
    {
        /// <summary>Where a merchant group's dues are settled: where it is, or its home while abroad, since its coins are its home continent's.</summary>
        public static int MerchantSettlesAt(NodeTable nodes, AgentTable agents, int agent) =>
            nodes.Continent[agents.Node[agent]] == nodes.Continent[agents.Home[agent]] ? agents.Node[agent] : agents.Home[agent];

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
                if (nodes.Continent[agents.Home[a]] == continent) money += agents.Cash[a];
            var groups = world.Get<GroupTable>();
            for (int g = 0; g < groups.Count; g++)
                if (nodes.Continent[groups.Node[g]] == continent) money += groups.Cash[g];
            for (int n = 0; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent) money += bank.Reserve[n];
            for (int p = 0; p < purses.Count; p++)
                if (purses.ContinentOf(p) == continent) money += purses.Float[p];
            var households = world.Get<HouseholdCash>();
            var houses = world.Get<NobleState>();
            for (int node = 0; node < nodes.Count; node++)
                if (nodes.Continent[node] == continent) money += households.Cash[node] + households.Spent[node] + houses.Cash[node];
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

        // Government sits at the core towns, and pays there (DEC-104).
        static Fx WageWeight(NodeTable levels, SimulationConfig config, int node) =>
            config.LevelWageWeight[levels.Level[node]] * (Fx.One + Fx.FromInt(levels.CoreStrength[node]) * config.CoreWagePerPoint);

        /// <summary>Wages for a nation's households: by population, weighted so towns earn more per head (DEC-076).</summary>
        public static void PayNationWages(WorldState world, SimulationConfig config, int nation, long amount)
        {
            var levels = world.Get<NodeTable>();
            var nodes = world.Get<PurseState>().NationNodes[nation];
            var population = world.Get<HouseholdState>().Population;
            var cash = world.Get<HouseholdCash>().Cash;
            long total = 0;
            int first = -1;
            foreach (int node in nodes)
            {
                if (population[node] <= 0) continue;
                total += WageWeight(levels, config, node).MulInt(population[node]);
                if (first < 0) first = node;
            }
            if (first < 0) return;
            long given = 0;
            foreach (int node in nodes)
            {
                if (population[node] <= 0) continue;
                long share = amount / total * WageWeight(levels, config, node).MulInt(population[node])
                    + amount % total * WageWeight(levels, config, node).MulInt(population[node]) / total;
                cash[node] += share;
                given += share;
            }
            cash[first] += amount - given;
        }

        /// <summary>Public wages (DEC-082): to a nation's towns by how far their cash falls short of the necessities they would buy; by the usual wage weights if none does.</summary>
        public static void PayNationTownWages(WorldState world, SimulationConfig config, int nation, long amount)
        {
            var levels = world.Get<NodeTable>();
            var nodes = world.Get<PurseState>().NationNodes[nation];
            var households = world.Get<HouseholdCash>();
            long total = 0;
            int first = -1;
            foreach (int node in nodes)
            {
                long gap = TownShortfall(levels, households, config, node);
                if (gap <= 0) continue;
                total += gap;
                if (first < 0) first = node;
            }
            if (first < 0)
            {
                PayNationWages(world, config, nation, amount);
                return;
            }
            long given = 0;
            foreach (int node in nodes)
            {
                long gap = TownShortfall(levels, households, config, node);
                if (gap <= 0) continue;
                long share = amount / total * gap + amount % total * gap / total;
                households.Cash[node] += share;
                given += share;
            }
            households.Cash[first] += amount - given;
        }

        /// <summary>How far a town's weekly budget falls short of the necessities it would buy.</summary>
        public static long TownShortfall(NodeTable levels, HouseholdCash households, SimulationConfig config, int node) =>
            levels.LevelOf(node) == WorldLevel.Village ? 0
                : households.DesiredNecessity[node] - Math.Max(0, households.Cash[node]) / Math.Max(1, config.HouseholdBudgetWeeks);

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

        /// <summary>The family at a node, to take over an asset there. One set up now starts with the coins of the households around, so none appear (DEC-107).</summary>
        public static int FamilyAt(WorldState world, GroupDirectory directory, SimulationConfig config, int node)
        {
            var groups = world.Get<GroupTable>();
            int before = groups.Count;
            int family = directory.PrivateAt(node, config);
            if (groups.Count > before) PayLocalHouseholds(world, node, -groups.Cash[family]);
            return family;
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
