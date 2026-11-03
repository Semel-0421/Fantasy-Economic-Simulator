using System.Linq;
using FES.Agents;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Merchants start in four classes of capital, whatever their node: top, average, most common, and the player's own start.</summary>
    public class MerchantCapitalTests
    {
        [Test]
        public void Merchants_StartInFourClasses_OneWithThePlayersCash()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var cash = Enumerable.Range(0, agents.Count).Select(a => agents.Cash[a]).Distinct().OrderBy(c => c).ToArray();
            var config = sim.Database.Config;
            CollectionAssert.AreEqual(new[] { 100L, config.PlayerStartCash, 1800L, 9500L }, cash);
        }

        [Test]
        public void TheClass_DoesNotFollowTheNode()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            int a = Enumerable.Range(0, agents.Count).First(i => agents.Cash[i] == 9500);
            Assert.IsTrue(Enumerable.Range(0, agents.Count).Any(i => agents.Scale[i] == agents.Scale[a] && agents.Cash[i] != 9500));
        }

        [Test]
        public void Shares_AreAboutAsConfigured()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            int top = Enumerable.Range(0, agents.Count).Count(i => agents.Cash[i] == 9500);
            Assert.Greater(top, 0);
            Assert.Less(top, agents.Count / 4, "the top class is the few");
        }

        [Test]
        public void EachClass_StartsWithItsVehicle()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var config = sim.Database.Config;
            var expected = new[] { MerchantVehicles.Wagon, MerchantVehicles.Cart, MerchantVehicles.Cart, MerchantVehicles.Pack };
            for (int a = 0; a < agents.Count; a++)
                Assert.AreEqual(expected[MerchantCapital.ClassOf(config, sim.Context.Seed, a)], agents.Vehicle[a], $"group {a}");
        }

        [Test]
        public void AReplacementGroup_StartsInItsClass()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            int a = 0;
            long start = agents.Cash[a];
            Assert.AreEqual(start, MerchantCapital.Start(sim.Database.Config, sim.Context.Seed, a));
        }
    }
}
