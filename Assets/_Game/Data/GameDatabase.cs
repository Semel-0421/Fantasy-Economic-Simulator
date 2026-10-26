using System;
using System.Collections.Generic;
using FES.Core;

namespace FES.Data
{
    /// <summary>All static definitions, immutable after load. Registered as a service for every system.</summary>
    public sealed class GameDatabase
    {
        public const string GoodsFile = "goods.csv";
        public const string RecipesFile = "recipes.csv";
        public const string FacilitiesFile = "facilities.csv";
        public const string EventsFile = "events.csv";
        public const string MarketProfilesFile = "market_profiles.csv";
        public const string ConfigFile = "config.csv";

        readonly Dictionary<string, int> _categoryByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _baseGoodByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly List<GoodCategoryDef> Categories = new List<GoodCategoryDef>();
        /// <summary>Indexed by <see cref="GoodId.Value"/>.</summary>
        public readonly List<GoodDef> Goods = new List<GoodDef>();
        public readonly List<RecipeDef> Recipes = new List<RecipeDef>();
        public readonly List<FacilityDef> Facilities = new List<FacilityDef>();
        public readonly List<WorldEventDef> Events = new List<WorldEventDef>();
        public readonly List<MarketProfileRule> MarketProfiles = new List<MarketProfileRule>();

        public SimulationConfig Config { get; private set; }

        /// <summary>
        /// Hash of every definition file's text plus the effective config values (defaults included).
        /// Saves refuse to load against different data.
        /// </summary>
        public ulong ContentHash { get; private set; }

        public int GoodCount => Goods.Count;

        public GoodDef Good(GoodId id) => Goods[id.Value];

        public bool TryGetGood(string baseKey, QualityGrade grade, out GoodId id)
        {
            if (_baseGoodByKey.TryGetValue(baseKey, out int baseIndex))
            {
                id = GoodId.From(baseIndex, grade);
                return true;
            }
            id = GoodId.None;
            return false;
        }

        /// <summary>
        /// Line endings and a leading BOM are normalized before hashing, so the same data checked out
        /// with CRLF (Windows) or LF (Android build, re-clone) yields the same hash.
        /// </summary>
        public static ulong HashText(string file, string text)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
            return DetHash.Combine(DetHash.Of(file), DetHash.Of(normalized));
        }

        public static GameDatabase Load(IDataSource source)
        {
            var db = new GameDatabase();
            ulong hash = 0;
            CsvTable Read(string file, bool required, bool hashText = true)
            {
                if (!source.Exists(file))
                {
                    if (required) throw new System.IO.FileNotFoundException($"Required data file '{file}' is missing.");
                    return null;
                }
                string text = source.ReadText(file);
                if (hashText) hash = DetHash.Combine(hash, HashText(file, text));
                return CsvTable.Parse(file, text);
            }

            db.Config = SimulationConfig.Load(Read(ConfigFile, false, hashText: false));
            hash = DetHash.Combine(hash, db.Config.ComputeHash());
            db.LoadGoods(Read(GoodsFile, true));
            db.LoadRecipesAndFacilities(Read(RecipesFile, false), Read(FacilitiesFile, false));
            db.LoadEvents(Read(EventsFile, false));
            db.LoadMarketProfiles(Read(MarketProfilesFile, true));
            db.ContentHash = hash;
            return db;
        }

        void LoadGoods(CsvTable t)
        {
            for (int r = 0; r < t.RowCount; r++)
            {
                string key = t.GetString(r, "key");
                string category = t.GetString(r, "category");
                if (!_categoryByKey.TryGetValue(category, out int categoryIndex))
                {
                    categoryIndex = Categories.Count;
                    _categoryByKey.Add(category, categoryIndex);
                    Categories.Add(new GoodCategoryDef(categoryIndex, category));
                }
                if (_baseGoodByKey.ContainsKey(key)) throw new FormatException($"goods.csv: duplicate key '{key}'.");
                int baseIndex = _baseGoodByKey.Count;
                _baseGoodByKey.Add(key, baseIndex);

                long basePrice = t.GetLong(r, "base_price");
                int baseDemand = t.GetIntOr(r, "base_demand", 0);
                int weight = t.GetIntOr(r, "weight", 1);
                int size = t.GetIntOr(r, "size", 1);
                if (weight < 0 || size < 0) throw new FormatException($"goods.csv row {r + 1}: weight and size must not be negative.");
                for (int g = 0; g < QualityGrades.Count; g++)
                {
                    long price = Math.Max(1, Config.GradePriceMultiplier[g].MulInt(basePrice));
                    Goods.Add(new GoodDef(GoodId.From(baseIndex, (QualityGrade)g), key, categoryIndex,
                        (int)Math.Min(int.MaxValue, price), baseDemand, weight, size));
                }
            }
        }

        void LoadRecipesAndFacilities(CsvTable recipes, CsvTable facilities)
        {
            var byKey = new Dictionary<string, (List<RecipeLine> inputs, List<RecipeLine> outputs)>(StringComparer.Ordinal);
            var order = new List<string>();
            if (recipes != null)
            {
                for (int r = 0; r < recipes.RowCount; r++)
                {
                    string key = recipes.GetString(r, "recipe");
                    if (!byKey.TryGetValue(key, out var lines))
                    {
                        byKey.Add(key, lines = (new List<RecipeLine>(), new List<RecipeLine>()));
                        order.Add(key);
                    }
                    string goodKey = recipes.GetString(r, "good");
                    var grade = recipes.GetEnum<QualityGrade>(r, "grade");
                    if (!TryGetGood(goodKey, grade, out var good))
                        throw new FormatException($"recipes.csv row {r + 1}: unknown good '{goodKey}'.");
                    var line = new RecipeLine(good, recipes.GetInt(r, "qty"));
                    if (line.Quantity < 0) throw new FormatException($"recipes.csv row {r + 1}: qty must not be negative.");
                    string role = recipes.GetString(r, "role");
                    var roleLines = role == "in" ? lines.inputs
                        : role == "out" ? lines.outputs
                        : throw new FormatException($"recipes.csv row {r + 1}: role must be 'in' or 'out'.");
                    if (roleLines.Exists(l => l.Good.Value == good.Value))
                        throw new FormatException($"recipes.csv row {r + 1}: '{goodKey}' is listed twice as '{role}' in recipe '{key}'.");
                    roleLines.Add(line);
                }
            }
            var recipeByKey = new Dictionary<string, RecipeDef>(StringComparer.Ordinal);
            foreach (var key in order)
            {
                var def = new RecipeDef(Recipes.Count, key, byKey[key].inputs.ToArray(), byKey[key].outputs.ToArray());
                Recipes.Add(def);
                recipeByKey.Add(key, def);
            }

            if (facilities == null) return;
            var facilityKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int r = 0; r < facilities.RowCount; r++)
            {
                string key = facilities.GetString(r, "key");
                string recipeKey = facilities.GetString(r, "recipe");
                if (!recipeByKey.TryGetValue(recipeKey, out var recipe))
                    throw new FormatException($"facilities.csv row {r + 1}: unknown recipe '{recipeKey}'.");
                if (!facilityKeys.Add(key)) throw new FormatException($"facilities.csv: duplicate key '{key}'.");
                Facilities.Add(new FacilityDef(Facilities.Count, key, facilities.GetEnum<IndustryTier>(r, "tier"), recipe));
            }
        }

        void LoadEvents(CsvTable t)
        {
            if (t == null) return;
            for (int r = 0; r < t.RowCount; r++)
            {
                var def = new WorldEventDef(
                    Events.Count,
                    t.GetString(r, "key"),
                    t.GetEnum<WorldLevel>(r, "level"),
                    CategoryOrAll(t, r),
                    t.GetEnum<ModifierTarget>(r, "target"),
                    Fx.FromPermille(t.GetLong(r, "mul_permille")),
                    t.GetInt(r, "duration"),
                    t.GetInt(r, "chance_permille"));
                if (def.DurationTurns < 1) throw new FormatException($"events.csv row {r + 1}: duration must be >= 1.");

                // Supply/demand modifiers act on weekly flows, so any duration works. Value modifiers are read
                // only when a market reprices, so they must outlast the slowest tier they cover.
                int slowestPeriod = 1;
                for (int l = (int)def.ScopeLevel; l < WorldLevels.Count; l++)
                    slowestPeriod = Math.Max(slowestPeriod, Config.TierCadences[l].Period);
                if (def.Target == ModifierTarget.Value && def.DurationTurns < slowestPeriod)
                    throw new FormatException(
                        $"events.csv row {r + 1}: Value event '{def.Key}' lasts {def.DurationTurns} turns but its markets reprice every {slowestPeriod}.");
                Events.Add(def);
            }
        }

        void LoadMarketProfiles(CsvTable t)
        {
            for (int r = 0; r < t.RowCount; r++)
            {
                string grades = t.GetString(r, "grades");
                int mask = 0;
                if (grades == "*") mask = (1 << QualityGrades.Count) - 1;
                else
                    foreach (var g in grades.Split('|'))
                        mask |= CsvTable.TryParseEnumName(g.Trim(), out QualityGrade grade)
                            ? 1 << (int)grade
                            : throw new FormatException($"{t.Name} row {r + 1}: unknown grade '{g}'.");
                MarketProfiles.Add(new MarketProfileRule(t.GetEnum<WorldLevel>(r, "level"), CategoryOrAll(t, r), mask));
            }
        }

        int CategoryOrAll(CsvTable t, int row)
        {
            string category = t.GetString(row, "category");
            if (category == "*") return -1;
            if (!_categoryByKey.TryGetValue(category, out int index))
                throw new FormatException($"{t.Name} row {row + 1}: unknown category '{category}'.");
            return index;
        }
    }
}
