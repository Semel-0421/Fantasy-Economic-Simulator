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
        public const string SpecialtiesFile = "specialties.csv";

        readonly Dictionary<string, int> _categoryByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _baseGoodByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly List<GoodCategoryDef> Categories = new List<GoodCategoryDef>();
        /// <summary>Indexed by <see cref="GoodId.Value"/>.</summary>
        public readonly List<GoodDef> Goods = new List<GoodDef>();
        public readonly List<RecipeDef> Recipes = new List<RecipeDef>();
        public readonly List<FacilityDef> Facilities = new List<FacilityDef>();
        public readonly List<WorldEventDef> Events = new List<WorldEventDef>();
        public readonly List<MarketProfileRule> MarketProfiles = new List<MarketProfileRule>();
        readonly Dictionary<string, SpecialtyRule> _specialtiesByTag = new Dictionary<string, SpecialtyRule>(StringComparer.Ordinal);

        public SimulationConfig Config { get; private set; }

        /// <summary>
        /// Hash of every definition file's text plus the effective config values (defaults included).
        /// Saves refuse to load against different data.
        /// </summary>
        public ulong ContentHash { get; private set; }

        public int GoodCount => Goods.Count;
        public int BaseGoodCount => _baseGoodByKey.Count;

        /// <summary>The specialties a terrain tag gives; false for a tag specialties.csv does not list.</summary>
        public bool TryGetSpecialty(string tag, out SpecialtyRule rule) => _specialtiesByTag.TryGetValue(tag, out rule);

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
            db.LoadSpecialties(Read(SpecialtiesFile, false));
            db.ContentHash = hash;
            return db;
        }

        const string FoodCategory = "provisions";
        const string DrinkCategory = "drink";

        static int RoundedShare(int amount, Fx share) => (int)((amount * share.Raw + Fx.OneRaw / 2) / Fx.OneRaw);

        void LoadGoods(CsvTable t)
        {
            for (int r = 0; r < t.RowCount; r++)
            {
                string key = t.GetString(r, "key");
                string category = t.GetString(r, "category");
                int categoryIndex = CategoryIndexOf(category);
                if (_baseGoodByKey.ContainsKey(key)) throw new FormatException($"goods.csv: duplicate key '{key}'.");
                int baseIndex = _baseGoodByKey.Count;
                _baseGoodByKey.Add(key, baseIndex);

                long basePrice = t.GetLong(r, "base_price");
                int baseDemand = t.GetIntOr(r, "base_demand", 0);
                var traits = ReadTraits(t, r);
                // Foods and drink keep their own grade prices, and their demand splits by grade, the rest to mid (DEC-090, DEC-091).
                bool food = (category == FoodCategory || category == DrinkCategory) && Config.FoodGradeDemand[(int)QualityGrade.Mid].Raw > 0;
                for (int g = 0; g < QualityGrades.Count; g++)
                {
                    long price = GradePrice(basePrice, g, food);
                    Goods.Add(new GoodDef(GoodId.From(baseIndex, (QualityGrade)g), key, categoryIndex,
                        (int)Math.Min(int.MaxValue, price), GradeDemand(baseDemand, g, food), traits));
                }
            }
        }

        int CategoryIndexOf(string category)
        {
            if (_categoryByKey.TryGetValue(category, out int index)) return index;
            index = Categories.Count;
            _categoryByKey.Add(category, index);
            Categories.Add(new GoodCategoryDef(index, category));
            return index;
        }

        static GoodTraits ReadTraits(CsvTable t, int r)
        {
            int weight = t.GetIntOr(r, "weight", 1);
            int size = t.GetIntOr(r, "size", 1);
            int license = t.GetIntOr(r, "requires_license", 0);
            if (license != 0 && license != 1) throw new FormatException($"goods.csv row {r + 1}: requires_license must be 0 or 1.");
            if (weight < 0 || size < 0) throw new FormatException($"goods.csv row {r + 1}: weight and size must not be negative.");
            string goodClass = t.GetStringOr(r, "class", "necessity");
            if (goodClass.Length == 0) goodClass = "necessity";
            if (goodClass != "necessity" && goodClass != "luxury" && goodClass != "semi")
                throw new FormatException($"goods.csv row {r + 1}: class must be necessity, semi or luxury, not '{goodClass}'.");
            return new GoodTraits(weight, size, license == 1, goodClass == "luxury", goodClass == "semi", t.GetIntOr(r, "made_to_order", 0) != 0);
        }

        long GradePrice(long basePrice, int grade, bool food) =>
            Math.Max(1, (food ? Config.FoodGradePrice[grade] : Config.GradePriceMultiplier[grade]).MulInt(basePrice));

        int GradeDemand(int baseDemand, int grade, bool food)
        {
            if (!food) return baseDemand;
            if (grade != (int)QualityGrade.Mid) return RoundedShare(baseDemand, Config.FoodGradeDemand[grade]);
            int otherDemand = 0;
            for (int g = 0; g < QualityGrades.Count; g++)
                if (g != (int)QualityGrade.Mid) otherDemand += RoundedShare(baseDemand, Config.FoodGradeDemand[g]);
            return Math.Max(0, baseDemand - otherDemand);
        }

        void LoadRecipesAndFacilities(CsvTable recipes, CsvTable facilities)
        {
            var recipeByKey = LoadRecipes(recipes);
            if (facilities != null) LoadFacilities(facilities, recipeByKey);
        }

        Dictionary<string, RecipeDef> LoadRecipes(CsvTable recipes)
        {
            var byKey = new Dictionary<string, RecipeRows>(StringComparer.Ordinal);
            var order = new List<string>();
            for (int r = 0; recipes != null && r < recipes.RowCount; r++)
                ReadRecipeRow(recipes, r, byKey, order);
            var recipeByKey = new Dictionary<string, RecipeDef>(StringComparer.Ordinal);
            foreach (var key in order)
            {
                var def = new RecipeDef(Recipes.Count, key, byKey[key].Inputs.ToArray(), byKey[key].Outputs.ToArray(), byKey[key].Aids.ToArray());
                Recipes.Add(def);
                recipeByKey.Add(key, def);
            }
            return recipeByKey;
        }

        sealed class RecipeRows
        {
            public readonly List<RecipeLine> Inputs = new List<RecipeLine>(), Outputs = new List<RecipeLine>(), Aids = new List<RecipeLine>();
        }

        void ReadRecipeRow(CsvTable recipes, int r, Dictionary<string, RecipeRows> byKey, List<string> order)
        {
            string key = recipes.GetString(r, "recipe");
            if (!byKey.TryGetValue(key, out var lines))
            {
                byKey.Add(key, lines = new RecipeRows());
                order.Add(key);
            }
            string goodKey = recipes.GetString(r, "good");
            var grade = recipes.GetEnum<QualityGrade>(r, "grade");
            if (!TryGetGood(goodKey, grade, out var good))
                throw new FormatException($"recipes.csv row {r + 1}: unknown good '{goodKey}'.");
            var line = new RecipeLine(good, recipes.GetInt(r, "qty"));
            if (line.Quantity < 0) throw new FormatException($"recipes.csv row {r + 1}: qty must not be negative.");
            string role = recipes.GetString(r, "role");
            var roleLines = role == "in" ? lines.Inputs
                : role == "out" ? lines.Outputs
                : role == "aid" ? lines.Aids
                : throw new FormatException($"recipes.csv row {r + 1}: role must be 'in', 'aid' or 'out'.");
            if (roleLines.Exists(l => l.Good.Value == good.Value))
                throw new FormatException($"recipes.csv row {r + 1}: '{goodKey}' is listed twice as '{role}' in recipe '{key}'.");
            roleLines.Add(line);
        }

        void LoadFacilities(CsvTable facilities, Dictionary<string, RecipeDef> recipeByKey)
        {
            var facilityKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int r = 0; r < facilities.RowCount; r++)
            {
                string key = facilities.GetString(r, "key");
                string recipeKey = facilities.GetString(r, "recipe");
                if (!recipeByKey.TryGetValue(recipeKey, out var recipe))
                    throw new FormatException($"facilities.csv row {r + 1}: unknown recipe '{recipeKey}'.");
                if (!facilityKeys.Add(key)) throw new FormatException($"facilities.csv: duplicate key '{key}'.");
                Facilities.Add(new FacilityDef(Facilities.Count, key, facilities.GetEnum<IndustryTier>(r, "tier"), recipe,
                    facilities.GetIntOr(r, "state_owned", 0) != 0)
                {
                    Strategic = facilities.GetIntOr(r, "strategic", 0) != 0,
                    CoreOnly = facilities.GetIntOr(r, "core_only", 0) != 0,
                    Continent = string.IsNullOrEmpty(facilities.GetStringOr(r, "continent", "")) ? null : facilities.GetString(r, "continent"),
                });
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
                    new EventEffect(t.GetEnum<ModifierTarget>(r, "target"), Fx.FromPermille(t.GetLong(r, "mul_permille")), t.GetInt(r, "duration")),
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

        // tag,category,good: an empty good means every base good of the category. Rows of one tag add up.
        void LoadSpecialties(CsvTable t)
        {
            if (t == null) return;
            var byTag = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            for (int r = 0; r < t.RowCount; r++)
                ReadSpecialtyRow(t, r, byTag);
            foreach (var entry in byTag)
                _specialtiesByTag.Add(entry.Key, new SpecialtyRule(entry.Key, new List<int>(entry.Value).ToArray()));
        }

        void ReadSpecialtyRow(CsvTable t, int r, SortedDictionary<string, SortedSet<int>> byTag)
        {
            string tag = t.GetString(r, "tag");
            string category = t.GetString(r, "category");
            if (!_categoryByKey.TryGetValue(category, out int categoryIndex))
                throw new FormatException($"{t.Name} row {r + 1}: unknown category '{category}'.");
            if (!byTag.TryGetValue(tag, out var goods)) byTag.Add(tag, goods = new SortedSet<int>());
            string goodKey = t.GetStringOr(r, "good", string.Empty);
            if (goodKey.Length == 0)
            {
                for (int b = 0; b < BaseGoodCount; b++)
                    if (Goods[b * QualityGrades.Count].CategoryIndex == categoryIndex) goods.Add(b);
                return;
            }
            if (!_baseGoodByKey.TryGetValue(goodKey, out int baseIndex))
                throw new FormatException($"{t.Name} row {r + 1}: unknown good '{goodKey}'.");
            if (Goods[baseIndex * QualityGrades.Count].CategoryIndex != categoryIndex)
                throw new FormatException($"{t.Name} row {r + 1}: '{goodKey}' is not in category '{category}'.");
            goods.Add(baseIndex);
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
