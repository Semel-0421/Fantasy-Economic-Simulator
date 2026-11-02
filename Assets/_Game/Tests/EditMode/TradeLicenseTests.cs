using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Trade between nations and between continents needs its own license (DEC-116).</summary>
    public class TradeLicenseTests
    {
        static int InOtherNation(GameSimulation sim, int from, bool otherContinent)
        {
            var nodes = sim.World.Get<NodeTable>();
            int nation = nodes.AncestorAt(from, WorldLevel.Nation);
            return Enumerable.Range(0, nodes.Count).First(n => nodes.AncestorAt(n, WorldLevel.Nation) >= 0 && nodes.AncestorAt(n, WorldLevel.Nation) != nation
                && (nodes.Continent[n] != nodes.Continent[from]) == otherContinent);
        }

        static int ASeat(GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            return Enumerable.Range(0, nodes.Count).First(n => nodes.AncestorAt(n, WorldLevel.Nation) >= 0 && nodes.IsSeat(n));
        }

        // The sample world has one nation per continent, so only this case can be played in it.
        [TestCase(true, TradeLicenseKind.Intercontinental)]
        public void CargoLeavesANation_OnlyWithItsLicense(bool otherContinent, TradeLicenseKind kind)
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            player.Node = ASeat(sim);
            int to = InOtherNation(sim, player.Node, otherContinent);
            Assert.IsTrue(PlayerTrip.CargoMayLeave(sim.Context, player.Node, to), "an empty cart crosses");

            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, wheat, 1);
            Assert.IsFalse(PlayerTrip.CargoMayLeave(sim.Context, player.Node, to));

            player.LocalCoins += 100_000;
            var buy = new BuyTradeLicenseCommand(kind);
            Assert.IsTrue(buy.Validate(sim.Context).Ok);
            buy.Apply(sim.Context);
            Assert.IsTrue(PlayerTrip.CargoMayLeave(sim.Context, player.Node, to));
        }

        [Test]
        public void ALicense_PaysTheNation_WithoutMakingCoins()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            player.Node = ASeat(sim);
            player.LocalCoins += 100_000;
            int continent = sim.World.Get<NodeTable>().Continent[player.Node];
            long money = MoneyLedger.ContinentMoney(sim.World, continent);
            new BuyTradeLicenseCommand(TradeLicenseKind.International).Apply(sim.Context);
            Assert.AreEqual(money, MoneyLedger.ContinentMoney(sim.World, continent));
        }

        [Test]
        public void UnlicensedMerchants_StayInTheirNation()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig(
                "license.international_merchant_share_permille,0", "license.intercontinental_merchant_share_permille,0"), 1).Build();
            var agents = sim.World.Get<AgentTable>();
            var nodes = sim.World.Get<NodeTable>();
            for (int t = 0; t < 2 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                for (int a = 0; a < agents.Count; a++)
                    Assert.AreEqual(nodes.AncestorAt(agents.Home[a], WorldLevel.Nation), nodes.AncestorAt(agents.Node[a], WorldLevel.Nation), "group " + a);
            }
        }
    }
}
