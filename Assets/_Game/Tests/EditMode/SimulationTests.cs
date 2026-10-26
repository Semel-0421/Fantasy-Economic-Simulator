using System.Diagnostics;
using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;
using Debug = UnityEngine.Debug;

namespace FES.Tests
{
    public class SimulationTests
    {
        [Test]
        public void SampleWorld_RunsOneYear()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            Assert.AreEqual(36, nodes.Count);
            Assert.AreEqual(2, nodes.ContinentCount);

            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.AreEqual(SimClock.WeeksPerYear, sim.Turn);
        }

        [Test]
        public void SpawnCounts_FollowConfig()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            int villages = nodes.LevelCount(WorldLevel.Village), cities = nodes.LevelCount(WorldLevel.City);
            var facilities = sim.World.Get<FacilityTable>();
            Assert.AreEqual(villages * (4 + 2) + cities * 4, facilities.Count);

            int expectedMerchants = villages * 3 + cities * 10 + nodes.LevelCount(WorldLevel.Region) * 30
                + nodes.LevelCount(WorldLevel.Nation) * 50 + nodes.LevelCount(WorldLevel.Continent) * 50;
            Assert.AreEqual(expectedMerchants, sim.World.Get<AgentTable>().Count);
        }

        [Test]
        public void SameSeed_SameHash_DifferentSeed_DifferentHash()
        {
            using var a = TestWorlds.BuildSample(seed: 1);
            using var b = TestWorlds.BuildSample(seed: 1);
            using var c = TestWorlds.BuildSample(seed: 2);
            for (int t = 0; t < 30; t++)
            {
                a.AdvanceTurn();
                b.AdvanceTurn();
                c.AdvanceTurn();
            }
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash());
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesStateAndFuture()
        {
            using var original = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 10; t++) original.AdvanceTurn();
            using var buffer = new MemoryStream();
            original.Save(buffer);

            using var restored = TestWorlds.BuildSample(seed: 5);
            buffer.Position = 0;
            restored.Load(buffer);
            Assert.AreEqual(10, restored.Turn);
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());

            for (int t = 0; t < 10; t++)
            {
                original.AdvanceTurn();
                restored.AdvanceTurn();
            }
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());
        }

        [Test]
        public void Load_CorruptSave_LeavesWorldUnchanged()
        {
            using var source = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 5; t++) source.AdvanceTurn();
            using var buffer = new MemoryStream();
            source.Save(buffer);
            var truncated = new MemoryStream(buffer.ToArray(), 0, (int)buffer.Length / 2);

            using var target = TestWorlds.BuildSample(seed: 5);
            target.AdvanceTurn();
            ulong before = target.ComputeHash();
            Assert.Catch(() => target.Load(truncated));
            Assert.AreEqual(before, target.ComputeHash());
            Assert.AreEqual(1, target.Turn);
        }

        [Test]
        public void Load_DiscardsQueuedCommands()
        {
            using var sim = TestWorlds.BuildSample();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            var player = sim.World.Get<PlayerState>();
            long cash = player.Cash;

            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 100));
            buffer.Position = 0;
            sim.Load(buffer);
            sim.AdvanceTurn();
            Assert.AreEqual(cash, player.Cash);
        }

        [Test]
        public void Commands_RejectNegativeAmounts()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            long cash = player.Cash;
            sim.Enqueue(new BuyFacilityCommand(new FacilityId(0), -1000, GuidanceAxis.Industry));
            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, -1000));
            sim.AdvanceTurn();
            Assert.AreEqual(cash, player.Cash);
            Assert.AreEqual(2, sim.LastTurnEvents.OfType<CommandRejected>().Count());
        }

        [Test]
        public void Builder_RejectsDuplicateModules_AndDefaultsAreIdempotent()
        {
            var builder = SimulationBuilder.FromData(TestWorlds.Sample(), 1).WithDefaultModules();
            Assert.Throws<System.InvalidOperationException>(() => builder.AddModule(new WorldModule()));
            using var sim = builder.Build();
            Assert.AreEqual(0, sim.Turn);
        }

        [Test]
        public void Commands_AreValidated_AndFeedGuidance()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            var guidance = sim.World.Get<GuidanceState>();
            long cash = player.Cash;

            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 200));
            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 1_000_000));
            sim.AdvanceTurn();

            Assert.AreEqual(cash - 200, player.Cash);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(GuidanceAxis.Trade, guidance.Dominant);
            Assert.Greater(guidance[GuidanceAxis.Trade].Raw, Fx.FromInt(4).Raw);
            Assert.AreEqual(player.Node, guidance.FocusNode);
        }

        [Test]
        public void BuyingFacility_MakesItsNodeDetailed()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var facilities = sim.World.Get<FacilityTable>();
            var lod = sim.World.Get<LodState>();
            var player = sim.World.Get<PlayerState>();

            int playerRegion = nodes.AncestorAt(player.Node, WorldLevel.Region);
            int far = Enumerable.Range(0, facilities.Count).First(f => !nodes.IsAncestorOrSelf(playerRegion, facilities.Node[f]));
            sim.AdvanceTurn();
            Assert.IsFalse(lod.IsDetailed(facilities.Node[far]));
            Assert.IsTrue(lod.IsDetailed(player.Node));

            sim.Enqueue(new BuyFacilityCommand(new FacilityId(far), 100, GuidanceAxis.Agriculture));
            sim.AdvanceTurn(); // Commands phase runs before the Lod phase, so this same turn sees the purchase
            Assert.AreEqual(OwnerIds.Player, facilities.Owner[far]);
            Assert.IsTrue(lod.IsDetailed(facilities.Node[far]));
        }

        [Test, Explicit("Builds the full 1,580-market / 4,000-item world; run manually to measure.")]
        [Category("Scale")]
        public void FullScale_MemoryAndTurnTime()
        {
            var build = Stopwatch.StartNew();
            using var sim = SimulationBuilder.FromData(TestWorlds.FullScale(), 1).Build();
            build.Stop();
            var markets = sim.World.Get<MarketStore>();

            sim.AdvanceTurn(); // warm-up (Burst compile, first allocation)
            const int turns = 12;
            var run = Stopwatch.StartNew();
            for (int t = 0; t < turns; t++) sim.AdvanceTurn();
            run.Stop();

            var perSystem = string.Join(", ", sim.Systems.Select((s, i) => $"{s.Id}={sim.LastSystemDuration(i).TotalMilliseconds:0.0}ms"));
            Debug.Log($"[FES scale] markets={markets.MarketCount} cells={markets.CellCount} " +
                      $"marketMB={markets.NativeBytes / (1024.0 * 1024.0):0.0} " +
                      $"facilities={sim.World.Get<FacilityTable>().Count} merchants={sim.World.Get<AgentTable>().Count} " +
                      $"build={build.ElapsedMilliseconds}ms avgTurn={run.Elapsed.TotalMilliseconds / turns:0.0}ms | {perSystem}");

            Assert.AreEqual(1580, markets.MarketCount);
            Assert.AreEqual(4000, sim.Database.GoodCount);
        }
    }
}
