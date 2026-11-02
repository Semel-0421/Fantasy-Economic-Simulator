using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Loading replaces the world as one guarded step: all of it and what is derived from it, or none of it.</summary>
    public class LoadTransitionTests
    {
        sealed class FailsOnLoad : ISimSystem, ILoadListener
        {
            public int FailuresLeft;

            public string Id => "tests.fails_on_load";
            public SimPhase Phase => SimPhase.Cleanup;
            public int Order => SimOrder.Default;

            public void Initialize(SimContext ctx)
            {
            }

            public void Execute(SimContext ctx)
            {
            }

            public void OnLoaded(SimContext ctx)
            {
                if (FailuresLeft <= 0) return;
                FailuresLeft--;
                throw new InvalidOperationException("listener failed");
            }
        }

        sealed class FailingModule : ISimModule
        {
            readonly FailsOnLoad _system;

            public FailingModule(FailsOnLoad system) => _system = system;

            public string Id => "tests.failing";
            public IReadOnlyList<string> Requires => Array.Empty<string>();

            public void Register(SimBuilder builder) => builder.AddSystem(_system);
        }

        static MemoryStream SaveOf(GameSimulation sim)
        {
            var buffer = new MemoryStream();
            sim.Save(buffer);
            buffer.Position = 0;
            return buffer;
        }

        static List<int> NodesWithoutFamily(GameSimulation sim)
        {
            var groups = sim.World.Get<GroupTable>();
            var taken = new HashSet<int>();
            for (int g = 0; g < groups.Count; g++)
                if (groups.KindOf(g) == GroupKind.PrivateEnterprise) taken.Add(groups.Node[g]);
            return Enumerable.Range(0, sim.World.Get<NodeTable>().Count).Where(n => !taken.Contains(n)).ToList();
        }

        [Test]
        public void AFailingListener_RestoresTheStateBeforeTheLoad()
        {
            var listener = new FailsOnLoad();
            using var sim = SimulationBuilder.FromData(TestWorlds.Sample(), 1).AddModule(new FailingModule(listener)).Build();
            using var older = SaveOf(sim);
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            ulong before = sim.ComputeHash();
            int turn = sim.Turn;

            listener.FailuresLeft = 1;
            Assert.Throws<InvalidOperationException>(() => sim.Load(older));

            Assert.AreEqual(turn, sim.Turn);
            Assert.AreEqual(before, sim.ComputeHash());
        }

        [Test]
        public void ASaveWithThePlayerOffTheMap_IsRefused()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            int node = player.Node;
            player.Node = -2;
            using var broken = SaveOf(sim);
            player.Node = node;
            ulong before = sim.ComputeHash();

            Assert.Catch<Exception>(() => sim.Load(broken));

            Assert.AreEqual(node, player.Node);
            Assert.AreEqual(before, sim.ComputeHash());
        }

        [Test]
        public void ALongLivedDirectory_SeesTheLoadedGroups_WhenTheirCountMatches()
        {
            using var sim = TestWorlds.BuildSample();
            var groups = sim.World.Get<GroupTable>();
            var nodes = sim.World.Get<NodeTable>();
            var free = NodesWithoutFamily(sim);
            Assert.GreaterOrEqual(free.Count, 2, "the sample needs villages without a family");
            var directory = new GroupDirectory(groups, nodes);
            using var save = SaveOf(sim);
            directory.PrivateAt(free[0], sim.Database.Config);

            sim.Load(save);
            int otherTimeline = new GroupDirectory(groups, nodes).PrivateAt(free[1], sim.Database.Config);
            int count = groups.Count;

            Assert.AreEqual(otherTimeline, directory.PrivateAt(free[1], sim.Database.Config));
            Assert.AreEqual(count, groups.Count);
        }

        [Test]
        public void Loading_LeavesABankHeldFacilityWithoutAnOwner()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            facilities.Owner[0] = OwnerIds.Bank;
            facilities.Group[0] = -1;

            sim.Systems.OfType<GroupSystem>().Single().OnLoaded(sim.Context);

            Assert.AreEqual(-1, facilities.Group[0]);
        }

        [Test]
        public void AFamilyFoundedForAnOlderSave_IsPaidForByTheHouseholds()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var groups = sim.World.Get<GroupTable>();
            var totals = sim.World.Get<MoneyTotals>();
            int node = NodesWithoutFamily(sim).First();
            int primary = sim.Database.Facilities.First(d => d.Tier == IndustryTier.Primary && !d.StateOwned).Index;
            facilities.Add(node, primary, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, 0);
            totals.Recompute(sim.World);
            var money = totals.ContinentTotal.ToArray();
            int count = groups.Count;

            sim.Systems.OfType<GroupSystem>().Single().OnLoaded(sim.Context);

            Assert.Greater(groups.Count, count);
            totals.Recompute(sim.World);
            CollectionAssert.AreEqual(money, totals.ContinentTotal);
        }

        [Test]
        public void MoneyTotals_FollowASaveOfTheSameTurn()
        {
            using var sim = TestWorlds.BuildSample();
            var totals = sim.World.Get<MoneyTotals>();
            var bank = sim.World.Get<BankState>();
            using var first = SaveOf(sim);
            bank.Reserve[0] += 1000;
            using var second = SaveOf(sim);
            long richer = bank.Reserve[0];

            sim.Load(first);
            totals.Refresh(sim.World);
            sim.Load(second);
            totals.Refresh(sim.World);

            Assert.AreEqual(richer, totals.NationReserve[0]);
        }
    }
}
