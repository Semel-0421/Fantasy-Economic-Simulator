using System.Collections.Generic;
using FES.Core;
using NUnit.Framework;

namespace FES.Tests
{
    public class CoreTests
    {
        [Test]
        public void Fx_Arithmetic_IsExact()
        {
            var half = Fx.FromPermille(500);
            Assert.AreEqual(Fx.One, half + half);
            Assert.AreEqual(Fx.FromPermille(250), half * half);
            Assert.AreEqual(Fx.FromInt(2), Fx.One / half);
            Assert.AreEqual(150, Fx.FromPermille(1500).MulInt(100));
            Assert.AreEqual(Fx.FromPermille(-500).Raw, (-half).Raw);
            Assert.AreEqual(Fx.Zero, Fx.FromRatio(5, 0));
        }

        [Test]
        public void DetRandom_SameKey_SameSequence_AndInRange()
        {
            var a = DetRandom.For(7, 3, 11, 5);
            var b = DetRandom.For(7, 3, 11, 5);
            var c = DetRandom.For(7, 3, 11, 6);
            bool anyDifferent = false;
            for (int i = 0; i < 100; i++)
            {
                int x = a.NextInt(10);
                Assert.AreEqual(x, b.NextInt(10));
                Assert.That(x, Is.InRange(0, 9));
                anyDifferent |= x != c.NextInt(10);
            }
            Assert.IsTrue(anyDifferent);
        }

        [Test]
        public void DetHash_Of_IsStableAndDistinct()
        {
            Assert.AreEqual(DetHash.Of("economy.pricing"), DetHash.Of("economy.pricing"));
            Assert.AreNotEqual(DetHash.Of("economy.pricing"), DetHash.Of("economy.currency"));
        }

        [Test]
        public void TierSchedule_DueAndElapsed()
        {
            var schedule = new TierSchedule(new[]
            {
                new TierCadence(4, 0), new TierCadence(4, 1), new TierCadence(2, 0), new TierCadence(1, 0), new TierCadence(1, 0),
            });
            var due = new List<int>();
            for (int t = 0; t < 8; t++)
                if (schedule.IsDue(WorldLevel.Nation, t)) due.Add(t);
            CollectionAssert.AreEqual(new[] { 3, 7 }, due);
            Assert.IsTrue(schedule.IsDue(WorldLevel.Village, 5));
            Assert.IsFalse(schedule.IsDue(WorldLevel.Region, 1));
            Assert.AreEqual(1, schedule.ElapsedTurns(WorldLevel.Continent, 0));
            Assert.AreEqual(4, schedule.ElapsedTurns(WorldLevel.Continent, 8));
        }

        sealed class RecordingSystem : ISimSystem
        {
            readonly List<string> _log;
            public RecordingSystem(string id, SimPhase phase, int order, List<string> log)
            {
                Id = id;
                Phase = phase;
                Order = order;
                _log = log;
            }
            public string Id { get; }
            public SimPhase Phase { get; }
            public int Order { get; }
            public void Initialize(SimContext ctx) { }
            public void Execute(SimContext ctx) => _log.Add(Id);
        }

        [Test]
        public void Pipeline_RunsByPhaseThenOrderThenId_AndAdvancesClock()
        {
            var log = new List<string>();
            using var world = new WorldState();
            var clock = world.Add(new SimClock(1));
            var pipeline = new TurnPipeline(new ISimSystem[]
            {
                new RecordingSystem("b", SimPhase.Markets, 0, log),
                new RecordingSystem("a", SimPhase.Markets, 0, log),
                new RecordingSystem("late", SimPhase.Production, 5, log),
                new RecordingSystem("early", SimPhase.Production, -5, log),
                new RecordingSystem("first", SimPhase.Commands, 99, log),
            });
            var ctx = new SimContext(world, new ServiceRegistry(), TierSchedule.EveryTurn(), clock);
            pipeline.Initialize(ctx);
            pipeline.Advance(ctx);
            CollectionAssert.AreEqual(new[] { "first", "early", "late", "a", "b" }, log);
            Assert.AreEqual(1, clock.Turn);
        }

        sealed class StubModule : ISimModule
        {
            public StubModule(string id, params string[] requires)
            {
                Id = id;
                Requires = requires;
            }
            public string Id { get; }
            public IReadOnlyList<string> Requires { get; }
            public void Register(SimBuilder builder) { }
        }

        static string[] SortedIds(params ISimModule[] modules) => ModuleOrder.Sort(modules).ConvertAll(m => m.Id).ToArray();

        [Test]
        public void ModuleOrder_PlacesRequirementsFirst_StableOtherwise()
        {
            // Among ready modules the one added first wins: "agents" was added before "mod".
            CollectionAssert.AreEqual(new[] { "world", "economy", "agents", "mod" },
                SortedIds(new StubModule("agents", "economy"), new StubModule("economy", "world"),
                    new StubModule("world"), new StubModule("mod", "world")));
            CollectionAssert.AreEqual(new[] { "b", "a" }, SortedIds(new StubModule("b"), new StubModule("a")));
        }

        [Test]
        public void ModuleOrder_RejectsMissingAndCyclicRequirements()
        {
            StringAssert.Contains("'missing'", Assert.Throws<System.InvalidOperationException>(
                () => ModuleOrder.Sort(new ISimModule[] { new StubModule("a", "missing") })).Message);
            StringAssert.Contains("cycle", Assert.Throws<System.InvalidOperationException>(
                () => ModuleOrder.Sort(new ISimModule[] { new StubModule("a", "b"), new StubModule("b", "a") })).Message);
        }

        [Test]
        public void SimBuilder_ReplacesAndRemovesSystemsById()
        {
            var log = new List<string>();
            using var world = new WorldState();
            var builder = new SimBuilder(world, new ServiceRegistry());
            var replacement = new RecordingSystem("x", SimPhase.Markets, 0, log);
            builder.ReplaceSystem(replacement);
            builder.RemoveSystem("y");
            builder.AddSystem(new RecordingSystem("x", SimPhase.Markets, 0, log));
            builder.AddSystem(new RecordingSystem("y", SimPhase.Markets, 0, log));
            builder.AddSystem(new RecordingSystem("z", SimPhase.Markets, 0, log));
            var systems = builder.BuildPipeline().Systems;
            Assert.AreEqual(2, systems.Count);
            Assert.AreSame(replacement, systems[0]);
            Assert.AreEqual("z", systems[1].Id);

            var unknown = new SimBuilder(world, new ServiceRegistry());
            unknown.RemoveSystem("nope");
            Assert.Throws<System.InvalidOperationException>(() => unknown.BuildPipeline());
        }

        sealed class Ping : ISimEvent
        {
        }

        [Test]
        public void EventBus_DeliversAtDispatch_InPublishOrder()
        {
            var bus = new EventBus();
            int received = 0;
            bus.Subscribe<Ping>(_ => received++);
            bus.Publish(new Ping());
            bus.Publish(new Ping());
            Assert.AreEqual(0, received);
            bus.Dispatch();
            Assert.AreEqual(2, received);
            Assert.AreEqual(2, bus.TurnLog.Count);
        }
    }
}
