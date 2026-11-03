using System.Linq;
using FES.Agents;
using FES.Simulation;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Merchants' premiums and refunds pass through the origin city's guild, as the player's do (C27, DEC-061).</summary>
    public class MerchantInsuranceTests
    {
        // Only the merchants' step runs, so nothing else can move a guild's cash.
        [Test]
        public void Premiums_ReachTheGuilds()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("terrain.road.danger_permille,1000"), 1).Build();
            var groups = sim.World.Get<GroupTable>();
            var stats = sim.World.Get<MerchantTripStats>();
            var merchants = sim.Systems.OfType<MerchantSystem>().Single();
            long gained = 0, cover = stats.SpentCover;
            for (int t = 0; t < 60 && stats.SpentCover == cover; t++)
            {
                sim.AdvanceTurn();
                cover = stats.SpentCover;
                long guilds = Guilds(groups);
                merchants.Execute(sim.Context);
                gained = Guilds(groups) - guilds;
            }
            Assert.Greater(stats.SpentCover, cover, "someone bought cover");
            Assert.AreNotEqual(0, gained, "premiums less refunds moved through the guilds");
        }

        static long Guilds(GroupTable groups) =>
            Enumerable.Range(0, groups.Count).Where(g => groups.KindOf(g) == GroupKind.Guild).Sum(g => groups.Cash[g]);
    }
}
