using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public sealed class NobleTests
    {
        static int Village(FES.Simulation.GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            var state = sim.World.Get<NobleState>();
            return Enumerable.Range(0, nodes.Count).First(n => nodes.LevelOf(n) == WorldLevel.Village
                && nodes.AncestorAt(n, WorldLevel.City) >= 0
                && state.CityHolder[nodes.AncestorAt(n, WorldLevel.City)] == nodes.AncestorAt(n, WorldLevel.City));
        }

        [Test]
        public void Tax_RoyalHouseAndNoblesTakeTheirSharesBeforeTheState()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var config = sim.Database.Config;
            var state = sim.World.Get<NobleState>();
            var bank = sim.World.Get<BankState>();
            int village = Village(sim);
            int city = nodes.AncestorAt(village, WorldLevel.City), nation = nodes.AncestorAt(village, WorldLevel.Nation);
            int index = sim.World.Get<NodeBanks>().NationIndex(nation);
            long reserve = bank.Reserve[index];

            Nobles.CollectTax(sim.World, config, village, 1000);

            Assert.AreEqual(config.NobleRoyalShare.MulInt(1000), state.Cash[nation]);
            Assert.AreEqual(config.NobleCityShare.MulInt(1000), state.Cash[city]);
            Assert.AreEqual(config.NobleVillageShare.MulInt(1000), state.Cash[village]);
            Assert.AreEqual(1000 - state.Cash[nation] - state.Cash[city] - state.Cash[village], bank.Reserve[index] - reserve);
        }

        [Test]
        public void RoyalDomain_HasNoCityNoble()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var state = sim.World.Get<NobleState>();
            var banks = sim.World.Get<NodeBanks>();
            for (int n = 0; n < banks.NationCount; n++)
            {
                int nation = banks.NationNode(n);
                int domains = Enumerable.Range(0, nodes.Count).Count(c => state.CityHolder[c] == nation);
                Assert.AreEqual(1, domains);
            }
        }

        [Test]
        public void LordWhoCannotProtect_LosesAidForTheYear()
        {
            using var sim = TestWorlds.BuildSample();
            var state = sim.World.Get<NobleState>();
            int village = Village(sim);
            int lord = state.Lord[village];
            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            state.ProtectionOwed[village] = long.MaxValue / 2;
            state.Cash[village] = 1000;
            sim.AdvanceTurn();
            Assert.IsFalse(state.ContractHolds[village]);
            Assert.AreEqual(0, state.ProtectionOwed[village]);
            Assert.Greater(state.Lord[village], -1);
            Assert.AreEqual(lord, state.Lord[village]);
        }

        [Test]
        public void Houses_KeepMoneyConserved()
        {
            using var sim = TestWorlds.BuildSample();
            for (int t = 0; t < 2 * SimClock.WeeksPerYear + 5; t++) sim.AdvanceTurn();
            var state = sim.World.Get<NobleState>();
            Assert.Greater(state.Cash.Sum(), 0);
            var currency = sim.World.Get<FES.Economy.CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }
    }
}
