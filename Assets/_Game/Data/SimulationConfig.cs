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

        /// <summary>Price elasticity of necessities and of luxuries: the share of demand lost per 100% above the anchor (DEC-048).</summary>
        public Fx NecessityElasticity = Fx.FromPermille(500);
        public Fx LuxuryElasticity = Fx.FromPermille(1500);
        /// <summary>Per grade: how far a necessity is bought like a luxury (higher grades lean toward luxury, DEC-048).</summary>
        public readonly Fx[] GradeLuxuryShare = { Fx.Zero, Fx.Zero, Fx.FromPermille(500), Fx.One };
        /// <summary>Demand below the anchor price grows with elasticity up to this multiple.</summary>
        public Fx MaxCheapDemand = Fx.FromInt(2);
        /// <summary>Share of household income per quintile, poorest first (Pareto, DEC-049); sums to 1.</summary>
        public readonly Fx[] QuintileIncomeShare =
        {
            Fx.FromPermille(20), Fx.FromPermille(30), Fx.FromPermille(50), Fx.FromPermille(100), Fx.FromPermille(800),
        };
        /// <summary>Starting population per node level, unless the map sets it (DEC-050).</summary>
        public readonly long[] DefaultPopulation = { 20000, 10000, 4000, 2000, 500 };
        /// <summary>
        /// Starting weekly income per head, in coins, per node level ([Draft] P13d: near what households measurably
        /// earn in the sample world; was 4 / 4 / 3 / 3 / 2).
        /// </summary>
        public readonly Fx[] DefaultIncomePerHead =
            { Fx.FromPermille(300), Fx.FromPermille(300), Fx.FromPermille(300), Fx.FromPermille(500), Fx.FromPermille(2000) };
        /// <summary>Yearly population change: the base rate, plus this share of how far necessities were afforded above or below need.</summary>
        /// <summary>
        /// Yearly population change with necessities fully supplied: Victorian Europe's rate. The best year reaches
        /// England and Wales 1851-1901 (about 1.2-1.3%: births 35, deaths 22 per thousand); the worst, famine.
        /// </summary>
        public Fx PopulationBaseGrowth = Fx.FromPermille(8);
        public Fx PopulationGrowthResponse = Fx.FromPermille(40);
        public Fx PopulationMaxGrowth = Fx.FromPermille(13);
        public Fx PopulationMaxDecline = Fx.FromPermille(40);
        /// <summary>
        /// Yearly income change (DEC-050, P13c): income per head moves this share of the way to the income
        /// households measurably received over the year, by at most the cap either way.
        /// </summary>
        public Fx IncomeResponse = Fx.FromPermille(500);
        public Fx IncomeMaxChange = Fx.FromPermille(500);
        /// <summary>A necessity counts toward population change if its market offered it within this many weeks: a good that stops arriving is a famine, one never sold is not (P13d).</summary>
        public int NeedMemoryWeeks = 52;
        /// <summary>Households spend at most their cash over this many weeks a week; demand shrinks to fit (DEC-061, P13b).</summary>
        public int HouseholdBudgetWeeks = 26;
        /// <summary>Share of the money nobody holds at the start that goes to the traders' floats; households get the rest (P13b).</summary>
        public Fx TraderFloatShare = Fx.FromPermille(100);
        /// <summary>Share of its float, as the week starts, that producers may be paid from; merchants sell into the rest later in the week.</summary>
        public Fx ProducerFloatShare = Fx.FromPermille(500);
        /// <summary>Employment rate where activity matches its long-run level, and its limits.</summary>
        public Fx LaborBaseEmployment = Fx.FromPermille(900);
        public Fx LaborMinEmployment = Fx.FromPermille(500);
        public Fx LaborMaxEmployment = Fx.FromPermille(1000);
        /// <summary>Share of the way the rate moves toward its target per quarter, and the long-run level toward activity.</summary>
        public Fx LaborResponse = Fx.FromPermille(500);
        public Fx LaborLongRunResponse = Fx.FromPermille(125);
        /// <summary>Weekly share of a merchant group's cash above its buffer that it pays out in wages at full employment.</summary>
        public Fx LaborMerchantWage = Fx.FromPermille(100);
        /// <summary>A merchant group keeps this share of its starting cash as working capital before paying wages.</summary>
        public Fx LaborMerchantBuffer = Fx.FromPermille(1000);
        /// <summary>Income tax, paid yearly, on households' measured income and on companies' profit.</summary>
        public Fx TaxHousehold = Fx.FromPermille(100);
        public Fx TaxCompany = Fx.FromPermille(100);
        /// <summary>Defense as a share of a nation's yearly budget, and its maintenance on top, as a share of that.</summary>
        public Fx DefenseShare = Fx.FromPermille(50);
        public Fx DefenseMaintenance = Fx.FromPermille(200);
        /// <summary>Weeks between defense purchases; scanning every market is the cost.</summary>
        public int DefenseIntervalWeeks = 4;
        /// <summary>Yearly defense per route of the nation for one full danger cut, and the most it can cut.</summary>
        public int DefenseSafetyCostPerRoute = 15_000;
        public Fx DefenseMaxSafety = Fx.FromPermille(600);
        /// <summary>Share of the reserve above target subsidies may pay in a year, and the rate on the value produced.</summary>
        public Fx SubsidyShare = Fx.FromPermille(300);
        public Fx SubsidyRate = Fx.FromPermille(200);
        public int SubsidyYears = 3;
        public int SubsidyMaxPerNation = 2;
        /// <summary>Share of the reserve above target a nation may put into projects each year, and how many.</summary>
        public Fx StateProjectShare = Fx.FromPermille(500);
        public int StateProjectsPerYear = 3;
        public int StateProjectWeeks = 16;
        /// <summary>What harbors and highways score beyond the import bill.</summary>
        public int StateTradeScore = 100;
        /// <summary>What paving scores when a nation chooses, in the permille its other needs score in.</summary>
        public int StateRoadScore = 120;
        public long StateFarmlandCost = 5_000;
        public long StateMineCost = 8_000;
        public long StateHarborCost = 10_000;
        public long StateHighwayCostPerTurn = 1_000;
        /// <summary>Primary output per level of land development, and the most levels.</summary>
        public Fx StateLandBonus = Fx.FromPermille(100);
        public int StateLandMax = 5;
        /// <summary>Per-unit charge cut on water routes per harbor level, and the most levels; and on highways.</summary>
        public Fx StateHarborBonus = Fx.FromPermille(100);
        public int StateHarborMax = 3;
        public Fx StateHighwayBonus = Fx.FromPermille(200);
        /// <summary>Share of a state enterprise's dividends the state collects; it keeps the rest for itself.</summary>
        public Fx StateFacilityLevy = Fx.FromPermille(500);

        /// <summary>One policy step changes tolls, license prices or minting by this share; at most this many steps either way (DEC-058).</summary>
        public Fx PolicyStep = Fx.FromPermille(100);
        public int PolicyMaxSteps = 5;
        /// <summary>A policy moves when the influence-weighted balance of stances reaches this share of the nation's influence.</summary>
        public Fx PolicyMoveThreshold = Fx.FromPermille(100);
        /// <summary>Status-quo bias: each step a policy already stands from its start raises the threshold by this share of itself.</summary>
        public Fx PolicyResistance = Fx.One;
        /// <summary>Influence per coin of assets, per coin lobbied, and per job (DEC-058).</summary>
        public Fx InfluenceAssetWeight = Fx.One;
        public Fx InfluenceLobbyWeight = Fx.FromInt(3);
        public long InfluencePerJob = 50;
        public int JobsPerFacilityLevel = 20;
        public int JobsPerMerchantGroup = 10;
        /// <summary>Share of its cash an NPC group spends lobbying each year.</summary>
        public Fx NpcLobbyShare = Fx.FromPermille(50);
        /// <summary>A group that lost this share of its worth over the year leans fully toward protection (as a multiplier on the share lost).</summary>
        public Fx PolicyLossTilt = Fx.FromInt(2);
        /// <summary>Published yearly inflation at which groups other than state enterprises fully oppose minting (DEC-060).</summary>
        public Fx InflationOpposition = Fx.FromPermille(100);

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
        /// <summary>Share of each continent's starting coin supply its international bank holds, to change coins with (P13a).</summary>
        public Fx InternationalBankReserveShare = Fx.FromPermille(20);
        /// <summary>Reserve ratio (reserve over reserve plus loans owed) below which loan rates rise (DEC-042).</summary>
        public Fx TargetReserveRatio = Fx.FromPermille(500);
        /// <summary>Cap on the loan rate's rise from low reserves, as a multiple of the base rate.</summary>
        public Fx MaxLoanRateMultiplier = Fx.FromInt(3);
        /// <summary>
        /// A nation's reserve target: this multiple of its share of the initial reserve share of the current
        /// coin supply. The state spends reserves above it, beyond the player's deposits, as public wages (DEC-061).
        /// </summary>
        public Fx StateReserveTarget = Fx.FromInt(2);
        /// <summary>Weekly share of the reserve above the target the state spends (DEC-061).</summary>
        public Fx StateSpendingShare = Fx.FromPermille(20);
        /// <summary>Term of NPC loans (DEC-037).</summary>
        public int NpcLoanTermWeeks = 26;
        /// <summary>An NPC borrowing to cover a shortfall also borrows this many weeks of its upkeep.</summary>
        public int NpcLoanBufferWeeks = 8;
        /// <summary>One NPC loan takes at most this share of the lending bank's reserve; 0 turns NPC lending off.</summary>
        public Fx NpcLoanMaxReserveShare = Fx.FromPermille(100);
        /// <summary>Quarterly records kept (DEC-043).</summary>
        public int FinanceHistoryQuarters = 20;
        /// <summary>Chance per nation and quarter that a financial event starts (DEC-044).</summary>
        public int FinancialEventChancePermille = 20;
        /// <summary>An event ends once the reserve ratio is back above this, after at least a quarter (DEC-047).</summary>
        public Fx FinancialEventEndReserveRatio = Fx.FromPermille(500);
        /// <summary>Share of the reserve the public withdraws when a bank run starts.</summary>
        public Fx BankRunDrain = Fx.FromPermille(200);
        /// <summary>During a bank run the player may withdraw at most this share of the deposit, once a week.</summary>
        public Fx BankRunWithdrawShare = Fx.FromPermille(100);
        /// <summary>Loan rates are multiplied by this during a rate spike.</summary>
        public Fx RateSpikeMultiplier = Fx.FromInt(2);

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

        /// <summary>Merchant groups have a personality and occupation (reference specification, [Draft]).</summary>
        public bool MerchantTraitsEnabled = true;
        /// <summary>
        /// Groups judge prices only at nodes they have seen or heard of (reference specification, [Draft]). Off by
        /// default: in the sample world it raised merchant bankruptcies from 2 to 233 in ten years.
        /// </summary>
        public bool MerchantRumorsEnabled = false;
        /// <summary>Yearly share of groups that hand over to a successor of a newly drawn trait.</summary>
        public Fx MerchantGenerationShare = Fx.FromPermille(50);
        /// <summary>Chance a new group's trait is drawn at random rather than by fitness.</summary>
        public Fx MerchantMutationShare = Fx.FromPermille(150);
        /// <summary>Weight of relative capital in a trait's fitness; survival has the rest.</summary>
        public Fx MerchantFitnessCapital = Fx.FromPermille(700);
        /// <summary>Per personality (cautious, normal, aggressive): share of cash risked on a load, and the least margin over the buying price.</summary>
        public readonly Fx[] PersonalitySpend = { Fx.FromPermille(500), Fx.FromPermille(750), Fx.One };
        public readonly Fx[] PersonalityMinMargin = { Fx.FromPermille(200), Fx.FromPermille(100), Fx.Zero };
        /// <summary>Per personality: the group trades within its home's ancestor at this level.</summary>
        public readonly WorldLevel[] PersonalityRange = { WorldLevel.Region, WorldLevel.Nation, WorldLevel.Continent };
        /// <summary>Per occupation (local, expedition, peddler): capacity and upkeep scale, and how strongly distance counts when choosing a load.</summary>
        public readonly Fx[] OccupationCapacity = { Fx.One, Fx.One, Fx.FromPermille(500) };
        public readonly Fx[] OccupationUpkeep = { Fx.One, Fx.One, Fx.FromPermille(500) };
        public readonly int[] OccupationDistancePower = { 2, 0, 1 };

        /// <summary>An NPC facility keeps this many weeks of costs (upkeep and inputs at current prices); cash above it is profit (DEC-061).</summary>
        public int DividendBufferWeeks = 26;
        /// <summary>Weekly share of that profit paid to the owning group as dividends (DEC-061).</summary>
        public Fx DividendShare = Fx.FromPermille(100);
        /// <summary>Share of the dividends a group receives that it passes on to the households of its home node (DEC-061).</summary>
        public Fx GroupPayoutShare = Fx.FromPermille(800);

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
                DefaultPopulation[l] = Long(v, $"households.{level}.population", DefaultPopulation[l]);
                DefaultIncomePerHead[l] = Permille(v, $"households.{level}.income_permille", DefaultIncomePerHead[l]);
                if (DefaultPopulation[l] <= 0 || DefaultIncomePerHead[l].Raw <= 0)
                    throw new FormatException($"config.csv: households.{level}.population and income must be positive.");
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
            PolicyStep = Permille(v, "policy.step_permille", PolicyStep);
            PolicyMaxSteps = Int(v, "policy.max_steps", PolicyMaxSteps);
            if (PolicyMaxSteps < 0 || PolicyStep.MulInt(PolicyMaxSteps * 1000L) >= 1000)
                throw new FormatException("config.csv: policy.step_permille x policy.max_steps must stay below 1000.");
            PolicyMoveThreshold = Permille(v, "policy.move_threshold_permille", PolicyMoveThreshold);
            PolicyResistance = Permille(v, "policy.resistance_permille", PolicyResistance);
            InfluenceAssetWeight = Permille(v, "policy.asset_weight_permille", InfluenceAssetWeight);
            InfluenceLobbyWeight = Permille(v, "policy.lobby_weight_permille", InfluenceLobbyWeight);
            InfluencePerJob = Long(v, "policy.influence_per_job", InfluencePerJob);
            JobsPerFacilityLevel = Int(v, "policy.jobs_per_facility_level", JobsPerFacilityLevel);
            JobsPerMerchantGroup = Int(v, "policy.jobs_per_merchant_group", JobsPerMerchantGroup);
            NpcLobbyShare = Permille(v, "policy.npc_lobby_share_permille", NpcLobbyShare);
            PolicyLossTilt = Permille(v, "policy.loss_tilt_permille", PolicyLossTilt);
            InflationOpposition = Permille(v, "policy.inflation_opposition_permille", InflationOpposition);
            NecessityElasticity = Permille(v, "demand.necessity_elasticity_permille", NecessityElasticity);
            LuxuryElasticity = Permille(v, "demand.luxury_elasticity_permille", LuxuryElasticity);
            MaxCheapDemand = Permille(v, "demand.max_cheap_permille", MaxCheapDemand);
            if (MaxCheapDemand.Raw < Fx.One.Raw) throw new FormatException("config.csv: demand.max_cheap_permille must be >= 1000.");
            for (int g = 0; g < QualityGrades.Count; g++)
                GradeLuxuryShare[g] = Permille(v, $"demand.{((QualityGrade)g).ToString().ToLowerInvariant()}.luxury_share_permille", GradeLuxuryShare[g]);
            long shares = 0;
            for (int q = 0; q < QuintileIncomeShare.Length; q++)
            {
                QuintileIncomeShare[q] = Permille(v, $"households.q{q + 1}.income_share_permille", QuintileIncomeShare[q]);
                if (QuintileIncomeShare[q].Raw <= 0) throw new FormatException("config.csv: household income shares must be positive.");
                shares += QuintileIncomeShare[q].Raw;
            }
            if (Math.Abs(shares - Fx.One.Raw) > QuintileIncomeShare.Length)
                throw new FormatException("config.csv: households.q1..q5.income_share_permille must sum to 1000.");
            PopulationBaseGrowth = Permille(v, "households.base_growth_permille", PopulationBaseGrowth);
            PopulationGrowthResponse = Permille(v, "households.growth_response_permille", PopulationGrowthResponse);
            PopulationMaxGrowth = Permille(v, "households.max_growth_permille", PopulationMaxGrowth);
            PopulationMaxDecline = Permille(v, "households.max_decline_permille", PopulationMaxDecline);
            IncomeResponse = Permille(v, "households.income_response_permille", IncomeResponse);
            IncomeMaxChange = Permille(v, "households.income_max_change_permille", IncomeMaxChange);
            NeedMemoryWeeks = Int(v, "households.need_memory_weeks", NeedMemoryWeeks);
            if (NeedMemoryWeeks < 0) throw new FormatException("config.csv: households.need_memory_weeks must be >= 0.");
            HouseholdBudgetWeeks = Int(v, "households.budget_weeks", HouseholdBudgetWeeks);
            if (HouseholdBudgetWeeks < 1) throw new FormatException("config.csv: households.budget_weeks must be >= 1.");
            TraderFloatShare = Permille(v, "markets.trader_float_permille", TraderFloatShare);
            ProducerFloatShare = Permille(v, "markets.producer_float_permille", ProducerFloatShare);
            LaborBaseEmployment = Permille(v, "labor.base_employment_permille", LaborBaseEmployment);
            LaborMinEmployment = Permille(v, "labor.min_employment_permille", LaborMinEmployment);
            LaborMaxEmployment = Permille(v, "labor.max_employment_permille", LaborMaxEmployment);
            LaborResponse = Permille(v, "labor.response_permille", LaborResponse);
            LaborLongRunResponse = Permille(v, "labor.long_run_response_permille", LaborLongRunResponse);
            LaborMerchantWage = Permille(v, "labor.merchant_wage_permille", LaborMerchantWage);
            LaborMerchantBuffer = Permille(v, "labor.merchant_buffer_permille", LaborMerchantBuffer);
            TaxHousehold = Permille(v, "tax.household_permille", TaxHousehold);
            TaxCompany = Permille(v, "tax.company_permille", TaxCompany);
            DefenseShare = Permille(v, "state.defense_permille", DefenseShare);
            DefenseMaintenance = Permille(v, "state.defense_maintenance_permille", DefenseMaintenance);
            DefenseIntervalWeeks = Int(v, "state.defense_interval_weeks", DefenseIntervalWeeks);
            DefenseSafetyCostPerRoute = Int(v, "state.defense_safety_cost_per_route", DefenseSafetyCostPerRoute);
            DefenseMaxSafety = Permille(v, "state.defense_max_safety_permille", DefenseMaxSafety);
            SubsidyShare = Permille(v, "state.subsidy_share_permille", SubsidyShare);
            SubsidyRate = Permille(v, "state.subsidy_rate_permille", SubsidyRate);
            SubsidyYears = Int(v, "state.subsidy_years", SubsidyYears);
            SubsidyMaxPerNation = Int(v, "state.subsidy_max_per_nation", SubsidyMaxPerNation);
            StateTradeScore = Int(v, "state.trade_score_permille", StateTradeScore);
            if (DefenseIntervalWeeks < 1) throw new FormatException("config.csv: state.defense_interval_weeks must be >= 1.");
            StateProjectShare = Permille(v, "state.project_share_permille", StateProjectShare);
            StateProjectsPerYear = Int(v, "state.projects_per_year", StateProjectsPerYear);
            StateProjectWeeks = Int(v, "state.project_weeks", StateProjectWeeks);
            StateRoadScore = Int(v, "state.road_score_permille", StateRoadScore);
            StateFarmlandCost = Long(v, "state.farmland_cost", StateFarmlandCost);
            StateMineCost = Long(v, "state.mine_cost", StateMineCost);
            StateHarborCost = Long(v, "state.harbor_cost", StateHarborCost);
            StateHighwayCostPerTurn = Long(v, "state.highway_cost_per_turn", StateHighwayCostPerTurn);
            StateLandBonus = Permille(v, "state.land_bonus_permille", StateLandBonus);
            StateLandMax = Int(v, "state.land_max", StateLandMax);
            StateHarborBonus = Permille(v, "state.harbor_bonus_permille", StateHarborBonus);
            StateHarborMax = Int(v, "state.harbor_max", StateHarborMax);
            StateHighwayBonus = Permille(v, "state.highway_bonus_permille", StateHighwayBonus);
            StateFacilityLevy = Permille(v, "state.facility_levy_permille", StateFacilityLevy);
            InternationalBankReserveShare = Permille(v, "bank.international_reserve_permille", InternationalBankReserveShare);
            TargetReserveRatio = Permille(v, "bank.target_reserve_permille", TargetReserveRatio);
            MaxLoanRateMultiplier = Permille(v, "bank.max_rate_multiplier_permille", MaxLoanRateMultiplier);
            if (MaxLoanRateMultiplier.Raw < Fx.One.Raw) throw new FormatException("config.csv: bank.max_rate_multiplier_permille must be >= 1000.");
            NpcLoanTermWeeks = Int(v, "bank.npc_loan_term_weeks", NpcLoanTermWeeks);
            if (NpcLoanTermWeeks < 1) throw new FormatException("config.csv: bank.npc_loan_term_weeks must be >= 1.");
            NpcLoanBufferWeeks = Int(v, "bank.npc_buffer_weeks", NpcLoanBufferWeeks);
            NpcLoanMaxReserveShare = Permille(v, "bank.npc_max_reserve_share_permille", NpcLoanMaxReserveShare);
            FinanceHistoryQuarters = Int(v, "finance.history_quarters", FinanceHistoryQuarters);
            if (FinanceHistoryQuarters < 4) throw new FormatException("config.csv: finance.history_quarters must be >= 4.");
            FinancialEventChancePermille = Int(v, "finance.event_chance_permille", FinancialEventChancePermille);
            FinancialEventEndReserveRatio = Permille(v, "finance.event_end_reserve_permille", FinancialEventEndReserveRatio);
            BankRunDrain = Permille(v, "finance.bank_run_drain_permille", BankRunDrain);
            BankRunWithdrawShare = Permille(v, "finance.bank_run_withdraw_permille", BankRunWithdrawShare);
            RateSpikeMultiplier = Permille(v, "finance.rate_spike_permille", RateSpikeMultiplier);
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
            MerchantTraitsEnabled = Int(v, "merchants.traits", MerchantTraitsEnabled ? 1 : 0) != 0;
            MerchantRumorsEnabled = Int(v, "merchants.rumors", MerchantRumorsEnabled ? 1 : 0) != 0;
            MerchantGenerationShare = Permille(v, "merchants.generation_permille", MerchantGenerationShare);
            MerchantMutationShare = Permille(v, "merchants.mutation_permille", MerchantMutationShare);
            MerchantFitnessCapital = Permille(v, "merchants.fitness_capital_permille", MerchantFitnessCapital);
            string[] personalities = { "cautious", "normal", "aggressive" };
            for (int i = 0; i < personalities.Length; i++)
            {
                PersonalitySpend[i] = Permille(v, $"merchants.{personalities[i]}.spend_permille", PersonalitySpend[i]);
                PersonalityMinMargin[i] = Permille(v, $"merchants.{personalities[i]}.min_margin_permille", PersonalityMinMargin[i]);
                string rangeKey = $"merchants.{personalities[i]}.range_level";
                if (v.TryGetValue(rangeKey, out var range))
                    PersonalityRange[i] = CsvTable.TryParseEnumName(range, out WorldLevel rangeLevel)
                        ? rangeLevel
                        : throw new FormatException($"config.csv: {rangeKey} '{range}' is not a WorldLevel.");
                Record(rangeKey, PersonalityRange[i].ToString());
            }
            string[] occupations = { "local", "expedition", "peddler" };
            for (int i = 0; i < occupations.Length; i++)
            {
                OccupationCapacity[i] = Permille(v, $"merchants.{occupations[i]}.capacity_permille", OccupationCapacity[i]);
                OccupationUpkeep[i] = Permille(v, $"merchants.{occupations[i]}.upkeep_permille", OccupationUpkeep[i]);
                OccupationDistancePower[i] = Int(v, $"merchants.{occupations[i]}.distance_power", OccupationDistancePower[i]);
                if (OccupationDistancePower[i] < 0 || OccupationDistancePower[i] > 2)
                    throw new FormatException($"config.csv: merchants.{occupations[i]}.distance_power must be 0, 1 or 2.");
            }
            DividendBufferWeeks = Int(v, "facility.dividend_buffer_weeks", DividendBufferWeeks);
            if (DividendBufferWeeks < 0) throw new FormatException("config.csv: facility.dividend_buffer_weeks must be >= 0.");
            DividendShare = Permille(v, "facility.dividend_permille", DividendShare);
            GroupPayoutShare = Permille(v, "groups.payout_permille", GroupPayoutShare);
            StateReserveTarget = Permille(v, "bank.state_reserve_target_permille", StateReserveTarget);
            StateSpendingShare = Permille(v, "bank.state_spending_permille", StateSpendingShare);
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
