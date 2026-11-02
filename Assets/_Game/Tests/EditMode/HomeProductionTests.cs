using System.Linq;
using FES.Agents;
using FES.Economy;
using FES.Simulation;
using FES.World;
using FES.Core;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Households grow some food: they eat it first, and the rest goes to market (DEC-117).</summary>
    public class HomeProductionTests
    {
        static int Village(GameSimulation sim, GoodId good, out int cell)
        {
            var nodes = sim.World.Get<NodeTable>();
            var markets = sim.World.Get<MarketStore>();
            int found = -1;
            for (int n = 0; n < nodes.Count && found < 0; n++)
                if (nodes.LevelOf(n) == WorldLevel.Village && sim.World.Get<HouseholdState>().Population[n] > 0 && markets.TryFindCell(n, good, out _)) found = n;
            markets.TryFindCell(found, good, out cell);
            return found;
        }

        // Nobody holds a coin, so anything they eat is what they grew.
        [Test]
        public void PenniLessHouseholds_StillEatWhatTheyGrow()
        {
            using var sim = TestWorlds.BuildSample();
            var cash = sim.World.Get<HouseholdCash>();
            var households = sim.World.Get<HouseholdState>();
            for (int n = 0; n < cash.Cash.Length; n++) cash.Cash[n] = 0;
            for (int q = 0; q < cash.QuintileCash.Length; q++) cash.QuintileCash[q] = 0;
            for (int n = 0; n < households.NecessityMet.Length; n++) households.NecessityMet[n] = 0;
            sim.Systems.OfType<PricingSystem>().Single().Execute(sim.Context);
            Assert.Greater(Enumerable.Range(0, households.NecessityMet.Length).Sum(n => households.NecessityMet[n]), 0);
        }

        static long GrownInto(int population)
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.Database.TryGetGood("vegetables", QualityGrade.Low, out var good) || sim.Database.TryGetGood("vegetables", QualityGrade.Mid, out good));
            int node = Village(sim, good, out int cell);
            var households = sim.World.Get<HouseholdState>();
            households.Population[node] = population;
            sim.Systems.OfType<PricingSystem>().Single().Execute(sim.Context);
            long left = sim.World.Get<HouseholdCash>().HomeGrown[cell];
            return left;
        }

        // A crowd grows far more than it eats of one grade, so the leftovers show the crop.
        [Test]
        public void TheCrop_FollowsThePeople()
        {
            long small = GrownInto(1_000_000), large = GrownInto(2_000_000);
            Assert.Greater(small, 0);
            Assert.Greater(large, small);
        }

        [Test]
        public void TheSurplus_GoesToMarket_AndPaysHouseholds()
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.Database.TryGetGood("vegetables", QualityGrade.Low, out var good) || sim.Database.TryGetGood("vegetables", QualityGrade.Mid, out good));
            int node = Village(sim, good, out int cell);
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var markets = sim.World.Get<MarketStore>();
            purses.Float[purses.NodeFloat[node]] = 1_000_000;
            markets.Price[cell] = 8;
            cash.HomeGrown[cell] = 100;
            long before = cash.Cash[node], supply = markets.Supply[cell], floatBefore = purses.Float[purses.NodeFloat[node]];
            sim.Systems.OfType<HomeSurplusSystem>().Single().Execute(sim.Context);
            long paid = cash.Cash[node] - before;
            Assert.AreEqual(100, markets.Supply[cell] - supply);
            Assert.AreEqual(100L * sim.Database.Config.TraderOffer(8), paid);
            Assert.AreEqual(floatBefore - paid, purses.Float[purses.NodeFloat[node]]);
            Assert.AreEqual(0, cash.HomeGrown[cell]);
        }

        [Test]
        public void Towns_GrowNothing_OnlyVillagesDo()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var households = sim.World.Get<HouseholdState>();
            for (int n = 0; n < nodes.Count; n++)
                households.Population[n] = nodes.LevelOf(n) == WorldLevel.Village ? 0 : 5_000_000;
            sim.Systems.OfType<PricingSystem>().Single().Execute(sim.Context);
            var cash = sim.World.Get<HouseholdCash>();
            Assert.AreEqual(0, Enumerable.Range(0, cash.HomeGrown.Length).Sum(c => (long)cash.HomeGrown[c]));
        }

        // Dried and pickled food keeps: what cannot be sold this week waits for a buyer.
        [TestCase("pickled_vegetables")]
        [TestCase("dried_beans")]
        public void PreservedFood_KeepsWhenTradersCannotPay(string key)
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.Database.TryGetGood(key, QualityGrade.Low, out var good));
            int node = Village(sim, good, out int cell);
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            purses.Float[purses.NodeFloat[node]] = 0;
            cash.HomeGrown[cell] = 100;
            sim.Systems.OfType<HomeSurplusSystem>().Single().Execute(sim.Context);
            Assert.AreEqual(100, cash.HomeGrown[cell]);
        }

        [TestCase("pickled_vegetables", "vegetables")]
        [TestCase("dried_beans", "beans")]
        public void PreservedFood_IsSmallerAndDearerThanFresh(string preserved, string fresh)
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.Database.TryGetGood(preserved, QualityGrade.Mid, out var p));
            Assert.IsTrue(sim.Database.TryGetGood(fresh, QualityGrade.Mid, out var f));
            var a = sim.Database.Good(p);
            var b = sim.Database.Good(f);
            Assert.IsTrue(a.IsPreserved);
            Assert.IsFalse(b.IsPreserved);
            Assert.Less(a.Size, b.Size);
            Assert.Greater(a.BasePrice, b.BasePrice);
            Assert.Greater(a.HomeOutput, 0);
        }

        [Test]
        public void WhatTradersCannotPayFor_Spoils()
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.Database.TryGetGood("vegetables", QualityGrade.Low, out var good) || sim.Database.TryGetGood("vegetables", QualityGrade.Mid, out good));
            int node = Village(sim, good, out int cell);
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var markets = sim.World.Get<MarketStore>();
            purses.Float[purses.NodeFloat[node]] = 0;
            cash.HomeGrown[cell] = 100;
            long supply = markets.Supply[cell];
            sim.Systems.OfType<HomeSurplusSystem>().Single().Execute(sim.Context);
            Assert.AreEqual(supply, markets.Supply[cell]);
            Assert.AreEqual(0, cash.HomeGrown[cell]);
        }
    }
}
