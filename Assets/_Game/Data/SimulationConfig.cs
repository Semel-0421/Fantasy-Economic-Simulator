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

        /// <summary>Cargo limits of one merchant group, per level of its home node (DEC-013).</summary>
        public readonly long[] MerchantMaxWeight = { 4000, 4000, 2000, 1000, 400 };
        public readonly long[] MerchantMaxSize = { 3200, 3200, 1600, 800, 300 };

        /// <summary>Weekly running cost of a detailed merchant group, per level of its home node.</summary>
        public readonly long[] MerchantUpkeep = { 50, 40, 30, 20, 10 };

        /// <summary>A detailed merchant group goes bankrupt after this many weeks in a row with cash below zero.</summary>
        public int MerchantBankruptWeeks = 5;

        /// <summary>
        /// Aggregate trade: share of the origin's stock moved per week along a route, per unit of margin over
        /// the destination price (DEC-003).
        /// </summary>
        public Fx MerchantFlowShare = Fx.FromPermille(500);

        /// <summary>[level, industry tier] producer groups spawned per node.</summary>
        public readonly int[,] ProducersPerNode = new int[WorldLevels.Count, IndustryTiers.Count];

        /// <summary>Weekly output variance of detailed facilities, +- permille around the expected value.</summary>
        public int ProductionVariancePermille = 100;

        /// <summary>Starting cash of a facility (and of a new operator after bankruptcy), per industry tier.</summary>
        public readonly long[] FacilityStartCash = { 500, 2000, 1000 };

        /// <summary>Output multiplier for a facility making one of its node's specialties (DEC-016).</summary>
        public Fx SpecialtyOutputMultiplier = Fx.FromPermille(1250);

        /// <summary>Share of a specialty's output that comes out one grade higher (chance per week when detailed).</summary>
        public Fx SpecialtyGradeUpChance = Fx.FromPermille(200);

        /// <summary>Anchor price multiplier for a node's specialties in its own market.</summary>
        public Fx SpecialtyPriceMultiplier = Fx.FromPermille(800);

        /// <summary>Per route terrain: extra travel turns, multiplier on the route's fees, chance of an encounter per trip.</summary>
        public readonly int[] TerrainExtraTurns = { 0, 1, 0, 0 };
        public readonly Fx[] TerrainCostMultiplier = { Fx.One, Fx.FromPermille(1500), Fx.FromPermille(1200), Fx.FromPermille(1500) };
        public readonly Fx[] TerrainDanger = { Fx.FromPermille(50), Fx.FromPermille(100), Fx.FromPermille(60), Fx.FromPermille(80) };

        /// <summary>A region's danger factor unless the map sets it (1.0 = normal).</summary>
        public Fx RegionDanger = Fx.One;
        /// <summary>Cargo value that doubles the encounter chance; the chance grows linearly with value.</summary>
        public long DangerValueScale = 2000;
        /// <summary>Share of the coins or of every carried good an encounter takes.</summary>
        public Fx EncounterCashLoss = Fx.FromPermille(300);
        public Fx EncounterCargoLoss = Fx.FromPermille(300);
        /// <summary>Guards cost a base plus a share of the cargo's value, and cut the encounter chance.</summary>
        public long GuardBaseCost = 10;
        public Fx GuardCostShare = Fx.FromPermille(20);
        public Fx GuardReduction = Fx.FromPermille(700);
        /// <summary>Guild insurance costs a share of the cargo's value and refunds a share of any loss.</summary>
        public Fx InsurancePremium = Fx.FromPermille(30);
        public Fx InsuranceRefund = Fx.FromPermille(800);
        /// <summary>Passage on a water route without an own ship: a base fare plus a fare per cargo unit (DEC-034).</summary>
        public long PassageBaseFee = 20;
        public long PassagePerUnit = 2;
        /// <summary>An own ship: its price (also its collateral value) and cargo limits on water routes.</summary>
        public long ShipPrice = 5000;
        public long ShipMaxWeight = 3000;
        public long ShipMaxSize = 2400;
        /// <summary>Share of the ship's price paid back when it is sold at a port.</summary>
        public Fx ShipResaleShare = Fx.FromPermille(700);

        /// <summary>A fully paved road's cut of the per-unit charge and of the encounter chance (DEC-054).</summary>
        public Fx RoadCostReduction = Fx.FromPermille(400);
        public Fx RoadDangerReduction = Fx.FromPermille(500);
        /// <summary>Weekly upkeep of one paved route (each direction counts), in its maintainer's coin.</summary>
        public long RoadUpkeep = 5;
        /// <summary>Condition lost per week while the upkeep is unpaid, in permille.</summary>
        public int RoadDecayPermille = 5;
        /// <summary>Building a road costs this per travel turn of the route and takes this many weeks.</summary>
        public long RoadBuildCostPerTurn = 300;
        public int RoadBuildWeeks = 8;

        /// <summary>Starting cash per interest group kind: company, guild, state enterprise, private enterprise.</summary>
        public readonly long[] GroupStartCash = { 20000, 2000, 50000, 1000 };
        /// <summary>NPC companies founded per nation.</summary>
        public int CompaniesPerNation = 2;
        /// <summary>Yearly guild fee; members pay less for insurance and for licenses in the guild's trade (DEC-057).</summary>
        public long GuildFee = 200;
        public Fx GuildInsuranceDiscount = Fx.FromPermille(300);
        public Fx GuildLicenseDiscount = Fx.FromPermille(300);
        /// <summary>Limits of a guild hall warehouse used by a member.</summary>
        public long GuildHallMaxWeight = 1000;
        public long GuildHallMaxSize = 800;

        /// <summary>Share of each continent's starting coin supply its national banks hold as reserves, split evenly.</summary>
        public Fx BankInitialReserveShare = Fx.FromPermille(100);
        /// <summary>Yearly interest on deposits, credited weekly (DEC-036).</summary>
        public Fx DepositInterestPerYear = Fx.FromPermille(20);
        /// <summary>Share of the exchanged amount the international bank keeps as its fee (DEC-036).</summary>
        public Fx ExchangeFee = Fx.FromPermille(30);
        /// <summary>Yearly interest on loans, added to the debt when the loan is taken (DEC-037).</summary>
        public Fx LoanInterestPerYear = Fx.FromPermille(100);
        public int LoanTermWeeks = 52;
        /// <summary>Credit limit as a share of the collateral's value.</summary>
        public Fx LoanToValue = Fx.FromPermille(500);
        /// <summary>Weeks a payment may stay overdue before the collateral is seized (DEC-037).</summary>
        public int LoanGraceWeeks = 4;
        /// <summary>Weeks a seized facility stays up for auction (DEC-039).</summary>
        public int AuctionWeeks = 2;
        /// <summary>An auction's reserve price, as a share of the facility's value; an NPC buys at it without a winning bid.</summary>
        public Fx AuctionReserveShare = Fx.FromPermille(800);
        /// <summary>Value of a level-1 facility as collateral, per industry tier; scales with level.</summary>
        public readonly long[] FacilityValue = { 3000, 8000, 5000 };

        /// <summary>A license costs this times the good's Mid base price (DEC-024).</summary>
        public Fx LicensePriceMultiplier = Fx.FromInt(20);

        /// <summary>Share of merchant groups that hold licenses, in permille (DEC-024).</summary>
        public int LicensedMerchantPermille = 200;

        /// <summary>Toll on entering a node, as a share of the cargo's value, unless the map sets the node's own.</summary>
        public Fx TollRate = Fx.FromPermille(30);

        /// <summary>Extra toll on goods in the entered node's specialty categories, unless the map sets it.</summary>
        public Fx TollSpecialtySurcharge = Fx.FromPermille(70);

        /// <summary>Weekly running cost (wages) of a level-1 facility, per industry tier; scales with level.</summary>
        public readonly long[] FacilityUpkeep = { 50, 100, 50 };

        /// <summary>A facility goes bankrupt after this many consecutive weeks with cash below zero (DEC-051).</summary>
        public int FacilityBankruptWeeks = 5;

        /// <summary>Weeks the player can still empty the warehouse of a facility lost to bankruptcy (DEC-052).</summary>
        public int LostWarehouseWeeks = 8;

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
        public Fx GuidanceWeightBuyLicense = Fx.FromInt(2);

        /// <summary>The default LOD policy simulates the player's ancestor at this level (and its subtree) in detail.</summary>
        public WorldLevel LodFocusLevel = WorldLevel.Region;

        /// <summary>Test/debug override; empty (the default) starts the player in a random village.</summary>
        public string PlayerStartNode = string.Empty;
        /// <summary>Starting coins, in the start continent's currency.</summary>
        public long PlayerStartCash = 500;

        /// <summary>The player's cart: cargo must fit both the total weight and the total size.</summary>
        public long CartMaxWeight = 400;
        public long CartMaxSize = 300;

        /// <summary>The warehouse at each player-owned facility.</summary>
        public long WarehouseMaxWeight = 2000;
        public long WarehouseMaxSize = 1500;

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
                MerchantMaxWeight[l] = Long(v, $"merchants.{level}.max_weight", MerchantMaxWeight[l]);
                MerchantMaxSize[l] = Long(v, $"merchants.{level}.max_size", MerchantMaxSize[l]);
                MerchantUpkeep[l] = Long(v, $"merchants.{level}.upkeep", MerchantUpkeep[l]);
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
            MerchantBankruptWeeks = Int(v, "merchants.bankrupt_weeks", MerchantBankruptWeeks);
            if (MerchantBankruptWeeks < 1) throw new FormatException("config.csv: merchants.bankrupt_weeks must be >= 1.");
            MerchantFlowShare = Permille(v, "merchants.flow_permille", MerchantFlowShare);
            for (int t = 0; t < IndustryTiers.Count; t++)
            {
                string tier = ((IndustryTier)t).ToString().ToLowerInvariant();
                FacilityStartCash[t] = Long(v, $"facility.{tier}.start_cash", FacilityStartCash[t]);
                FacilityUpkeep[t] = Long(v, $"facility.{tier}.upkeep", FacilityUpkeep[t]);
                FacilityValue[t] = Long(v, $"facility.{tier}.value", FacilityValue[t]);
            }
            for (int t = 0; t < RouteTerrains.Count; t++)
            {
                string terrain = ((RouteTerrain)t).ToString().ToLowerInvariant();
                TerrainExtraTurns[t] = Int(v, $"terrain.{terrain}.extra_turns", TerrainExtraTurns[t]);
                TerrainCostMultiplier[t] = Permille(v, $"terrain.{terrain}.cost_permille", TerrainCostMultiplier[t]);
                TerrainDanger[t] = Permille(v, $"terrain.{terrain}.danger_permille", TerrainDanger[t]);
                if (TerrainExtraTurns[t] < 0) throw new FormatException($"config.csv: terrain.{terrain}.extra_turns must not be negative.");
            }
            RegionDanger = Permille(v, "danger.region_permille", RegionDanger);
            DangerValueScale = Long(v, "danger.value_scale", DangerValueScale);
            if (DangerValueScale < 1) throw new FormatException("config.csv: danger.value_scale must be >= 1.");
            EncounterCashLoss = Permille(v, "encounter.cash_loss_permille", EncounterCashLoss);
            EncounterCargoLoss = Permille(v, "encounter.cargo_loss_permille", EncounterCargoLoss);
            GuardBaseCost = Long(v, "guard.base_cost", GuardBaseCost);
            GuardCostShare = Permille(v, "guard.cost_permille", GuardCostShare);
            GuardReduction = Permille(v, "guard.reduction_permille", GuardReduction);
            InsurancePremium = Permille(v, "insurance.premium_permille", InsurancePremium);
            InsuranceRefund = Permille(v, "insurance.refund_permille", InsuranceRefund);
            PassageBaseFee = Long(v, "passage.base_fee", PassageBaseFee);
            PassagePerUnit = Long(v, "passage.per_unit", PassagePerUnit);
            ShipPrice = Long(v, "ship.price", ShipPrice);
            ShipMaxWeight = Long(v, "ship.max_weight", ShipMaxWeight);
            ShipMaxSize = Long(v, "ship.max_size", ShipMaxSize);
            ShipResaleShare = Permille(v, "ship.resale_permille", ShipResaleShare);
            RoadCostReduction = Permille(v, "road.cost_reduction_permille", RoadCostReduction);
            RoadDangerReduction = Permille(v, "road.danger_reduction_permille", RoadDangerReduction);
            RoadUpkeep = Long(v, "road.upkeep", RoadUpkeep);
            RoadDecayPermille = Int(v, "road.decay_permille", RoadDecayPermille);
            RoadBuildCostPerTurn = Long(v, "road.build_cost_per_turn", RoadBuildCostPerTurn);
            RoadBuildWeeks = Int(v, "road.build_weeks", RoadBuildWeeks);
            if (RoadBuildWeeks < 1) throw new FormatException("config.csv: road.build_weeks must be >= 1.");
            string[] groupKeys = { "company", "guild", "state", "private" };
            for (int k = 0; k < GroupStartCash.Length; k++)
                GroupStartCash[k] = Long(v, $"groups.{groupKeys[k]}.start_cash", GroupStartCash[k]);
            CompaniesPerNation = Int(v, "groups.companies_per_nation", CompaniesPerNation);
            GuildFee = Long(v, "guild.fee", GuildFee);
            GuildInsuranceDiscount = Permille(v, "guild.insurance_discount_permille", GuildInsuranceDiscount);
            GuildLicenseDiscount = Permille(v, "guild.license_discount_permille", GuildLicenseDiscount);
            GuildHallMaxWeight = Long(v, "guild.hall_max_weight", GuildHallMaxWeight);
            GuildHallMaxSize = Long(v, "guild.hall_max_size", GuildHallMaxSize);
            BankInitialReserveShare = Permille(v, "bank.initial_reserve_permille", BankInitialReserveShare);
            DepositInterestPerYear = Permille(v, "bank.deposit_interest_permille", DepositInterestPerYear);
            ExchangeFee = Permille(v, "bank.exchange_fee_permille", ExchangeFee);
            LoanInterestPerYear = Permille(v, "bank.loan_interest_permille", LoanInterestPerYear);
            LoanTermWeeks = Int(v, "bank.loan_term_weeks", LoanTermWeeks);
            if (LoanTermWeeks < 1) throw new FormatException("config.csv: bank.loan_term_weeks must be >= 1.");
            LoanToValue = Permille(v, "bank.loan_to_value_permille", LoanToValue);
            LoanGraceWeeks = Int(v, "bank.grace_weeks", LoanGraceWeeks);
            AuctionWeeks = Int(v, "bank.auction_weeks", AuctionWeeks);
            if (AuctionWeeks < 1) throw new FormatException("config.csv: bank.auction_weeks must be >= 1.");
            AuctionReserveShare = Permille(v, "bank.auction_reserve_permille", AuctionReserveShare);
            LicensePriceMultiplier = Permille(v, "license.price_permille", LicensePriceMultiplier);
            LicensedMerchantPermille = Int(v, "license.merchant_share_permille", LicensedMerchantPermille);
            TollRate = Permille(v, "toll.tariff_permille", TollRate);
            TollSpecialtySurcharge = Permille(v, "toll.specialty_surcharge_permille", TollSpecialtySurcharge);
            SpecialtyOutputMultiplier = Permille(v, "specialty.output_permille", SpecialtyOutputMultiplier);
            SpecialtyGradeUpChance = Permille(v, "specialty.grade_up_permille", SpecialtyGradeUpChance);
            SpecialtyPriceMultiplier = Permille(v, "specialty.price_permille", SpecialtyPriceMultiplier);
            if (SpecialtyGradeUpChance.Raw < 0 || SpecialtyGradeUpChance.Raw > Fx.One.Raw)
                throw new FormatException("config.csv: specialty.grade_up_permille must be within 0..1000.");
            FacilityBankruptWeeks = Int(v, "facility.bankrupt_weeks", FacilityBankruptWeeks);
            if (FacilityBankruptWeeks < 1) throw new FormatException("config.csv: facility.bankrupt_weeks must be >= 1.");
            LostWarehouseWeeks = Int(v, "cargo.lost_warehouse_weeks", LostWarehouseWeeks);
            if (LostWarehouseWeeks < 0) throw new FormatException("config.csv: cargo.lost_warehouse_weeks must be >= 0.");
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
            GuidanceWeightBuyLicense = Permille(v, "guidance.weight.buy_license_permille", GuidanceWeightBuyLicense);
            if (v.TryGetValue("lod.focus_level", out var focus))
                LodFocusLevel = CsvTable.TryParseEnumName(focus, out WorldLevel level)
                    ? level
                    : throw new FormatException($"config.csv: lod.focus_level '{focus}' is not a WorldLevel.");
            Record("lod.focus_level", LodFocusLevel.ToString());
            PlayerStartNode = v.TryGetValue("player.start_node", out var start) ? start : PlayerStartNode;
            Record("player.start_node", PlayerStartNode);
            PlayerStartCash = Long(v, "player.start_cash", PlayerStartCash);
            CartMaxWeight = Long(v, "cargo.cart.max_weight", CartMaxWeight);
            CartMaxSize = Long(v, "cargo.cart.max_size", CartMaxSize);
            WarehouseMaxWeight = Long(v, "cargo.warehouse.max_weight", WarehouseMaxWeight);
            WarehouseMaxSize = Long(v, "cargo.warehouse.max_size", WarehouseMaxSize);
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
