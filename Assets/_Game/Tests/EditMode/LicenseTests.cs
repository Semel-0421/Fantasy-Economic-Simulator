using System.Collections.Generic;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class LicenseTests
    {
        static GoodId Tools(GameSimulation sim, QualityGrade grade = QualityGrade.Mid)
        {
            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", grade, out var good));
            return good;
        }

        /// <summary>Puts the player in a city that trades tools, with coins for a license and tools in the cart.</summary>
        static PlayerState AtCityWithTools(GameSimulation sim, string city = "aur_n1_r1_c1")
        {
            Assert.IsTrue(sim.View.TryFindNode(city, out var node));
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = node.Value;
            player.LocalCoins = 5000;
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Tools(sim), 4);
            return player;
        }

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        [Test]
        public void LicensedGoods_ComeFromGoodsData_ForEveryGrade()
        {
            var db = GameDatabase.Load(TestWorlds.Sample());
            for (int g = 0; g < QualityGrades.Count; g++)
            {
                Assert.IsTrue(db.TryGetGood("iron_tools", (QualityGrade)g, out var tools));
                Assert.IsTrue(db.Good(tools).RequiresLicense);
                Assert.IsTrue(db.TryGetGood("wheat", (QualityGrade)g, out var wheat));
                Assert.IsFalse(db.Good(wheat).RequiresLicense);
            }
            var goods = TestWorlds.Sample().ReadText(GameDatabase.GoodsFile).Replace("iron_tools,tools,90,12,6,4,1", "iron_tools,tools,90,12,6,4,2");
            Assert.Throws<System.FormatException>(() => GameDatabase.Load(TestWorlds.SampleWith(GameDatabase.GoodsFile, goods)));
        }

        [Test]
        public void SellingLicensedGoods_NeedsTheLicense_AnyoneMayBuy()
        {
            using var sim = TestWorlds.BuildSample();
            var player = AtCityWithTools(sim);
            Assert.IsFalse(sim.View.MaySell(Tools(sim)));
            sim.Actions.SellGoods(Tools(sim), 1);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));

            long coins = player.LocalCoins;
            long price = sim.View.LicensePrice(Tools(sim));
            Assert.AreEqual(20 * sim.Database.Good(Tools(sim)).BasePrice, price);
            sim.Actions.BuyLicense(Tools(sim, QualityGrade.Low)); // one license covers every grade
            sim.Actions.SellGoods(Tools(sim), 1);
            sim.AdvanceTurn();
            Assert.AreEqual(0, Rejected(sim));
            var bought = sim.LastTurnEvents.OfType<LicenseBought>().Single();
            Assert.AreEqual(sim.Turn - 1 + SimClock.WeeksPerYear, bought.ExpiresTurn);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<GoodsTraded>().Single().Filled);
            Assert.AreEqual(coins - price + sim.LastTurnEvents.OfType<GoodsTraded>().Single().Price, player.LocalCoins);
            Assert.IsTrue(sim.View.MaySell(Tools(sim, QualityGrade.Best)));

            var licenses = new List<LicenseInfo>();
            sim.View.GetLicenses(licenses);
            Assert.AreEqual(Tools(sim).BaseIndex, licenses.Single().BaseGood);
            Assert.AreEqual(WorldLevel.Nation, sim.View.NodeLevel(licenses.Single().Nation));
        }

        [Test]
        public void License_IsValidOnlyInTheIssuingNation()
        {
            using var sim = TestWorlds.BuildSample();
            var player = AtCityWithTools(sim);
            sim.Actions.BuyLicense(Tools(sim));
            sim.AdvanceTurn();

            Assert.IsTrue(sim.View.TryFindNode("bor_n1_r1_c2", out var abroad));
            player.Node = abroad.Value;
            Assert.IsFalse(sim.View.MaySell(Tools(sim)));
            sim.Actions.SellGoods(Tools(sim), 1);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
        }

        [Test]
        public void License_IsRenewedForAYear_AndExpires()
        {
            using var sim = TestWorlds.BuildSample();
            AtCityWithTools(sim);
            var licenses = sim.World.Get<LicenseTable>();
            sim.Actions.BuyLicense(Tools(sim));
            sim.AdvanceTurn();
            int expires = licenses.ExpiresTurn[0];
            sim.Actions.BuyLicense(Tools(sim)); // renewing early adds a year to the current expiry
            sim.AdvanceTurn();
            Assert.AreEqual(expires + SimClock.WeeksPerYear, licenses.ExpiresTurn[0]);
            Assert.AreEqual(1, licenses.Count);

            licenses.ExpiresTurn[0] = sim.Turn + 1;
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<LicenseExpired>().Count());
            Assert.AreEqual(0, licenses.Count);
            Assert.IsFalse(sim.View.MaySell(Tools(sim)));
        }

        [Test]
        public void BuyLicense_IsRejected_ForUnlicensedGoods_OrWithoutCash()
        {
            using var sim = TestWorlds.BuildSample();
            var player = AtCityWithTools(sim);
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            sim.Actions.BuyLicense(wheat);
            sim.AdvanceTurn();
            player.LocalCoins = sim.View.LicensePrice(Tools(sim)) - 1;
            sim.Actions.BuyLicense(Tools(sim));
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
            Assert.AreEqual(0, sim.World.Get<LicenseTable>().Count);
        }

        [Test]
        public void BuyingALicense_SignalsTrade()
        {
            using var sim = TestWorlds.BuildSample();
            AtCityWithTools(sim);
            sim.Actions.BuyLicense(Tools(sim));
            sim.AdvanceTurn();
            Assert.AreEqual(GuidanceAxis.Trade, sim.View.DominantGuidance);
        }

        [Test]
        public void SomeMerchantGroups_HoldLicenses_FixedBySeed()
        {
            int Holders(ulong seed, int share) =>
                Enumerable.Range(0, 2000).Count(a => LicenseRules.MerchantHoldsLicenses(seed, new AgentId(a), share));
            Assert.AreEqual(0, Holders(1, 0));
            Assert.AreEqual(2000, Holders(1, 1000));
            Assert.AreEqual(Holders(7, 200), Holders(7, 200));
            Assert.That(Holders(7, 200), Is.InRange(300, 500));
        }
    }
}
