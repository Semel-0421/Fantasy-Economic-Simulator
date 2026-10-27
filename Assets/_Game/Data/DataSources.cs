using System;
using System.Collections.Generic;
using System.IO;

namespace FES.Data
{
    /// <summary>Where definition files come from. Keeps loaders independent of Unity paths and platforms.</summary>
    public interface IDataSource
    {
        bool Exists(string name);
        string ReadText(string name);
    }

    /// <summary>Every file a data folder can hold. Sources that cannot list folders (Android APK) fetch these.</summary>
    public static class DataFiles
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            GameDatabase.GoodsFile, GameDatabase.RecipesFile, GameDatabase.FacilitiesFile, GameDatabase.EventsFile,
            GameDatabase.MarketProfilesFile, GameDatabase.ConfigFile, GameDatabase.SpecialtiesFile,
            CsvMapSource.NodesFile, CsvMapSource.RoutesFile,
        };
    }

    public sealed class FileDataSource : IDataSource
    {
        readonly string _root;

        public FileDataSource(string root) => _root = root;

        public bool Exists(string name) => File.Exists(Path.Combine(_root, name));
        public string ReadText(string name) => File.ReadAllText(Path.Combine(_root, name));
    }

    public sealed class InMemoryDataSource : IDataSource
    {
        readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

        public InMemoryDataSource Add(string name, string text)
        {
            _files[name] = text;
            return this;
        }

        public bool Exists(string name) => _files.ContainsKey(name);

        public string ReadText(string name) =>
            _files.TryGetValue(name, out var text) ? text : throw new FileNotFoundException(name);
    }
}
