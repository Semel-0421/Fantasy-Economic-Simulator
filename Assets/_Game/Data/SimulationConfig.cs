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
    public sealed partial class SimulationConfig
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
        /// <summary>DEC-126: necessities follow a constant-elasticity curve; from this price / anchor on, none are bought.</summary>
        public Fx NecessityChoke = Fx.FromInt(5);
        public Fx LuxuryElasticity = Fx.FromPermille(1500);
        /// <summary>Per grade: how far a necessity is bought like a luxury (higher grades lean toward luxury, DEC-048).</summary>
        public readonly Fx[] GradeLuxuryShare = { Fx.Zero, Fx.Zero, Fx.FromPermille(500), Fx.One };
        /// <summary>The same for a semi-necessity (DEC-091).</summary>
        public readonly Fx[] SemiGradeLuxuryShare = { Fx.FromPermille(250), Fx.FromPermille(650), Fx.FromPermille(900), Fx.One };
        /// <summary>How far a good is bought like a luxury.</summary>
        public Fx LuxuryShare(GoodDef good) =>
            good.IsLuxury ? Fx.One : (good.IsSemi ? SemiGradeLuxuryShare : GradeLuxuryShare)[(int)good.Grade];
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
        /// <summary>DEC-127: the share of its holdings the top quintile may give a week to pay for the lower quintiles' necessities.</summary>
        public Fx HouseholdAlmsShare = Fx.FromPermille(20);
        /// <summary>DEC-127: luxury demand grows by this for each year of the node's income the top quintile holds, up to the cap.</summary>
        public Fx LuxuryWealthPerYear = Fx.FromPermille(1000);
        public Fx LuxuryWealthCap = Fx.FromInt(4);
        /// <summary>DEC-127: the share of what households pay beyond their necessities that goes to the state.</summary>
        public Fx LuxuryTax = Fx.FromPermille(100);
        /// <summary>Share of the money nobody holds at the start that goes to the traders' floats; households get the rest (P13b).</summary>
        public Fx TraderFloatShare = Fx.FromPermille(100);
        /// <summary>Loans to the traders' float (DEC-092): the weekly share of what households can spare beyond these weeks of spending, and of the bank's excess; zero is off.</summary>
        public Fx TraderLoanShare = Fx.Zero;
        public int TraderLoanKeepWeeks = 26;
        public Fx TraderBankLoanShare = Fx.Zero;
        /// <summary>Yearly interest, the weeks a loan is paid back over, and the share of the float a week's payments may take.</summary>
        public Fx TraderLoanInterest = Fx.FromPermille(50);
        public int TraderLoanWeeks = 52;
        public Fx TraderLoanRepayShare = Fx.FromPermille(250);
        /// <summary>Share of its float, as the week starts, that producers may be paid from; merchants sell into the rest later in the week.</summary>
        public Fx ProducerFloatShare = Fx.FromPermille(500);
        /// <summary>Employment rate where activity matches its long-run level, and its limits.</summary>
        /// <summary>Per node level: how much a head earns of wages split across a nation (DEC-076): towns pay more than villages.</summary>
        public readonly Fx[] LevelWageWeight =
            { Fx.FromPermille(3000), Fx.FromPermille(3000), Fx.FromPermille(2500), Fx.FromPermille(2000), Fx.One };
        /// <summary>Shares of income tax taken before the state: the royal house's first, then the city's holder and the village's noble (DEC-076).</summary>
        public Fx NobleRoyalShare = Fx.FromPermille(100);
        public Fx NobleCityShare = Fx.FromPermille(100);
        public Fx NobleVillageShare = Fx.FromPermille(100);
        /// <summary>Yearly aid a vassal pays from its purse, the share of it the lord owes back as protection, and a house's weekly spending from its purse.</summary>
        public Fx NobleAidShare = Fx.FromPermille(200);
        public Fx NobleProtectionShare = Fx.FromPermille(500);
        public Fx NobleSpendShare = Fx.FromPermille(20);
        /// <summary>Share of a village facility's wages that leaves for the nation's towns, as rents to absentee landlords (DEC-077).</summary>
        public Fx VillageWageAwayShare = Fx.FromPermille(500);
        /// <summary>Migration (DEC-079), off at a zero limit: the most of a village's people moving a year on the widest gap; villages' free share at the start.</summary>
        public Fx MigrationMaxShare = Fx.Zero;
        /// <summary>Paid expansion (DEC-080): extra output per level above the first, on top of the level itself, and the highest level. Costs the tier's value times the level.</summary>
        public Fx FacilityExpansionBonus = Fx.Zero;
        /// <summary>Wages that follow production (DEC-082): this share of what a facility made this week, valued at base prices. Off at zero.</summary>
        public Fx FacilityOutputWage = Fx.Zero;
        /// <summary>Public wages in towns (DEC-082): the share of the reserve above target paid out each week. Off at zero.</summary>
        public Fx StatePublicWageShare = Fx.Zero;
        public int FacilityMaxLevel = 5;
        /// <summary>A facility's output without its tools and seed (DEC-081).</summary>
        public Fx FacilityUnaidedOutput = Fx.FromPermille(700);
        /// <summary>NPC investment (DEC-080), off at zero: the longest payback in years a group accepts, and the most facilities on a node.</summary>
        public int InvestmentPaybackYears = 0;
        public int InvestmentMaxPerNode = 24;
        public int InvestmentProjectsPerNode = 2;
        public Fx MigrationStartFreeShare = Fx.FromPermille(200);
        /// <summary>Yearly share of bound people who run away, and how much faster where the lord broke the contract.</summary>
        public Fx MigrationFleeShare = Fx.FromPermille(10);
        public int MigrationBrokenContractFactor = 3;
        public int MigrationJobsPerFacilityLevel = 100000;
        /// <summary>Open town jobs, as a share of jobs, that move a nation to emancipate; years before it starts (Prussia 1807 to 1810); share freed a year; redemption years (Russia, 49) and share of income paid.</summary>
        /// <summary>Years from the start before a nation can enact emancipation.</summary>
        public int MigrationEmancipationEarliestYears = 0;
        public Fx MigrationVacancyShare = Fx.FromPermille(200);
        public int MigrationEmancipationLeadYears = 3;
        public Fx MigrationFreedPerYear = Fx.FromPermille(100);
        public int MigrationRedemptionYears = 49;
        public Fx MigrationRedemptionShare = Fx.FromPermille(50);
        /// <summary>Lower yearly growth in towns: the urban penalty.</summary>
        public Fx PopulationUrbanPenalty = Fx.Zero;
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
        /// <summary>Top, average, most common; the fourth class starts with the player's cash (<see cref="FES.Agents.MerchantCapital"/>).</summary>
        public readonly long[] MerchantClassCash = { 9500, 1800, 100 };
        public readonly int[] MerchantClassSharePermille = { 100, 300, 400 };

        /// <summary>Cargo limits of one merchant group, per level of its home node (DEC-013).</summary>
        /// <summary>Per vehicle (pack, cart, wagon, DEC-084): how far it multiplies a group's cargo and flow.</summary>
        public readonly Fx[] MerchantVehicleCapacity = { Fx.One, Fx.One, Fx.One };
        /// <summary>Per vehicle: the share of groups starting with it; pack takes the rest.</summary>
        /// <summary>The vehicle each capital class starts with, by <see cref="FES.Agents.MerchantCapital"/> class.</summary>
        public readonly byte[] MerchantClassVehicle = { 0, 0, 0, 0 };
        /// <summary>A group buys the next vehicle when its cash is this many times the price; the yearly chance a vehicle wears down one step.</summary>
        public readonly int[] MerchantVehicleCashTimes = { 0, 3, 3 };
        /// <summary>Per vehicle (DEC-085): how far it multiplies running costs, encounter chance and insurance premium.</summary>
        public readonly Fx[] MerchantVehicleCost = { Fx.One, Fx.One, Fx.One };
        public readonly Fx[] MerchantVehicleDanger = { Fx.One, Fx.One, Fx.One };
        public readonly Fx[] MerchantVehiclePremium = { Fx.One, Fx.One, Fx.One };
        /// <summary>Orders (DEC-085): weeks from reserving to collecting, and weeks before an unfilled order lapses.</summary>
        public int OrderLeadWeeks = 8;
        /// <summary>Food grades (DEC-090): price multipliers in place of <see cref="GradePriceMultiplier"/>, and how a food's household demand splits by grade. Off at a zero split.</summary>
        public readonly Fx[] FoodGradePrice = { Fx.Zero, Fx.Zero, Fx.Zero, Fx.Zero };
        public readonly Fx[] FoodGradeDemand = { Fx.Zero, Fx.Zero, Fx.Zero, Fx.Zero };
        /// <summary>Supply contracts (DEC-087), off at zero: signed a year per nation; the least stock to count as having or lacking a good; weekly volume as stock over these weeks; term; discount off the buyer's market, growing with weekly volume to a limit.</summary>
        public int ContractsPerNation = 0;
        public int ContractMinStock = 200;
        public int ContractStockWeeks = 26;
        /// <summary>The share of the buyer market's weekly demand a contract may supply, and the weeks of its bill a buyer must hold to sign.</summary>
        public Fx ContractDemandShare = Fx.FromPermille(500);
        public int ContractCreditWeeks = 8;
        public int ContractMinWeeks = 8;
        public int ContractMaxWeeks = 104;
        /// <summary>The share of the whole contract's value the buyer pays at signing (DEC-088).</summary>
        public Fx ContractDepositShare = Fx.FromPermille(200);
        public Fx ContractBaseDiscount = Fx.FromPermille(50);
        public Fx ContractDiscountPerUnit = Fx.FromPermille(1);
        public Fx ContractMaxDiscount = Fx.FromPermille(250);
        /// <summary>The share of the reserve above target granted yearly to strategic producers (DEC-087).</summary>
        public Fx StrategicSubsidyShare = Fx.Zero;
        /// <summary>Merchant associations (DEC-086), off at zero: the yearly chance a group joins; the yearly fee on cash above its start; the share of start cash under which it is helped, the share of the association's cash a grant may take, and what members pay of the premium with carts and wagons.</summary>
        public Fx MerchantJoinShare = Fx.Zero;
        public Fx MerchantMemberFee = Fx.FromPermille(50);
        public Fx MerchantSupportFloor = Fx.FromPermille(200);
        public Fx MerchantSupportShare = Fx.FromPermille(100);
        public Fx MerchantMemberPremium = Fx.FromPermille(500);
        /// <summary>0: detail around the player. 1: every node detailed. 2: every node aggregate.</summary>
        public int LodMode = 1;
        /// <summary>Continents, nations and regions are classes only: their people, producers and groups stand at their core towns (DEC-104).</summary>
        public bool CoreAbsorb = true;
        public int FacilityCloseLossYears = 2;
        /// <summary>Per class level, continent to region: what being its core is worth. A town that is core of several takes the highest, or with stacking the sum (DEC-104).</summary>
        public readonly int[] CoreWeight = { 3, 2, 1 };
        public bool CoreStacking = false;
        /// <summary>A core town keeps the demand per head of the classes it took in; off, its people buy like any town's.</summary>
        public bool CoreDemandWeighted = true;
        /// <summary>Per point of a core's strength: more wages per head, more demand.</summary>
        public Fx CoreWagePerPoint = Fx.FromPermille(100);
        public Fx CoreDemandPerPoint = Fx.FromPermille(50);
        /// <summary>How many times the expected loss a merchant weighs when deciding on guards.</summary>
        public int MerchantRiskAversion = 1;
        /// <summary>Per personality: how many times a loss weighs against the same gain (DEC-099); 0 uses <see cref="MerchantRiskAversion"/>.</summary>
        public readonly int[] PersonalityLossAversion = new int[3];
        /// <summary>A group counts the trip's upkeep and expected losses when it chooses a load (DEC-099).</summary>
        public bool MerchantCountTripCosts = false;
        /// <summary>Every group acts each turn: with no sale, purchase or trip of its own it sets off on the road (DEC-106).</summary>
        public bool MerchantMustAct = true;
        /// <summary>Price cover (DEC-099): the share of a load's value the bank guarantees, its fee, the fall and its chance groups expect. A zero fee turns it off.</summary>
        public Fx HedgeFloor = Fx.FromPermille(850);
        public Fx HedgeFee = Fx.Zero;
        public Fx HedgeFall = Fx.FromPermille(150);
        public Fx HedgeFallChance = Fx.FromPermille(300);
        /// <summary>The share of its spendable reserve a bank may pay on cover in a week.</summary>
        public Fx HedgeBankShare = Fx.FromPermille(100);
        public int OrderExpiryWeeks = 52;
        /// <summary>Quarterly factory planning (DEC-085), and the least share of capacity a factory keeps running.</summary>
        public bool ForecastEnabled = false;
        public Fx ForecastMinPlan = Fx.FromPermille(100);
        /// <summary>The weight of what households and the factories they set running would take, against recent off-take (DEC-091).</summary>
        public Fx ForecastHouseholdWeight = Fx.One;
        /// <summary>The stock a plan aims to hold, as a share of the quarter's forecast.</summary>
        public Fx ForecastCover = Fx.One;
        /// <summary>Primary producers plan too (DEC-091).</summary>
        public bool ForecastPrimary = false;
        /// <summary>Nobody builds to make a good whose makers run below this share of capacity (DEC-091).</summary>
        public Fx InvestmentMinPlan = Fx.Zero;
        public Fx MerchantVehicleWear = Fx.FromPermille(100);
        public readonly long[] MerchantMaxWeight = { 4000, 4000, 2000, 1000, 400 };
        public readonly long[] MerchantMaxSize = { 3200, 3200, 1600, 800, 300 };

        /// <summary>Weekly running cost of a detailed merchant group, per level of its home node.</summary>
        public readonly long[] MerchantUpkeep = { 50, 40, 30, 20, 10 };

        /// <summary>A detailed merchant group goes bankrupt after this many weeks in a row with cash below zero.</summary>
        public int MerchantBankruptWeeks = 5;
        /// <summary>DEC-122: weeks of upkeep a group keeps in coins when it buys a load.</summary>
        public int MerchantReserveUpkeepWeeks = 2;
        /// <summary>DEC-124: an arriving group sells necessities to the households first, at the price, from their cash.</summary>
        public bool MerchantSellToHouseholds = true;

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
        /// <summary>DEC-130: the bank calls for margin when the debt rises above this share of the collateral, and after these weeks sells shares and land until it is back within the loan-to-value.</summary>
        public Fx MarginCallShare = Fx.FromPermille(750);
        public int MarginCallWeeks = 2;
        /// <summary>Weeks a seized facility stays up for auction (DEC-039).</summary>
        public int AuctionWeeks = 2;
        /// <summary>An auction's reserve price, as a share of the facility's value; an NPC buys at it without a winning bid.</summary>
        public Fx AuctionReserveShare = Fx.FromPermille(800);
        /// <summary>Value of a level-1 facility as collateral, per industry tier; scales with level.</summary>
        public readonly long[] FacilityValue = { 3000, 8000, 5000 };
        /// <summary>DEC-125: share of the new type's value that a change of use costs.</summary>
        public Fx FacilityRepurposeShare = Fx.FromPermille(500);
        /// <summary>DEC-128: plots per node by level, and a plot's value when all are free.</summary>
        public readonly int[] LandPlots = { 10, 10, 10, 40, 12 };
        /// <summary>DEC-129: shares a facility is split into, and how many its group may let others hold, so it keeps control.</summary>
        public int FacilityShares = 100;
        public int FacilityMaxListedShares = 49;
        public readonly long[] LandValue = { 2000, 2000, 2000, 1500, 500 };
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
        /// <summary>DEC-131: the asset markets. Households draw this share of their borrowing room a week on a market not expected to fall; credit moves the premium by this weight of its share of the fundamental value; this share of the last quarter's change carries on; the premium is pulled back toward one by this share a year; it moves at most this share a week, within the floor and cap.</summary>
        public Fx MarketCreditShare = Fx.FromPermille(5);
        public Fx MarketCreditWeight = Fx.FromPermille(250);
        public Fx MarketMomentum = Fx.FromPermille(900);
        public Fx MarketReversion = Fx.FromPermille(300);
        public Fx MarketWeeklyMove = Fx.FromPermille(30);
        public Fx MarketPremiumFloor = Fx.FromPermille(200);
        public Fx MarketPremiumCap = Fx.FromInt(10);
        /// <summary>DEC-131: what buyers expect rises by this weight of the quarter's new idle money over the market's value.</summary>
        public Fx MarketIdleWeight = Fx.FromPermille(400);
        public Fx MarketPropertyIdleWeight = Fx.FromPermille(20);
        public Fx MarketVentureIdleWeight = Fx.FromPermille(30);
        /// <summary>DEC-131: the share of their margin debt households pay off in a year.</summary>
        public Fx MarketRepayShare = Fx.FromPermille(200);
        /// <summary>DEC-131: a property market's burst stops its nation's credit when its debt is at least this share of what the bank has lent.</summary>
        public Fx MarketCrunchShare = Fx.FromPermille(100);
        /// <summary>DEC-131: published lines. Heated and burst on the price over the fundamental; a burst also when credit shrinks above the leverage limit; the price falls by the drop at a burst.</summary>
        public Fx MarketHeatedPremium = Fx.FromPermille(1500);
        public Fx MarketBurstPremium = Fx.FromPermille(2500);
        public Fx MarketLeverageLimit = Fx.FromPermille(600);
        public Fx MarketBurstDrop = Fx.FromPermille(300);
        /// <summary>DEC-132: trading ventures per continent, their shares, the capital they raise, a voyage's weeks, the chance it is lost, the least margin they sail for, and the years of expected profit in their value.</summary>
        public int VenturesPerContinent = 3;
        public int VentureShares = 100;
        public long VentureCapital = 20000;
        public int VentureVoyageWeeks = 26;
        public int VentureLossPermille = 100;
        public Fx VentureMinMargin = Fx.FromPermille(200);
        public int VentureValueYears = 2;
        /// <summary>DEC-133: short selling. The licence's fee, the deposit it needs, the weeks of trading before it; the deposit's share of a short's value on opening and its call line; the yearly fee to the lender; the most shares lent on one asset.</summary>
        public long ShortLicenceFee = 1000;
        public long ShortLicenceDeposit = 5000;
        public int ShortLicenceWeeks = 52;
        public Fx ShortOpenShare = Fx.FromPermille(1500);
        public Fx ShortCallShare = Fx.FromPermille(1300);
        public Fx ShortFee = Fx.FromPermille(50);
        public int ShortMaxLentShares = 20;
        /// <summary>DEC-134: land rent as parts per million of the node's households' yearly income, split over its plots under houses, and the years of it in a plot's value.</summary>
        public long LandRentPpm = 30;
        public int LandRentYears = 10;
        /// <summary>DEC-134: a credit crunch lifts loan rates as a rate spike does.</summary>
        public bool CrunchLiftsRates = true;
        /// <summary>DEC-134: groups list their facilities' shares at or above this premium, and buy them back at or below that; zero for never.</summary>
        public Fx GroupListPremium = Fx.FromPermille(1300);
        public Fx GroupBuybackPremium = Fx.FromPermille(800);
        /// <summary>DEC-131: a bank's cover (reserve over loans) under which a run starts and a heated market bursts; zero for none.</summary>
        public Fx BankRunCover = Fx.Zero;
        /// <summary>Loan rates are multiplied by this during a rate spike.</summary>
        public Fx RateSpikeMultiplier = Fx.FromInt(2);

        /// <summary>A license costs this times the good's Mid base price (DEC-024).</summary>
        public Fx LicensePriceMultiplier = Fx.FromInt(20);

        /// <summary>Share of merchant groups that hold licenses, in permille (DEC-024).</summary>
        public int LicensedMerchantPermille = 200;

        /// <summary>Share of merchant groups licensed to trade into other nations, in permille (DEC-116).</summary>
        public int InternationalMerchantPermille = 500;

        /// <summary>Share of merchant groups licensed to trade to other continents, in permille (DEC-116).</summary>
        public int IntercontinentalMerchantPermille = 500;

        /// <summary>What the player pays a nation for a year's license to carry cargo out to other nations (DEC-116).</summary>
        public long InternationalLicensePrice = 300;

        /// <summary>What the player pays a nation for a year's license to carry cargo out to other continents (DEC-116).</summary>
        public long IntercontinentalLicensePrice = 1500;

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
        /// <summary>Groups weigh selling here against carrying the load on, and can sell at a loss to save themselves (DEC-093). Off: they sell all on arrival.</summary>
        public bool MerchantWeighSales = true;
        /// <summary>How many markets ahead a group judges a load (DEC-095); 1 sees only the next one.</summary>
        public int MerchantJudgeHops = 1;
        /// <summary>A wish a group cannot fill bids the price up, as a factory's does (DEC-095).</summary>
        public bool MerchantBids = false;
        /// <summary>A group leaving with one load still bids for a better one it could not find (DEC-096).</summary>
        public bool MerchantBidsWhileLoaded = false;
        /// <summary>A group stranded above its home with nothing to carry goes back empty (DEC-095).</summary>
        public bool MerchantReturnHome = false;
        /// <summary>A market whose traders cannot pay for this many units is no market to a group judging a load; 0 off (DEC-096).</summary>
        public int MerchantJudgeCoinUnits = 0;
        /// <summary>A large enough group based at a sea port may sail whatever its personality (DEC-096).</summary>
        public bool MerchantPortCrossers = false;
        /// <summary>Nations keep their international bank between its starting share of the coin supply and twice that (DEC-096).</summary>
        public bool InternationalBalance = false;
        /// <summary>Traders pay sellers the price less this margin (DEC-098).</summary>
        public Fx TraderSpread = Fx.Zero;
        /// <summary>What traders pay a seller for a unit at this price.</summary>
        public int TraderOffer(int price) => price - (int)TraderSpread.MulInt(price);

        /// <summary>What a sum at list price brings a seller.</summary>
        public long TraderOffer(long worth) => worth - TraderSpread.MulInt(worth);
        /// <summary>Weeks of households' wishes a float keeps; a share of what is above goes to the nation's households a week; 0 off (DEC-098).</summary>
        public int TraderFloatKeepWeeks = 0;
        public Fx TraderFloatPayout = Fx.FromPermille(20);
        /// <summary>Share of an aggregate flow's margin the traders pay as wages at the origin (DEC-098).</summary>
        public Fx MerchantFlowWage = Fx.Zero;
        /// <summary>The lowest level whose groups trade between continents (DEC-096): 0 continent, 1 nation, 2 region; below 0 none.</summary>
        public int MerchantCrossLevel = -1;
        /// <summary>The share of the normal flow that aggregate trade runs at between continents (DEC-066); zero is none.</summary>
        public Fx MerchantCrossFlow = Fx.Zero;
        /// <summary>How many of each continent-bound producer start on its continent (DEC-096).</summary>
        public int ContinentalProducers = 3;
        /// <summary>Bills (DEC-097): weeks of households' wishes traders may owe (zero is off), the weeks to maturity, the bank's discount, the share of the float paid on bills a week, the weeks overdue before a bill is lost, and the share of the reserve target under which the bank stops discounting.</summary>
        public int BillCapWeeks = 0;
        public int BillWeeks = 8;
        public Fx BillDiscount = Fx.FromPermille(20);
        public Fx BillRepayShare = Fx.FromPermille(250);
        public int BillDefaultWeeks = 26;
        public Fx BillBankFloor = Fx.FromPermille(500);
        /// <summary>DEC-122: what buyers take off a bill's collectable part when a group short of coins sells it; zero closes the market.</summary>
        public Fx BillSaleDiscount = Fx.FromPermille(150);
        /// <summary>Producers too take bills for what traders cannot pay (DEC-097).</summary>
        public bool BillProducers = false;
        /// <summary>Bonds (DEC-097): the weekly share of idle money holders offer, weeks of spending households keep, and times its starting cash a group keeps.</summary>
        public Fx BondBuyShare = Fx.FromPermille(5);
        public int BondKeepWeeks = 26;
        public int BondGroupKeepTimes = 2;
        /// <summary>Government bonds: the debt limit as a share of the nation's part of the coin supply (zero is off), yearly interest, and weeks to repay.</summary>
        public Fx StateDebtShare = Fx.Zero;
        public Fx StateBondInterest = Fx.FromPermille(40);
        public int StateBondWeeks = 1040;
        /// <summary>Corporate bonds: the debt limit as a share of the value of a group's facilities (zero is off), yearly interest, weeks to repay, weeks behind before default.</summary>
        public Fx CompanyDebtShare = Fx.Zero;
        public Fx CompanyBondInterest = Fx.FromPermille(70);
        public int CompanyBondWeeks = 520;
        public int CompanyDefaultWeeks = 26;
        /// <summary>Weeks of costs a group keeps its facilities in cash for; zero is off (DEC-097).</summary>
        public int WorkingWeeks = 0;
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
    }
}
