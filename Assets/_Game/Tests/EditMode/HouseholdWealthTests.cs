using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>The rich pay for the hungry's necessities, buy more luxuries the more they hold, and pay a tax on them (DEC-127).</summary>
    public class HouseholdWealthTests
    {
        const long Rich = 1_000_000;

        static (GameSimulation sim, HouseholdCash cash, int node) Node()
        {
            var sim = TestWorlds.BuildSample();
            var cash = sim.World.Get<HouseholdCash>();
            int node = Enumerable.Range(0, cash.Cash.Length).First(n => sim.World.Get<HouseholdState>().Population[n] > 0);
            return (sim, cash, node);
        }

        // Only the top quintile holds money; the week's necessities cost 1,000.
        static void OnlyTheRichHold(HouseholdCash cash, int node, long top)
        {
            for (int q = 0; q < 4; q++) cash.QuintileCash[node * 5 + q] = 0;
            cash.QuintileCash[node * 5 + 4] = top;
            cash.Cash[node] = top;
            cash.DesiredNecessity[node] = 1_000;
        }

        [Test]
        public void TheRich_PayForTheHungrysNecessities()
        {
            var (sim, cash, node) = Node();
            using (sim)
            {
                OnlyTheRichHold(cash, node, Rich);
                Assert.AreEqual(Fx.OneRaw / 5, cash.NecessityBudgetFactor(node, 26, 0), 2, "without alms only the top fifth buys");
                Assert.AreEqual(Fx.OneRaw, cash.NecessityBudgetFactor(node, 26, Fx.FromPermille(20).Raw), "with alms all of it");
            }
        }

        [Test]
        public void Alms_GoNoFurtherThanTheShareOfWhatTheRichHold()
        {
            var (sim, cash, node) = Node();
            using (sim)
            {
                OnlyTheRichHold(cash, node, 10_000);
                // The top's own fifth, 200, from its week's budget; alms at most 2% of 10,000 = 200 of the other 800.
                long factor = cash.NecessityBudgetFactor(node, 26, Fx.FromPermille(20).Raw);
                Assert.AreEqual(Fx.FromRatio(400, 1_000).Raw, factor, 2);
            }
        }

        [Test]
        public void LuxuryDemand_GrowsWithWhatTheRichHold_UpToTheCap()
        {
            var (sim, cash, node) = Node();
            using (sim)
            {
                var per = Fx.FromPermille(1_000).Raw;
                var cap = Fx.FromInt(4).Raw;
                cash.QuintileCash[node * 5 + 4] = 0;
                Assert.AreEqual(Fx.OneRaw, cash.LuxuryWealthMul(node, 1_000, per, cap), "nothing held, nothing more");
                cash.QuintileCash[node * 5 + 4] = 52 * 1_000; // a year of the node's income
                Assert.AreEqual(2 * Fx.OneRaw, cash.LuxuryWealthMul(node, 1_000, per, cap), 2);
                cash.QuintileCash[node * 5 + 4] = 52 * 1_000 * 100;
                Assert.AreEqual(cap, cash.LuxuryWealthMul(node, 1_000, per, cap));
            }
        }

        [Test]
        public void LuxuryPurchases_PayATax_NecessitiesNone()
        {
            long rate = Fx.FromPermille(100).Raw;
            Assert.AreEqual(0, HouseholdCash.LuxuryTax(800, 1_000, rate), "all of it necessities");
            Assert.AreEqual(50, HouseholdCash.LuxuryTax(1_500, 1_000, rate), "a tenth of the 500 beyond them");
        }

        [Test]
        public void TheLuxuryTax_GoesToTheState_AndKeepsTheCoins()
        {
            using var sim = TestWorlds.BuildSample();
            var currency = sim.World.Get<CurrencyState>();
            for (int t = 0; t < 10; t++) sim.AdvanceTurn();
            Assert.Greater(sim.World.Get<HouseholdCash>().LuxuryTaxYear, 0, "some luxuries were bought and taxed");
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }
    }
}
