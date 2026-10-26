using System;
using System.Collections.Generic;
using System.Globalization;
using FES.Core;

namespace FES.Data
{
    /// <summary>
    /// Tunable numbers loaded from config.csv (key,value). Every key has a default, so the file only
    /// needs the values that differ; unknown keys are load errors. Integers only: permille values are
    /// converted to Fx here. Gameplay numbers belong here, not in system code.
    /// </summary>
    public sealed class SimulationConfig
    {
        // Every key's effective value (file value or code default), normalized; the source of ComputeHash.
        readonly SortedDictionary<string, string> _effective = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public readonly TierCadence[] TierCadences =
        {
            new TierCadence(4, 0), // Continent
            new TierCadence(4, 1), // Nation
            new TierCadence(2, 0), // Region
            new TierCadence(1, 0), // City
            new TierCadence(1, 0), // Village
        };

        /// <summary>Price multiplier per quality grade, applied to goods.csv base_price (Mid = 1.0).</summary>
        public readonly Fx[] GradePriceMultiplier =
        {
            Fx.FromPermille(600), Fx.One, Fx.FromPermille(1800), Fx.FromPermille(3500),
        };

        /// <summary>Weekly base demand weight per market level (village = 1.0).</summary>
        public readonly Fx[] LevelDemandWeight =
        {
            Fx.FromInt(40), Fx.FromInt(20), Fx.FromInt(8), Fx.FromInt(4), Fx.One,
        };

        /// <summary>Merchant groups spawned per node of each level.</summary>
        public readonly int[] MerchantsPerNode = { 50, 50, 30, 10, 3 };

        /// <summary>Starting cash of one merchant group, per level of its home node.</summary>
        public readonly long[] MerchantStartCash = { 5000, 4000, 3000, 2000, 1000 };

        /// <summary>[level, industry tier] producer groups spawned per node.</summary>
        public readonly int[,] ProducersPerNode = new int[WorldLevels.Count, IndustryTiers.Count];

        /// <summary>Weekly output variance of detailed facilities, +- permille around the expected value.</summary>
        public int ProductionVariancePermille = 100;

        public Fx PriceAdjustRate = Fx.FromPermille(250);
        public Fx PriceMaxStep = Fx.FromPermille(150);
        public Fx PriceAnchorRate = Fx.FromPermille(20);
        public Fx StockPressure = Fx.FromPermille(250);
        public int MinPrice = 1;

        public Fx MintRatePerYear = Fx.FromPermille(30);
        public long InitialGoldReserve = 1_000_000;
        public long InitialSilverReserve = 10_000_000;
        public long InitialCoinSupply = 5_000_000;
        public long CoinsPerGoldUnit = 10;
        public long CoinsPerSilverUnit = 1;
        /// <summary>Continents' starting metal reserves vary by +- this permille.</summary>
        public int CurrencyEndowmentSpreadPermille = 300;

        public Fx GuidanceDecayPerTurn = Fx.FromPermille(10);
        public Fx GuidanceWeightBuyFacility = Fx.One;
        public Fx GuidanceWeightFoundCompany = Fx.FromInt(5);

        /// <summary>The default LOD policy simulates the player's ancestor at this level (and its subtree) in detail.</summary>
        public WorldLevel LodFocusLevel = WorldLevel.Region;

        public string PlayerStartNode = string.Empty;
        public long PlayerStartCash = 500;

        SimulationConfig()
        {
            ProducersPerNode[(int)WorldLevel.Village, (int)IndustryTier.Primary] = 4;
            ProducersPerNode[(int)WorldLevel.Village, (int)IndustryTier.Tertiary] = 2;
            ProducersPerNode[(int)WorldLevel.City, (int)IndustryTier.Secondary] = 4;
        }

        /// <summary>Code defaults only.</summary>
        public static SimulationConfig Defaults() => Load(null);

        public static SimulationConfig Load(CsvTable table)
        {
            var config = new SimulationConfig();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (table != null)
                for (int r = 0; r < table.RowCount; r++)
                    values[table.GetString(r, "key")] = table.GetString(r, "value");
            config.Apply(values);
            foreach (var key in values.Keys)
                if (!config._effective.ContainsKey(key))
                    throw new FormatException($"config.csv: unknown key '{key}'.");
            return config;
        }

        /// <summary>Every key with its effective value (file or default), sorted by key.</summary>
        public IReadOnlyDictionary<string, string> EffectiveValues => _effective;

        /// <summary>
        /// Hash of the effective values, so a changed code default changes the data hash just like an
        /// edited file, while comments, row order and explicitly restated defaults do not.
        /// </summary>
        public ulong ComputeHash()
        {
            ulong hash = DetHash.Of(GameDatabase.ConfigFile);
            foreach (var entry in _effective)
                hash = DetHash.Combine(hash, DetHash.Combine(DetHash.Of(entry.Key), DetHash.Of(entry.Value)));
            return hash;
        }

        void Apply(Dictionary<string, string> v)
        {
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                string level = ((WorldLevel)l).ToString().ToLowerInvariant();
                TierCadences[l] = new TierCadence(
                    Int(v, $"tier.{level}.period", TierCadences[l].Period),
                    Int(v, $"tier.{level}.offset", TierCadences[l].Offset));
                LevelDemandWeight[l] = Permille(v, $"demand.{level}.weight_permille", LevelDemandWeight[l]);
                MerchantsPerNode[l] = Int(v, $"merchants.{level}", MerchantsPerNode[l]);
                MerchantStartCash[l] = Long(v, $"merchants.{level}.start_cash", MerchantStartCash[l]);
                for (int t = 0; t < IndustryTiers.Count; t++)
                {
                    string tier = ((IndustryTier)t).ToString().ToLowerInvariant();
                    ProducersPerNode[l, t] = Int(v, $"producers.{level}.{tier}", ProducersPerNode[l, t]);
                }
            }
            for (int g = 0; g < QualityGrades.Count; g++)
            {
                string grade = ((QualityGrade)g).ToString().ToLowerInvariant();
                GradePriceMultiplier[g] = Permille(v, $"grade.{grade}.price_permille", GradePriceMultiplier[g]);
            }
            ProductionVariancePermille = Int(v, "production.variance_permille", ProductionVariancePermille);
            PriceAdjustRate = Permille(v, "price.adjust_permille", PriceAdjustRate);
            PriceMaxStep = Permille(v, "price.max_step_permille", PriceMaxStep);
            PriceAnchorRate = Permille(v, "price.anchor_permille", PriceAnchorRate);
            StockPressure = Permille(v, "price.stock_pressure_permille", StockPressure);
            MinPrice = Int(v, "price.min", MinPrice);
            MintRatePerYear = Permille(v, "currency.mint_permille", MintRatePerYear);
            InitialGoldReserve = Long(v, "currency.gold_reserve", InitialGoldReserve);
            InitialSilverReserve = Long(v, "currency.silver_reserve", InitialSilverReserve);
            InitialCoinSupply = Long(v, "currency.coin_supply", InitialCoinSupply);
            CoinsPerGoldUnit = Long(v, "currency.coins_per_gold", CoinsPerGoldUnit);
            CoinsPerSilverUnit = Long(v, "currency.coins_per_silver", CoinsPerSilverUnit);
            CurrencyEndowmentSpreadPermille = Int(v, "currency.endowment_spread_permille", CurrencyEndowmentSpreadPermille);
            GuidanceDecayPerTurn = Permille(v, "guidance.decay_permille", GuidanceDecayPerTurn);
            GuidanceWeightBuyFacility = Permille(v, "guidance.weight.buy_facility_permille", GuidanceWeightBuyFacility);
            GuidanceWeightFoundCompany = Permille(v, "guidance.weight.found_company_permille", GuidanceWeightFoundCompany);
            if (v.TryGetValue("lod.focus_level", out var focus))
                LodFocusLevel = CsvTable.TryParseEnumName(focus, out WorldLevel level)
                    ? level
                    : throw new FormatException($"config.csv: lod.focus_level '{focus}' is not a WorldLevel.");
            Record("lod.focus_level", LodFocusLevel.ToString());
            PlayerStartNode = v.TryGetValue("player.start_node", out var start) ? start : PlayerStartNode;
            Record("player.start_node", PlayerStartNode);
            PlayerStartCash = Long(v, "player.start_cash", PlayerStartCash);
        }

        void Record(string key, string value) => _effective[key] = value;

        int Int(Dictionary<string, string> v, string key, int fallback)
        {
            long value = Long(v, key, fallback);
            if (value < int.MinValue || value > int.MaxValue)
                throw new FormatException($"config.csv: '{key}' = {value} is out of int range.");
            return (int)value;
        }

        long Long(Dictionary<string, string> v, string key, long fallback)
        {
            long value = v.TryGetValue(key, out var s) ? Parse(key, s) : fallback;
            Record(key, value.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        // Recorded as the raw Fx value, so a file value of 1000 and the default Fx.One hash the same.
        Fx Permille(Dictionary<string, string> v, string key, Fx fallback)
        {
            var value = v.TryGetValue(key, out var s) ? Fx.FromPermille(Parse(key, s)) : fallback;
            Record(key, "fx:" + value.Raw.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        static long Parse(string key, string s) =>
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : throw new FormatException($"config.csv: '{key}' = '{s}' is not an integer.");
    }
}
