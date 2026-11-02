using FES.Core;
using FES.Economy;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Households hold and spend by income quintile (DEC-115): the poorest go short while the top still buys.</summary>
    public class HouseholdQuintileTests
    {
        const int Weeks = 1;

        static readonly Fx[] Shares = { Fx.FromPermille(20), Fx.FromPermille(30), Fx.FromPermille(50), Fx.FromPermille(100), Fx.FromPermille(800) };

        static HouseholdCash Town(long cash)
        {
            var town = new HouseholdCash(1, 0);
            town.Cash[0] = cash;
            town.Settle(0, Shares);
            return town;
        }

        static long Sum(HouseholdCash town)
        {
            long sum = 0;
            for (int q = 0; q < HouseholdDemand.Quintiles; q++) sum += town.QuintileCash[q];
            return sum;
        }

        [Test]
        public void Income_IsSharedByIncomeShare()
        {
            var town = Town(1000);
            Assert.AreEqual(20, town.QuintileCash[0]);
            Assert.AreEqual(800, town.QuintileCash[4]);
            town.Dispose();
        }

        // Enough cash overall for all necessities, but the poorest fifth holds too little for theirs.
        [Test]
        public void ThePoor_GoShort_WhileTheNodeCouldPay()
        {
            var town = Town(1000);
            town.DesiredNecessity[0] = 500;
            town.Desired[0] = 500;
            long share = town.NecessityBudgetFactor(0, Weeks);
            Assert.Less(share, Fx.OneRaw);
            Assert.Greater(share, 0);
            town.Dispose();
        }

        [Test]
        public void Luxuries_AreBoughtByThoseWithMoneyLeft()
        {
            var town = Town(1000);
            town.DesiredNecessity[0] = 500;
            town.Desired[0] = 600;
            Assert.AreEqual(Fx.OneRaw, town.LuxuryBudgetFactor(0, Weeks), "the top quintile alone has 700 spare");
            town.Dispose();
        }

        [Test]
        public void TheParts_AlwaysAddUpToTheCash()
        {
            var town = Town(1000);
            town.DesiredNecessity[0] = 300;
            town.Cash[0] -= 400;
            town.SpentYear[0] += 400;
            town.Settle(0, Shares);
            Assert.AreEqual(town.Cash[0], Sum(town));
            town.Cash[0] -= 900; // a loan or a move, more than is left
            town.Settle(0, Shares);
            Assert.AreEqual(town.Cash[0], Sum(town));
            town.Cash[0] += 5000;
            town.StartYear();
            town.Settle(0, Shares);
            Assert.AreEqual(town.Cash[0], Sum(town));
            town.Dispose();
        }

        [Test]
        public void Spending_FallsOnNecessitiesFifthsFirst()
        {
            var town = Town(10000);
            long poorBefore = town.QuintileCash[0];
            town.DesiredNecessity[0] = 100;
            town.Cash[0] -= 100;
            town.SpentYear[0] += 100;
            town.Settle(0, Shares);
            Assert.AreEqual(poorBefore - 20, town.QuintileCash[0]);
            town.Dispose();
        }
    }
}
