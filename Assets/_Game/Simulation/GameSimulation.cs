using System;
using System.Collections.Generic;
using System.IO;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Persistence;
using FES.World;

namespace FES.Simulation
{
    /// <summary>
    /// Facade over one running world: the only entry point presentation code needs.
    /// Not thread-safe; drive it from one thread and read state between turns.
    /// </summary>
    public sealed class GameSimulation : IDisposable
    {
        readonly TurnPipeline _pipeline;
        readonly ulong _dataHash;

        public readonly SimContext Context;
        public readonly GameDatabase Database;
        public readonly MigrationRegistry Migrations = new MigrationRegistry();

        internal GameSimulation(SimContext context, TurnPipeline pipeline, GameDatabase database, ulong dataHash)
        {
            Context = context;
            _pipeline = pipeline;
            Database = database;
            _dataHash = dataHash;
        }

        public WorldState World => Context.World;
        public int Turn => Context.Turn;
        public IReadOnlyList<ISimSystem> Systems => _pipeline.Systems;
        public IReadOnlyList<ISimEvent> LastTurnEvents => Context.Events.TurnLog;

        public TimeSpan LastSystemDuration(int systemIndex) => _pipeline.LastDuration(systemIndex);

        public void Enqueue(ICommand command) => Context.Commands.Enqueue(command);

        public void AdvanceTurn() => _pipeline.Advance(Context);

        public ulong ComputeHash() => World.ComputeHash();

        public void Save(Stream stream)
        {
            var header = new SaveHeader { DataHash = _dataHash, Seed = Context.Seed, Turn = Context.Turn };
            SaveSerializer.Write(stream, header, World.PersistentComponents);
        }

        /// <summary>
        /// Loads a save into this simulation, replacing all persistent state (including the seed).
        /// It must have been built from the same definitions and map. Atomic: if anything fails, the
        /// previous state is restored and the exception is rethrown. Queued commands and undelivered
        /// events are discarded, and derived state (LOD, modifier multipliers) is rebuilt.
        /// </summary>
        public void Load(Stream stream)
        {
            using var backup = new MemoryStream();
            Save(backup);
            try
            {
                SaveSerializer.Read(stream, World, _dataHash, Migrations);
            }
            catch
            {
                backup.Position = 0;
                SaveSerializer.Read(backup, World, _dataHash, Migrations);
                throw;
            }
            _pipeline.NotifyLoaded(Context);
        }

        public void Dispose()
        {
            _pipeline.Dispose();
            World.Dispose();
        }
    }

    /// <summary>
    /// Composition root. Modules register in order (world, economy, agents by default); add your own
    /// with <see cref="AddModule"/> and override default services with <see cref="ConfigureServices"/>.
    /// </summary>
    public sealed class SimulationBuilder
    {
        readonly GameDatabase _database;
        readonly MapDef _map;
        readonly ulong _seed;
        readonly List<ISimModule> _modules = new List<ISimModule>();
        readonly HashSet<string> _moduleIds = new HashSet<string>(StringComparer.Ordinal);
        readonly List<Action<ServiceRegistry>> _serviceOverrides = new List<Action<ServiceRegistry>>();

        public SimulationBuilder(GameDatabase database, MapDef map, ulong seed)
        {
            _database = database;
            _map = map;
            _seed = seed;
        }

        public static IEnumerable<ISimModule> DefaultModules()
        {
            yield return new WorldModule();
            yield return new EconomyModule();
            yield return new AgentsModule();
        }

        /// <summary>Loads definitions and the CSV map from a data source and adds the default modules.</summary>
        public static SimulationBuilder FromData(IDataSource source, ulong seed) =>
            new SimulationBuilder(GameDatabase.Load(source), new CsvMapSource(source).LoadMap(), seed).WithDefaultModules();

        /// <summary>Adds the default modules that are not added yet (safe to call more than once).</summary>
        public SimulationBuilder WithDefaultModules()
        {
            foreach (var module in DefaultModules())
                if (!_moduleIds.Contains(module.Id)) AddModule(module);
            return this;
        }

        /// <summary>Appends a module. Modules register in the order added; ids must be unique.</summary>
        public SimulationBuilder AddModule(ISimModule module)
        {
            if (!_moduleIds.Add(module.Id))
                throw new InvalidOperationException($"Module '{module.Id}' is added twice.");
            _modules.Add(module);
            return this;
        }

        /// <summary>Runs before modules register, so services set here win over module defaults.</summary>
        public SimulationBuilder ConfigureServices(Action<ServiceRegistry> configure)
        {
            _serviceOverrides.Add(configure);
            return this;
        }

        public GameSimulation Build()
        {
            var world = new WorldState();
            TurnPipeline pipeline = null;
            try
            {
                var services = new ServiceRegistry();
                services.Set(_database);
                services.Set(_map);
                foreach (var configure in _serviceOverrides) configure(services);

                var clock = world.Add(new SimClock(_seed));
                var builder = new SimBuilder(world, services);
                builder.AddSystem(new CommandSystem());
                foreach (var module in _modules) module.Register(builder);

                pipeline = builder.BuildPipeline();
                var context = new SimContext(world, services, new TierSchedule(_database.Config.TierCadences), clock);
                pipeline.Initialize(context);
                return new GameSimulation(context, pipeline, _database, DetHash.Combine(_database.ContentHash, _map.ComputeHash()));
            }
            catch
            {
                pipeline?.Dispose();
                world.Dispose();
                throw;
            }
        }
    }
}
