using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Each node has plots; facilities stand on them, and the rest belong to its households or the player (DEC-128).</summary>
    public class LandTests
    {
        const long Plenty = 1_000_000_000;

        static int Village(GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            return Enumerable.Range(0, nodes.Count).First(n => nodes.LevelOf(n) == WorldLevel.Village && sim.World.Get<HouseholdState>().Population[n] > 0);
        }

        [Test]
        public void AtTheStart_EachFacilityStandsOnAPlot_AndHouseholdsHoldTheRest()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var facilities = sim.World.Get<FacilityTable>();
            var nodes = sim.World.Get<NodeTable>();
            for (int n = 0; n < nodes.Count; n++)
            {
                int standing = Enumerable.Range(0, facilities.Count).Count(f => facilities.Node[f] == n);
                Assert.AreEqual(standing, land.Used[n], $"node {n}");
                Assert.GreaterOrEqual(land.Plots[n], land.Used[n]);
                Assert.AreEqual(land.Plots[n] - land.Used[n], land.Free(n));
            }
        }

        [Test]
        public void ThePrice_RisesAsFreePlotsGetFew()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var config = sim.Database.Config;
            int n = Village(sim);
            long before = land.Price(n, config);
            land.PlayerPlots[n] = land.Free(n) - 1;
            Assert.Greater(land.Price(n, config), before);
        }

        [Test]
        public void WithoutFreeLand_GroupsFoundNothing()
        {
            int Founded(bool landless)
            {
                using var sim = TestWorlds.BuildSample();
                var land = sim.World.Get<LandState>();
                if (landless)
                    for (int n = 0; n < land.Plots.Length; n++) land.PlayerPlots[n] = land.Free(n);
                int count = 0;
                for (int t = 0; t < 2 * SimClock.WeeksPerYear + 1; t++)
                {
                    sim.AdvanceTurn();
                    count += sim.LastTurnEvents.OfType<FacilityFounded>().Count();
                }
                return count;
            }
            Assert.Greater(Founded(false), 0, "groups found facilities where land is free");
            Assert.AreEqual(0, Founded(true));
        }

        [Test]
        public void FoundingAFacility_TakesAPlot()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            int used = land.Used.Sum();
            int founded = 0;
            for (int t = 0; t < 2 * SimClock.WeeksPerYear + 1; t++)
            {
                sim.AdvanceTurn();
                founded += sim.LastTurnEvents.OfType<FacilityFounded>().Count();
            }
            Assert.Greater(founded, 0);
            Assert.GreaterOrEqual(land.Used.Sum(), used + founded);
        }

        [Test]
        public void ThePlayer_BuysAPlot_AndSellsItBack_AtThePrice()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var player = sim.World.Get<PlayerState>();
            var households = sim.World.Get<HouseholdCash>();
            int n = Village(sim);
            player.Node = n;
            player.CoinsAt(n) = Plenty;
            households.Cash[n] += Plenty;
            long price = land.Price(n, sim.Database.Config), cash = households.Cash[n];
            var buy = new BuyPlotCommand();
            Assert.IsNull(buy.Validate(sim.Context).Error);
            buy.Apply(sim.Context);
            Assert.AreEqual(1, land.PlayerPlots[n]);
            Assert.AreEqual(Plenty - price, player.CoinsAt(n));
            Assert.AreEqual(cash + price, households.Cash[n]);

            long back = land.Price(n, sim.Database.Config);
            var sell = new SellPlotCommand();
            Assert.IsNull(sell.Validate(sim.Context).Error);
            sell.Apply(sim.Context);
            Assert.AreEqual(0, land.PlayerPlots[n]);
            Assert.AreEqual(Plenty - price + back, player.CoinsAt(n));
            Assert.IsNotNull(new SellPlotCommand().Validate(sim.Context).Error, "nothing left to sell");
        }

        [Test]
        public void NoFreePlot_CannotBeBought()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var player = sim.World.Get<PlayerState>();
            int n = Village(sim);
            player.Node = n;
            player.CoinsAt(n) = Plenty;
            land.PlayerPlots[n] = land.Free(n);
            Assert.AreEqual("No land is for sale here.", new BuyPlotCommand().Validate(sim.Context).Error);
        }

        [Test]
        public void Land_IsSavedAndLoaded()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            int n = Village(sim);
            land.PlayerPlots[n] = 2;
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            land.PlayerPlots[n] = 0;
            buffer.Position = 0;
            sim.Load(buffer);
            Assert.AreEqual(2, sim.World.Get<LandState>().PlayerPlots[n]);
        }
    }
}
