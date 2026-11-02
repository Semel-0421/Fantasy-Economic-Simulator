using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public sealed class MigrationTests
    {
        static long Rural(FES.Simulation.GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            var people = sim.World.Get<HouseholdState>().Population;
            return Enumerable.Range(0, nodes.Count).Where(n => nodes.LevelOf(n) == WorldLevel.Village).Sum(n => people[n]);
        }

        static long Total(FES.Simulation.GameSimulation sim) => sim.World.Get<HouseholdState>().Population.Sum();

        [Test]
        public void Villagers_MoveToTowns_AndMoneyStaysConserved()
        {
            using var sim = TestWorlds.BuildSample();
            long share = Rural(sim) * 1000 / Total(sim);
            for (int t = 0; t < 5 * SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.Less(Rural(sim) * 1000 / Total(sim), share);
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }

        [Test]
        public void MigrantsSavings_AreNoIncome()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var cash = sim.World.Get<HouseholdCash>();
            var people = sim.World.Get<HouseholdState>().Population;
            sim.AdvanceTurn();
            while (!sim.Context.Clock.IsNewYear) sim.AdvanceTurn();
            for (int n = 0; n < nodes.Count; n++)
                if (nodes.LevelOf(n) != WorldLevel.Village) cash.Cash[n] += 100_000_000; // towns pay better, so people move
            var income = Enumerable.Range(0, nodes.Count).Select(n => cash.YearIncome(n)).ToArray();
            var held = Enumerable.Range(0, nodes.Count).Select(n => cash.Cash[n]).ToArray();

            sim.Systems.OfType<MigrationSystem>().Single().Execute(sim.Context);

            Assert.IsTrue(Enumerable.Range(0, nodes.Count).Any(n => cash.Cash[n] != held[n]), "savings moved");
            CollectionAssert.AreEqual(income, Enumerable.Range(0, nodes.Count).Select(n => cash.YearIncome(n)).ToArray());
        }

        [Test]
        public void Towns_AreFree_VillagesStartPartlyBound()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var state = sim.World.Get<MigrationState>();
            for (int n = 0; n < nodes.Count; n++)
                if (nodes.LevelOf(n) == WorldLevel.Village) Assert.Less(state.FreeShare[n], Fx.OneRaw);
                else Assert.AreEqual(Fx.OneRaw, state.FreeShare[n]);
        }

        [Test]
        public void TownsShortOfWorkers_BringEmancipation()
        {
            using var sim = TestWorlds.BuildSample();
            bool enacted = false;
            for (int t = 0; t < 10 * SimClock.WeeksPerYear && !enacted; t++)
            {
                sim.AdvanceTurn();
                enacted = sim.LastTurnEvents.OfType<EmancipationEnacted>().Any();
            }
            Assert.IsTrue(enacted);
        }
    }
}
