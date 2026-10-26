using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FES.Core
{
    /// <summary>Fixed turn phases. Systems in a phase run in (Order, Id) order; events dispatch after each phase.</summary>
    public enum SimPhase
    {
        Commands = 0,
        Lod = 1,
        Environment = 2,
        Production = 3,
        Agents = 4,
        Markets = 5,
        Finance = 6,
        Guidance = 7,
        Cleanup = 8,
    }

    /// <summary>
    /// Named Order bands inside a phase. Use these (or values between them) instead of bare numbers,
    /// e.g. accumulate flows at Early/Default and consume them at Late.
    /// </summary>
    public static class SimOrder
    {
        public const int Early = -100;
        public const int Default = 0;
        public const int Late = 100;
    }

    /// <summary>
    /// A unit of simulation behavior. Implement this, register it from an <see cref="ISimModule"/>,
    /// and the pipeline runs it; no existing system needs to change.
    /// Systems that own native memory may also implement IDisposable.
    /// </summary>
    public interface ISimSystem
    {
        /// <summary>
        /// Stable id, e.g. "economy.pricing". Convention: declare it as a const and derive the system's
        /// RNG stream from it with <see cref="DetHash.Of"/>, so renaming the system is a visible change.
        /// </summary>
        string Id { get; }
        SimPhase Phase { get; }
        int Order { get; }

        /// <summary>Called once after every module has registered. Resolve components and services here.</summary>
        void Initialize(SimContext ctx);

        void Execute(SimContext ctx);
    }

    /// <summary>A feature package: registers its components, services and systems.</summary>
    public interface ISimModule
    {
        /// <summary>Unique module id; the composition root rejects duplicates.</summary>
        string Id { get; }
        void Register(SimBuilder builder);
    }

    /// <summary>
    /// Implemented by systems that keep derived (non-saved) state which must be rebuilt after a save is
    /// loaded, so queries between turns see consistent data.
    /// </summary>
    public interface ILoadListener
    {
        void OnLoaded(SimContext ctx);
    }

    /// <summary>Everything a system may touch during a turn.</summary>
    public sealed class SimContext
    {
        public readonly WorldState World;
        public readonly ServiceRegistry Services;
        public readonly EventBus Events;
        public readonly CommandQueue Commands;
        public readonly TierSchedule Schedule;
        public readonly SimClock Clock;

        public SimContext(WorldState world, ServiceRegistry services, TierSchedule schedule, SimClock clock)
        {
            World = world;
            Services = services;
            Schedule = schedule;
            Clock = clock;
            Events = new EventBus();
            Commands = new CommandQueue();
        }

        public int Turn => Clock.Turn;
        public ulong Seed => Clock.Seed;

        public bool IsDue(WorldLevel level) => Schedule.IsDue(level, Turn);

        /// <summary>Deterministic random stream keyed by this turn, the caller's stream id and an entity.</summary>
        public DetRandom RandomFor(uint stream, int entity = 0, uint salt = 0) =>
            DetRandom.For(Seed, Turn, stream, entity, salt);
    }

    /// <summary>Collects module registrations, then produces the ordered pipeline.</summary>
    public sealed class SimBuilder
    {
        readonly List<ISimSystem> _systems = new List<ISimSystem>();
        readonly HashSet<string> _systemIds = new HashSet<string>(StringComparer.Ordinal);

        public readonly WorldState World;
        public readonly ServiceRegistry Services;

        public SimBuilder(WorldState world, ServiceRegistry services)
        {
            World = world;
            Services = services;
        }

        public void AddSystem(ISimSystem system)
        {
            if (!_systemIds.Add(system.Id))
                throw new InvalidOperationException($"System id '{system.Id}' is registered twice.");
            _systems.Add(system);
        }

        public TurnPipeline BuildPipeline() => new TurnPipeline(_systems);
    }

    public sealed class TurnPipeline : IDisposable
    {
        readonly ISimSystem[] _systems;
        readonly long[] _lastTicks;

        public TurnPipeline(IEnumerable<ISimSystem> systems)
        {
            var list = new List<ISimSystem>(systems);
            list.Sort((a, b) =>
            {
                int c = a.Phase.CompareTo(b.Phase);
                if (c != 0) return c;
                c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            _systems = list.ToArray();
            _lastTicks = new long[_systems.Length];
        }

        public IReadOnlyList<ISimSystem> Systems => _systems;

        /// <summary>Wall-clock cost of each system in the last turn (diagnostics only, not simulation state).</summary>
        public TimeSpan LastDuration(int systemIndex) => TimeSpan.FromTicks(_lastTicks[systemIndex] * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

        public void Initialize(SimContext ctx)
        {
            foreach (var s in _systems) s.Initialize(ctx);
        }

        /// <summary>Clears per-turn transient state and lets systems rebuild derived state after a load.</summary>
        public void NotifyLoaded(SimContext ctx)
        {
            ctx.Commands.Clear();
            ctx.Events.Reset();
            foreach (var s in _systems)
                if (s is ILoadListener listener) listener.OnLoaded(ctx);
        }

        public void Advance(SimContext ctx)
        {
            ctx.Events.BeginTurn();
            int i = 0;
            while (i < _systems.Length)
            {
                var phase = _systems[i].Phase;
                for (; i < _systems.Length && _systems[i].Phase == phase; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    _systems[i].Execute(ctx);
                    _lastTicks[i] = Stopwatch.GetTimestamp() - start;
                }
                ctx.Events.Dispatch();
            }
            ctx.Clock.Advance();
        }

        public void Dispose()
        {
            foreach (var s in _systems)
                if (s is IDisposable d) d.Dispose();
        }
    }
}
