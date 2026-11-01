using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class TraderLoanTests
    {
        static (int node, int nation) Town(FES.Simulation.GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            var purses = sim.World.Get<PurseState>();
            int node = purses.NationNodes[0].First(n => nodes.LevelOf(n) == WorldLevel.City);
            return (node, 0);
        }

        static void Run(FES.Simulation.GameSimulation sim) => sim.Systems.OfType<TraderLoanSystem>().Single().Execute(sim.Context);

        static long Money(FES.Simulation.GameSimulation sim)
        {
            long money = 0;
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++) money += MoneyLedger.ContinentMoney(sim.World, c);
            return money;
        }

        [Test]
        public void Households_LendOnlyWhatTheyCanSpare()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var (node, nation) = Town(sim);
            var cash = sim.World.Get<HouseholdCash>();
            var loans = sim.World.Get<TraderLoans>();
            var purses = sim.World.Get<PurseState>();
            config.TraderLoanShare = Fx.FromPermille(100);
            config.TraderLoanKeepWeeks = 26;
            cash.Desired[node] = 100;
            cash.Cash[node] = 2_600;
            long floatBefore = purses.Float[nation];
            Run(sim);
            Assert.AreEqual(0, loans.OwedHouseholds[node], "a half year of spending is kept");
            cash.Cash[node] = 12_600;
            long money = Money(sim);
            Run(sim);
            Assert.AreEqual(1_000, loans.OwedHouseholds[node]);
            Assert.AreEqual(11_600, cash.Cash[node]);
            Assert.AreEqual(money, Money(sim));
            Assert.Greater(purses.Float[nation], floatBefore);
        }

        [Test]
        public void Traders_PayInterestAndAPart_WithinAShareOfTheFloat()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var (node, nation) = Town(sim);
            var cash = sim.World.Get<HouseholdCash>();
            var loans = sim.World.Get<TraderLoans>();
            var purses = sim.World.Get<PurseState>();
            config.TraderLoanShare = Fx.Zero; // repayment alone
            config.TraderLoanInterest = Fx.FromPermille(520);
            config.TraderLoanWeeks = 10;
            config.TraderLoanRepayShare = Fx.FromPermille(500);
            loans.OwedHouseholds[node] = 100_000;
            cash.Cash[node] = 0;
            purses.Float[nation] = 1_000_000;
            long money = Money(sim);
            long interest = config.TraderLoanInterest.MulInt(100_000) / 52;
            Run(sim);
            Assert.AreEqual(90_000, loans.OwedHouseholds[node], "a tenth falls due");
            Assert.AreEqual(0, loans.InterestHouseholds[node]);
            Assert.AreEqual(10_000 + interest, cash.Cash[node]);
            Assert.AreEqual(money, Money(sim));

            purses.Float[nation] = 2_000;
            cash.Cash[node] = 0;
            long second = config.TraderLoanInterest.MulInt(90_000) / 52;
            Assert.Less(second, 1_000);
            Run(sim);
            Assert.AreEqual(1_000, cash.Cash[node], "half the float is all it may take");
            Assert.AreEqual(1_000, purses.Float[nation]);
            Assert.AreEqual(0, loans.InterestHouseholds[node], "interest comes first");
            Assert.AreEqual(90_000 - (1_000 - second), loans.OwedHouseholds[node]);

            purses.Float[nation] = -500;
            cash.Cash[node] = 0;
            long owed = loans.OwedHouseholds[node];
            Run(sim);
            Assert.AreEqual(0, cash.Cash[node], "an empty float pays nothing");
            Assert.AreEqual(owed, loans.OwedHouseholds[node], "interest does not compound");
            Assert.Greater(loans.InterestHouseholds[node], 0);
        }

        [Test]
        public void Households_StopLending_WhileTradersAreBehind()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var (node, nation) = Town(sim);
            var cash = sim.World.Get<HouseholdCash>();
            var loans = sim.World.Get<TraderLoans>();
            var purses = sim.World.Get<PurseState>();
            config.TraderLoanShare = Fx.FromPermille(100);
            config.TraderLoanKeepWeeks = 0;
            cash.Cash[node] = 100_000;
            loans.OwedHouseholds[node] = 1_000_000;
            purses.Float[nation] = 0;
            Run(sim);
            Assert.AreEqual(1_000_000, loans.OwedHouseholds[node]);
            Assert.AreEqual(100_000, cash.Cash[node]);
            purses.Float[nation] = 10_000_000;
            long part = 1_000_000 / config.TraderLoanWeeks;
            Run(sim);
            Assert.Greater(loans.OwedHouseholds[node], 1_000_000 - part, "paying up opens lending again");
        }

        [Test]
        public void Lending_IsNotIncome_ButPaidInterestIs()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var (node, nation) = Town(sim);
            var cash = sim.World.Get<HouseholdCash>();
            var loans = sim.World.Get<TraderLoans>();
            var purses = sim.World.Get<PurseState>();
            config.TraderLoanShare = Fx.FromPermille(100);
            config.TraderLoanKeepWeeks = 0;
            config.TraderLoanInterest = Fx.FromPermille(520);
            config.TraderLoanWeeks = 10;
            cash.Cash[node] = 100_000;
            cash.StartYear();
            long income = cash.YearIncome(node);
            Run(sim);
            Assert.AreEqual(income, cash.YearIncome(node), "lending moves money, it does not lose it");
            config.TraderLoanShare = Fx.Zero;
            long owed = loans.OwedHouseholds[node], interest = config.TraderLoanInterest.MulInt(owed) / 52;
            purses.Float[nation] = 0;
            Run(sim);
            Assert.AreEqual(income, cash.YearIncome(node), "unpaid interest is not income yet");
            purses.Float[nation] = 10_000_000;
            long interestTwo = interest + config.TraderLoanInterest.MulInt(owed) / 52;
            Run(sim);
            Assert.AreEqual(income + interestTwo, cash.YearIncome(node));
        }

        [Test]
        public void Bank_LendsItsExcess_AndIsPaidBack()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var bank = sim.World.Get<BankState>();
            var loans = sim.World.Get<TraderLoans>();
            var purses = sim.World.Get<PurseState>();
            config.TraderBankLoanShare = Fx.FromPermille(100);
            bank.Reserve[0] += 1_000_000;
            long money = Money(sim), reserve = bank.Reserve[0];
            long excess = MoneyLedger.StateExcess(sim.World, config, 0);
            Run(sim);
            Assert.AreEqual(config.TraderBankLoanShare.MulInt(excess), loans.OwedBank[0]);
            Assert.AreEqual(reserve - loans.OwedBank[0], bank.Reserve[0]);
            Assert.AreEqual(money, Money(sim));
            config.TraderBankLoanShare = Fx.Zero;
            purses.Float[0] = 10_000_000;
            money = Money(sim);
            long lower = bank.Reserve[0];
            Run(sim);
            Assert.Greater(bank.Reserve[0], lower);
            Assert.AreEqual(money, Money(sim));
        }
    }
}
