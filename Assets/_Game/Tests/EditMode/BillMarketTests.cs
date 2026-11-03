using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>A group short of coins sells the bills it holds below face, priced by whether the traders can pay (DEC-122).</summary>
    public class BillMarketTests
    {
        const long Plenty = 1_000_000_000;

        [Test]
        public void Price_FollowsWhatTheTradersCanPay_AndStaysBelowFace()
        {
            var discount = Fx.FromPermille(150);
            Assert.AreEqual(850, BillMarket.Price(1_000, Plenty, 1_000, discount));
            Assert.AreEqual(425, BillMarket.Price(1_000, 500, 1_000, discount), "the traders' coins cover half of what they owe");
            Assert.AreEqual(0, BillMarket.Price(1_000, 0, 1_000, discount));
            Assert.AreEqual(0, BillMarket.Price(1_000, -5, 1_000, discount));
        }

        // DEC-123: traders may owe on bills no more than the coins they hold.
        [Test]
        public void BillRoom_NeverExceedsWhatTheTradersHold()
        {
            Assert.AreEqual(500, BillMarket.Room(10_000, 500));
            Assert.AreEqual(0, BillMarket.Room(10_000, -50));
            Assert.AreEqual(800, BillMarket.Room(800, 10_000), "households' wishes still bound it");
        }


        sealed class Scene : System.IDisposable
        {
            public readonly GameSimulation Sim;
            public readonly AgentTable Agents;
            public readonly BillTable Bills;
            public readonly int A, Row;

            public Scene(long cash)
            {
                Sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.bill_cap_weeks,8"), 1).Build();
                Agents = Sim.World.Get<AgentTable>();
                Bills = Sim.World.Get<BillTable>();
                var purses = Sim.World.Get<PurseState>();
                for (int a = 0; a < Agents.Count; a++) Agents.Mode[a] = (byte)ControlMode.Manual; // only the bill market acts
                A = Enumerable.Range(0, Agents.Count).First(a => purses.NodeFloat[Agents.Home[a]] < purses.NationCount);
                Agents.Cash[A] = cash;
                int purse = purses.NodeFloat[Agents.Home[A]];
                purses.Float[purse] = Plenty;
                var households = Sim.World.Get<HouseholdCash>();
                foreach (int node in purses.NationNodes[purse]) households.Cash[node] += Plenty;
                Row = Bills.Count;
                Bills.Holder.Add(A);
                Bills.Purse.Add(purse);
                Bills.Face.Add(1_000);
                Bills.DueTurn.Add(Sim.Context.Turn + 8);
            }

            public long HeldBy(int holder) => Enumerable.Range(Row, Bills.Count - Row).Where(r => Bills.Holder[r] == holder).Sum(r => Bills.Face[r]);

            public void Dispose() => Sim.Dispose();
        }

        [Test]
        public void AGroupInTheRed_SellsItsBill_BelowFace()
        {
            using var s = new Scene(-100);
            s.Sim.AdvanceTurn();
            Assert.Greater(s.HeldBy(BillHolders.Households), 990, "the buyers collect the face when it falls due; shares round down");
            Assert.Less(s.HeldBy(s.A), 10);
            Assert.AreEqual(1_000, s.HeldBy(BillHolders.Households) + s.HeldBy(s.A));
            Assert.Greater(s.Agents.Cash[s.A], 0, "the sale pays its debt");
            Assert.Less(s.Agents.Cash[s.A], -100 + 1_000);
            Assert.Greater(s.Sim.World.Get<CreditStats>().BillsSold, 0);
        }

        [Test]
        public void AGroupWithCoins_KeepsItsBill()
        {
            using var s = new Scene(100);
            s.Sim.AdvanceTurn();
            Assert.AreEqual(s.A, s.Bills.Holder[s.Row]);
            Assert.AreEqual(100, s.Agents.Cash[s.A]);
        }

        [Test]
        public void SellingABill_KeepsTheCoins()
        {
            using var s = new Scene(-100);
            var currency = s.Sim.World.Get<CurrencyState>();
            var before = Enumerable.Range(0, currency.ContinentCount).Select(c => MoneyLedger.ContinentMoney(s.Sim.World, c) - currency.CoinSupply[c]).ToArray();
            s.Sim.AdvanceTurn();
            Assert.Greater(s.HeldBy(BillHolders.Households), 0);
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(before[c], MoneyLedger.ContinentMoney(s.Sim.World, c) - currency.CoinSupply[c]);
        }
    }
}
