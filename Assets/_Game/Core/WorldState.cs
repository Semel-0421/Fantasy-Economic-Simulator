using System;
using System.Collections.Generic;
using System.IO;

namespace FES.Core
{
    /// <summary>
    /// A block of simulation state owned by the <see cref="WorldState"/>.
    /// Components that own native memory also implement IDisposable; the world disposes them.
    /// </summary>
    public interface IWorldComponent
    {
    }

    /// <summary>State that is saved, loaded and included in the determinism hash.</summary>
    public interface IPersistentComponent : IWorldComponent
    {
        /// <summary>Stable save-section key, e.g. "economy.markets". Never rename once shipped.</summary>
        string Key { get; }

        /// <summary>Schema version of this section. Bump it and register a migration when the layout changes.</summary>
        int Version { get; }

        void Write(BinaryWriter writer);
        void Read(BinaryReader reader);
    }

    /// <summary>
    /// Upgrades one save section by one version: FromVersion -> FromVersion + 1. The module that owns the
    /// component registers it with <see cref="SimBuilder.AddMigration"/>.
    /// </summary>
    public interface ISaveMigration
    {
        string SectionKey { get; }
        int FromVersion { get; }
        byte[] Migrate(byte[] section);
    }

    /// <summary>
    /// Typed container for every piece of simulation state. Not an ECS: each component is a
    /// struct-of-arrays table registered once by its module. Systems read and write components;
    /// they never call each other.
    /// </summary>
    public sealed class WorldState : IDisposable
    {
        readonly Dictionary<Type, IWorldComponent> _byType = new Dictionary<Type, IWorldComponent>();
        readonly List<IWorldComponent> _ordered = new List<IWorldComponent>();
        readonly SortedDictionary<string, IPersistentComponent> _persistent =
            new SortedDictionary<string, IPersistentComponent>(StringComparer.Ordinal);

        public T Add<T>(T component) where T : class, IWorldComponent
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (_byType.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"Component {typeof(T).Name} is already registered.");
            if (component is IPersistentComponent p)
            {
                if (_persistent.ContainsKey(p.Key))
                    throw new InvalidOperationException($"Save key '{p.Key}' is already used.");
                _persistent.Add(p.Key, p);
            }
            _byType.Add(typeof(T), component);
            _ordered.Add(component);
            return component;
        }

        public T Get<T>() where T : class, IWorldComponent
        {
            if (_byType.TryGetValue(typeof(T), out var c)) return (T)c;
            throw new KeyNotFoundException($"Component {typeof(T).Name} is not registered. Check module order.");
        }

        public bool TryGet<T>(out T component) where T : class, IWorldComponent
        {
            if (_byType.TryGetValue(typeof(T), out var c))
            {
                component = (T)c;
                return true;
            }
            component = null;
            return false;
        }

        /// <summary>Persistent components ordered by key (ordinal), the canonical save and hash order.</summary>
        public IEnumerable<IPersistentComponent> PersistentComponents => _persistent.Values;

        public bool TryGetPersistent(string key, out IPersistentComponent component) =>
            _persistent.TryGetValue(key, out component);

        /// <summary>FNV-1a hash of every persistent component's serialized bytes.</summary>
        public ulong ComputeHash()
        {
            using var stream = new HashingStream();
            using var writer = new BinaryWriter(stream);
            foreach (var p in _persistent.Values)
            {
                writer.Write(p.Key);
                writer.Write(p.Version);
                p.Write(writer);
            }
            writer.Flush();
            return stream.Hash;
        }

        public void Dispose()
        {
            for (int i = _ordered.Count - 1; i >= 0; i--)
                if (_ordered[i] is IDisposable disposable) disposable.Dispose();
            _ordered.Clear();
            _byType.Clear();
            _persistent.Clear();
        }
    }

    /// <summary>Write-only stream that accumulates an FNV-1a 64-bit hash.</summary>
    public sealed class HashingStream : Stream
    {
        ulong _hash = DetHash.FnvOffset;
        long _length;

        public ulong Hash => _hash;

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++) _hash = DetHash.FnvStep(_hash, buffer[i]);
            _length += count;
        }

        public override void WriteByte(byte value)
        {
            _hash = DetHash.FnvStep(_hash, value);
            _length++;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
