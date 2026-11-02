using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FES.Core;

namespace FES.Persistence
{
    public sealed class SaveHeader
    {
        public int FormatVersion;
        /// <summary>Hash of the definitions and map the save was made with.</summary>
        public ulong DataHash;
        public ulong Seed;
        public int Turn;
    }

    public sealed class MigrationRegistry
    {
        readonly Dictionary<(string, int), ISaveMigration> _migrations = new Dictionary<(string, int), ISaveMigration>();

        public void Register(ISaveMigration migration) => _migrations.Add((migration.SectionKey, migration.FromVersion), migration);

        public byte[] Upgrade(string key, int fromVersion, int toVersion, byte[] section)
        {
            for (int v = fromVersion; v < toVersion; v++)
            {
                if (!_migrations.TryGetValue((key, v), out var migration))
                    throw new InvalidDataException($"No migration for section '{key}' from version {v}.");
                section = migration.Migrate(section);
            }
            return section;
        }
    }

    /// <summary>
    /// Binary save container: header, then one length-prefixed section per persistent component
    /// (key, version, bytes). Unknown sections are skipped; older sections are migrated; sections
    /// missing from the file keep the state the component holds, so only a newly built world can take them.
    /// </summary>
    public static class SaveSerializer
    {
        const uint Magic = 0x53454646; // "FFES"
        public const int FormatVersion = 1;

        public static void Write(Stream stream, SaveHeader header, IEnumerable<IPersistentComponent> components)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(header.DataHash);
            writer.Write(header.Seed);
            writer.Write(header.Turn);

            var sections = new List<IPersistentComponent>(components);
            writer.Write(sections.Count);
            foreach (var component in sections)
            {
                using var buffer = new MemoryStream();
                using (var sectionWriter = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                    component.Write(sectionWriter);
                writer.Write(component.Key);
                writer.Write(component.Version);
                writer.Write((int)buffer.Length);
                writer.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }

        public static SaveHeader ReadHeader(BinaryReader reader)
        {
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a save file.");
            var header = new SaveHeader { FormatVersion = reader.ReadInt32() };
            if (header.FormatVersion != FormatVersion)
                throw new InvalidDataException($"Unsupported save format {header.FormatVersion}.");
            header.DataHash = reader.ReadUInt64();
            header.Seed = reader.ReadUInt64();
            header.Turn = reader.ReadInt32();
            return header;
        }

        /// <summary>
        /// Reads a save into an already-built world. The whole file is parsed, version-checked and migrated
        /// before any component is touched, so malformed or incompatible saves leave the world unchanged.
        /// (A component's own Read can still fail mid-apply; <c>GameSimulation.Load</c> rolls back in that case.)
        /// </summary>
        /// <param name="expectedDataHash">The running build's data hash; a mismatch is rejected.</param>
        /// <param name="requireEverySection">Rejects a file without a section for every component of the world.</param>
        public static SaveHeader Read(Stream stream, WorldState world, ulong expectedDataHash, MigrationRegistry migrations, bool requireEverySection = false)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var header = ReadHeader(reader);
            // TODO: remap definition ids by key so saves survive data changes, instead of rejecting them.
            if (header.DataHash != expectedDataHash)
                throw new InvalidDataException("Save was created with different game data.");

            var staged = new List<(IPersistentComponent component, byte[] bytes)>();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) StageSection(reader, world, migrations, staged);
            if (requireEverySection) CheckEverySection(world, staged);

            foreach (var (component, bytes) in staged)
            {
                using var sectionReader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
                component.Read(sectionReader);
                if (sectionReader.BaseStream.Position != bytes.Length)
                    throw new InvalidDataException($"Section '{component.Key}' has bytes left over after reading.");
            }
            return header;
        }

        static void StageSection(BinaryReader reader, WorldState world, MigrationRegistry migrations, List<(IPersistentComponent component, byte[] bytes)> staged)
        {
            string key = reader.ReadString();
            int version = reader.ReadInt32();
            int length = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException($"Section '{key}' is truncated.");
            if (!world.TryGetPersistent(key, out var component)) return;
            if (version > component.Version)
                throw new InvalidDataException($"Section '{key}' version {version} is newer than this build ({component.Version}).");
            if (version < component.Version) bytes = migrations.Upgrade(key, version, component.Version, bytes);
            staged.Add((component, bytes));
        }

        static void CheckEverySection(WorldState world, List<(IPersistentComponent component, byte[] bytes)> staged)
        {
            foreach (var component in world.PersistentComponents)
                if (!staged.Exists(s => s.component == component))
                    throw new InvalidDataException($"The save has no section '{component.Key}': only a newly built simulation can load it.");
        }
    }
}
