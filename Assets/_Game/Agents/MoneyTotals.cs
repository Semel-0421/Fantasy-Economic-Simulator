using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Money held in each nation and continent (DEC-042): cash of the player, merchant groups, facilities
    /// and interest groups, counted where it is now, plus bank reserves. Deposits already sit in the
    /// reserves, so they are shown but not added again; loan balances are not money. Recomputed from the
    /// tables when a quarter is recorded or someone looks, so not saved.
    /// </summary>
    public sealed class MoneyTotals : IWorldComponent
    {
        /// <summary>Per nation index, in its continent's coin.</summary>
        public readonly long[] NationCash;
        public readonly long[] NationReserve;
        public readonly long[] NationDeposits;
        /// <summary>Per continent: every nation's total, the international bank, and coins held outside any nation.</summary>
        public readonly long[] ContinentTotal;
        /// <summary>Per continent: coins of that continent held outside its nations (carried abroad, or at nodes above nation level).</summary>
        public readonly long[] ContinentLoose;

        /// <summary>Per node: the nation index it lies in, or -1. Built on first use; the map is static.</summary>
        int[] _nationOfNode;
        int _computedTurn = -1;

        public MoneyTotals(int nationCount, int continentCount)
        {
            NationCash = new long[nationCount];
            NationReserve = new long[nationCount];
            NationDeposits = new long[nationCount];
            ContinentTotal = new long[continentCount];
            ContinentLoose = new long[continentCount];
        }

        public long NationTotal(int nation) => NationCash[nation] + NationReserve[nation];

        /// <summary>A load may bring a save of the same turn.</summary>
        public void Invalidate() => _computedTurn = -1;

        /// <summary>Recomputes unless already done this turn.</summary>
        public void Refresh(WorldState world)
        {
            if (_computedTurn != world.Get<SimClock>().Turn) Recompute(world);
        }

        public void Recompute(WorldState world)
        {
            _computedTurn = world.Get<SimClock>().Turn;
            var nodes = world.Get<NodeTable>();
            var banks = world.Get<NodeBanks>();
            var bank = world.Get<BankState>();
            if (_nationOfNode == null)
            {
                _nationOfNode = new int[nodes.Count];
                for (int node = 0; node < nodes.Count; node++)
                    _nationOfNode[node] = banks.NationOf(node);
            }
            Array.Clear(NationCash, 0, NationCash.Length);
            Array.Clear(ContinentLoose, 0, ContinentLoose.Length);

            var player = world.Get<PlayerState>();
            for (int c = 0; c < player.Coins.Length; c++)
                AddCash(nodes, player.Node, c, player.Coins[c]);

            var facilities = world.Get<FacilityTable>();
            AddCash(nodes, facilities.Node.AsArray(), facilities.Cash.AsArray());
            var agents = world.Get<AgentTable>();
            for (int a = 0; a < agents.Count; a++) // a group's coins are its home continent's, wherever it is
                AddCash(nodes, agents.Node[a], nodes.Continent[agents.Home[a]], agents.Cash[a]);
            var groups = world.Get<GroupTable>();
            AddCash(nodes, groups.Node.AsArray(), groups.Cash.AsArray());

            // Traders' floats and households' cash are cash held there too (DEC-061).
            var purses = world.Get<PurseState>();
            for (int p = 0; p < purses.Count; p++)
            {
                if (purses.Float[p] <= 0) continue;
                if (p < NationCash.Length) NationCash[p] += purses.Float[p];
                else ContinentLoose[purses.ContinentOf(p)] += purses.Float[p];
            }
            var households = world.Get<HouseholdCash>();
            for (int node = 0; node < nodes.Count; node++)
                AddCash(nodes, node, nodes.Continent[node], households.Cash[node] + households.Spent[node]);

            Array.Copy(bank.Reserve, NationReserve, NationReserve.Length);
            Array.Copy(bank.PlayerDeposit, NationDeposits, NationDeposits.Length);
            for (int c = 0; c < ContinentTotal.Length; c++) ContinentTotal[c] = ContinentLoose[c] + bank.InternationalReserve[c];
            for (int n = 0; n < NationCash.Length; n++) ContinentTotal[nodes.Continent[banks.NationNode(n)]] += NationTotal(n);
        }

        // Each holder's cash in its node's continent coin.
        void AddCash(NodeTable nodes, Unity.Collections.NativeArray<int> node, Unity.Collections.NativeArray<long> cash)
        {
            for (int i = 0; i < node.Length; i++)
            {
                if (cash[i] <= 0) continue;
                int at = node[i];
                AddCash(nodes, at, nodes.Continent[at], cash[i]);
            }
        }

        // Negative cash (upkeep arrears) is owed to nobody, so it is no money.
        void AddCash(NodeTable nodes, int node, int continent, long amount)
        {
            if (amount <= 0) return;
            int nation = _nationOfNode[node];
            if (nation >= 0 && nodes.Continent[node] == continent) NationCash[nation] += amount;
            else ContinentLoose[continent] += amount;
        }
    }
}
