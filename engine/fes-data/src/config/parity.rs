//! JSON of the config in the C# `ParityJson` shape (feature `parity`).

use super::SimulationConfig;
use super::{TierCadence, WorldLevel};
use fes_core::Fx;
use serde_json::{json, Map, Value};

fn put(m: &mut Fields, name: &str, value: Value) {
    m.insert(name.to_string(), value);
}

fn fx_str(value: Fx) -> Value {
    Value::String(value.raw().to_string())
}

fn long_str(value: i64) -> Value {
    Value::String(value.to_string())
}

type Fields = Map<String, Value>;

fn fx_arr(values: &[Fx]) -> Value {
    Value::Array(values.iter().map(|v| fx_str(*v)).collect())
}

fn long_arr(values: &[i64]) -> Value {
    Value::Array(values.iter().map(|v| long_str(*v)).collect())
}

fn level_arr(values: &[WorldLevel]) -> Value {
    json!(values.iter().map(|v| v.name()).collect::<Vec<_>>())
}

fn cadence_arr(values: &[TierCadence]) -> Value {
    Value::Array(
        values
            .iter()
            .map(|v| json!({"Period": v.period, "Offset": v.offset}))
            .collect(),
    )
}

fn part0(c: &SimulationConfig, m: &mut Fields) {
    put(m, "TierCadences", cadence_arr(&c.tier_cadences));
    put(m, "GradePriceMultiplier", fx_arr(&c.grade_price_multiplier));
    put(m, "LevelDemandWeight", fx_arr(&c.level_demand_weight));
    put(m, "NecessityElasticity", fx_str(c.necessity_elasticity));
    put(m, "NecessityChoke", fx_str(c.necessity_choke));
    put(m, "LuxuryElasticity", fx_str(c.luxury_elasticity));
    put(m, "GradeLuxuryShare", fx_arr(&c.grade_luxury_share));
    put(
        m,
        "SemiGradeLuxuryShare",
        fx_arr(&c.semi_grade_luxury_share),
    );
    put(m, "MaxCheapDemand", fx_str(c.max_cheap_demand));
    put(m, "QuintileIncomeShare", fx_arr(&c.quintile_income_share));
    put(m, "DefaultPopulation", long_arr(&c.default_population));
    put(
        m,
        "DefaultIncomePerHead",
        fx_arr(&c.default_income_per_head),
    );
    put(m, "PopulationBaseGrowth", fx_str(c.population_base_growth));
    put(
        m,
        "PopulationGrowthResponse",
        fx_str(c.population_growth_response),
    );
    put(m, "PopulationMaxGrowth", fx_str(c.population_max_growth));
    put(m, "PopulationMaxDecline", fx_str(c.population_max_decline));
    put(m, "IncomeResponse", fx_str(c.income_response));
    put(m, "IncomeMaxChange", fx_str(c.income_max_change));
    put(m, "NeedMemoryWeeks", json!(c.need_memory_weeks));
    put(m, "HouseholdBudgetWeeks", json!(c.household_budget_weeks));
}

fn part1(c: &SimulationConfig, m: &mut Fields) {
    put(m, "HouseholdAlmsShare", fx_str(c.household_alms_share));
    put(m, "LuxuryWealthPerYear", fx_str(c.luxury_wealth_per_year));
    put(m, "LuxuryWealthCap", fx_str(c.luxury_wealth_cap));
    put(m, "LuxuryTax", fx_str(c.luxury_tax));
    put(m, "TraderFloatShare", fx_str(c.trader_float_share));
    put(m, "TraderLoanShare", fx_str(c.trader_loan_share));
    put(m, "TraderLoanKeepWeeks", json!(c.trader_loan_keep_weeks));
    put(m, "TraderBankLoanShare", fx_str(c.trader_bank_loan_share));
    put(m, "TraderLoanInterest", fx_str(c.trader_loan_interest));
    put(m, "TraderLoanWeeks", json!(c.trader_loan_weeks));
    put(m, "TraderLoanRepayShare", fx_str(c.trader_loan_repay_share));
    put(m, "ProducerFloatShare", fx_str(c.producer_float_share));
    put(m, "LevelWageWeight", fx_arr(&c.level_wage_weight));
    put(m, "NobleRoyalShare", fx_str(c.noble_royal_share));
    put(m, "NobleCityShare", fx_str(c.noble_city_share));
    put(m, "NobleVillageShare", fx_str(c.noble_village_share));
    put(m, "NobleAidShare", fx_str(c.noble_aid_share));
    put(m, "NobleProtectionShare", fx_str(c.noble_protection_share));
    put(m, "NobleSpendShare", fx_str(c.noble_spend_share));
    put(m, "VillageWageAwayShare", fx_str(c.village_wage_away_share));
}

fn part2(c: &SimulationConfig, m: &mut Fields) {
    put(m, "MigrationMaxShare", fx_str(c.migration_max_share));
    put(
        m,
        "FacilityExpansionBonus",
        fx_str(c.facility_expansion_bonus),
    );
    put(m, "FacilityOutputWage", fx_str(c.facility_output_wage));
    put(m, "StatePublicWageShare", fx_str(c.state_public_wage_share));
    put(m, "FacilityMaxLevel", json!(c.facility_max_level));
    put(
        m,
        "FacilityUnaidedOutput",
        fx_str(c.facility_unaided_output),
    );
    put(
        m,
        "InvestmentPaybackYears",
        json!(c.investment_payback_years),
    );
    put(m, "InvestmentMaxPerNode", json!(c.investment_max_per_node));
    put(
        m,
        "InvestmentProjectsPerNode",
        json!(c.investment_projects_per_node),
    );
    put(
        m,
        "MigrationStartFreeShare",
        fx_str(c.migration_start_free_share),
    );
    put(m, "MigrationFleeShare", fx_str(c.migration_flee_share));
    put(
        m,
        "MigrationBrokenContractFactor",
        json!(c.migration_broken_contract_factor),
    );
    put(
        m,
        "MigrationJobsPerFacilityLevel",
        json!(c.migration_jobs_per_facility_level),
    );
    put(
        m,
        "MigrationEmancipationEarliestYears",
        json!(c.migration_emancipation_earliest_years),
    );
    put(
        m,
        "MigrationVacancyShare",
        fx_str(c.migration_vacancy_share),
    );
    put(
        m,
        "MigrationEmancipationLeadYears",
        json!(c.migration_emancipation_lead_years),
    );
    put(
        m,
        "MigrationFreedPerYear",
        fx_str(c.migration_freed_per_year),
    );
    put(
        m,
        "MigrationRedemptionYears",
        json!(c.migration_redemption_years),
    );
    put(
        m,
        "MigrationRedemptionShare",
        fx_str(c.migration_redemption_share),
    );
    put(
        m,
        "PopulationUrbanPenalty",
        fx_str(c.population_urban_penalty),
    );
}

fn part3(c: &SimulationConfig, m: &mut Fields) {
    put(m, "LaborBaseEmployment", fx_str(c.labor_base_employment));
    put(m, "LaborMinEmployment", fx_str(c.labor_min_employment));
    put(m, "LaborMaxEmployment", fx_str(c.labor_max_employment));
    put(m, "LaborResponse", fx_str(c.labor_response));
    put(m, "LaborLongRunResponse", fx_str(c.labor_long_run_response));
    put(m, "LaborMerchantWage", fx_str(c.labor_merchant_wage));
    put(m, "LaborMerchantBuffer", fx_str(c.labor_merchant_buffer));
    put(m, "TaxHousehold", fx_str(c.tax_household));
    put(m, "TaxCompany", fx_str(c.tax_company));
    put(m, "DefenseShare", fx_str(c.defense_share));
    put(m, "DefenseMaintenance", fx_str(c.defense_maintenance));
    put(m, "DefenseIntervalWeeks", json!(c.defense_interval_weeks));
    put(
        m,
        "DefenseSafetyCostPerRoute",
        json!(c.defense_safety_cost_per_route),
    );
    put(m, "DefenseMaxSafety", fx_str(c.defense_max_safety));
    put(m, "SubsidyShare", fx_str(c.subsidy_share));
    put(m, "SubsidyRate", fx_str(c.subsidy_rate));
    put(m, "SubsidyYears", json!(c.subsidy_years));
    put(m, "SubsidyMaxPerNation", json!(c.subsidy_max_per_nation));
    put(m, "StateProjectShare", fx_str(c.state_project_share));
    put(m, "StateProjectsPerYear", json!(c.state_projects_per_year));
}

fn part4(c: &SimulationConfig, m: &mut Fields) {
    put(m, "StateProjectWeeks", json!(c.state_project_weeks));
    put(m, "StateTradeScore", json!(c.state_trade_score));
    put(m, "StateRoadScore", json!(c.state_road_score));
    put(m, "StateFarmlandCost", long_str(c.state_farmland_cost));
    put(m, "StateMineCost", long_str(c.state_mine_cost));
    put(m, "StateHarborCost", long_str(c.state_harbor_cost));
    put(
        m,
        "StateHighwayCostPerTurn",
        long_str(c.state_highway_cost_per_turn),
    );
    put(m, "StateLandBonus", fx_str(c.state_land_bonus));
    put(m, "StateLandMax", json!(c.state_land_max));
    put(m, "StateHarborBonus", fx_str(c.state_harbor_bonus));
    put(m, "StateHarborMax", json!(c.state_harbor_max));
    put(m, "StateHighwayBonus", fx_str(c.state_highway_bonus));
    put(m, "StateFacilityLevy", fx_str(c.state_facility_levy));
    put(m, "PolicyStep", fx_str(c.policy_step));
    put(m, "PolicyMaxSteps", json!(c.policy_max_steps));
    put(m, "PolicyMoveThreshold", fx_str(c.policy_move_threshold));
    put(m, "PolicyResistance", fx_str(c.policy_resistance));
    put(m, "InfluenceAssetWeight", fx_str(c.influence_asset_weight));
    put(m, "InfluenceLobbyWeight", fx_str(c.influence_lobby_weight));
    put(m, "InfluencePerJob", long_str(c.influence_per_job));
}

fn part5(c: &SimulationConfig, m: &mut Fields) {
    put(m, "JobsPerFacilityLevel", json!(c.jobs_per_facility_level));
    put(m, "JobsPerMerchantGroup", json!(c.jobs_per_merchant_group));
    put(m, "NpcLobbyShare", fx_str(c.npc_lobby_share));
    put(m, "PolicyLossTilt", fx_str(c.policy_loss_tilt));
    put(m, "InflationOpposition", fx_str(c.inflation_opposition));
    put(m, "MerchantsPerNode", json!(c.merchants_per_node));
    put(m, "MerchantClassCash", long_arr(&c.merchant_class_cash));
    put(
        m,
        "MerchantClassSharePermille",
        json!(c.merchant_class_share_permille),
    );
    put(
        m,
        "MerchantVehicleCapacity",
        fx_arr(&c.merchant_vehicle_capacity),
    );
    put(m, "MerchantClassVehicle", json!(c.merchant_class_vehicle));
    put(
        m,
        "MerchantVehicleCashTimes",
        json!(c.merchant_vehicle_cash_times),
    );
    put(m, "MerchantVehicleCost", fx_arr(&c.merchant_vehicle_cost));
    put(
        m,
        "MerchantVehicleDanger",
        fx_arr(&c.merchant_vehicle_danger),
    );
    put(
        m,
        "MerchantVehiclePremium",
        fx_arr(&c.merchant_vehicle_premium),
    );
    put(m, "OrderLeadWeeks", json!(c.order_lead_weeks));
    put(m, "FoodGradePrice", fx_arr(&c.food_grade_price));
    put(m, "FoodGradeDemand", fx_arr(&c.food_grade_demand));
    put(m, "ContractsPerNation", json!(c.contracts_per_nation));
    put(m, "ContractMinStock", json!(c.contract_min_stock));
    put(m, "ContractStockWeeks", json!(c.contract_stock_weeks));
}

fn part6(c: &SimulationConfig, m: &mut Fields) {
    put(m, "ContractDemandShare", fx_str(c.contract_demand_share));
    put(m, "ContractCreditWeeks", json!(c.contract_credit_weeks));
    put(m, "ContractMinWeeks", json!(c.contract_min_weeks));
    put(m, "ContractMaxWeeks", json!(c.contract_max_weeks));
    put(m, "ContractDepositShare", fx_str(c.contract_deposit_share));
    put(m, "ContractBaseDiscount", fx_str(c.contract_base_discount));
    put(
        m,
        "ContractDiscountPerUnit",
        fx_str(c.contract_discount_per_unit),
    );
    put(m, "ContractMaxDiscount", fx_str(c.contract_max_discount));
    put(
        m,
        "StrategicSubsidyShare",
        fx_str(c.strategic_subsidy_share),
    );
    put(m, "MerchantJoinShare", fx_str(c.merchant_join_share));
    put(m, "MerchantMemberFee", fx_str(c.merchant_member_fee));
    put(m, "MerchantSupportFloor", fx_str(c.merchant_support_floor));
    put(m, "MerchantSupportShare", fx_str(c.merchant_support_share));
    put(
        m,
        "MerchantMemberPremium",
        fx_str(c.merchant_member_premium),
    );
    put(m, "LodMode", json!(c.lod_mode));
    put(m, "CoreAbsorb", json!(c.core_absorb));
    put(
        m,
        "FacilityCloseLossYears",
        json!(c.facility_close_loss_years),
    );
    put(m, "CoreWeight", json!(c.core_weight));
    put(m, "CoreStacking", json!(c.core_stacking));
    put(m, "CoreDemandWeighted", json!(c.core_demand_weighted));
}

fn part7(c: &SimulationConfig, m: &mut Fields) {
    put(m, "CoreWagePerPoint", fx_str(c.core_wage_per_point));
    put(m, "CoreDemandPerPoint", fx_str(c.core_demand_per_point));
    put(m, "MerchantRiskAversion", json!(c.merchant_risk_aversion));
    put(
        m,
        "PersonalityLossAversion",
        json!(c.personality_loss_aversion),
    );
    put(
        m,
        "MerchantCountTripCosts",
        json!(c.merchant_count_trip_costs),
    );
    put(m, "MerchantMustAct", json!(c.merchant_must_act));
    put(m, "HedgeFloor", fx_str(c.hedge_floor));
    put(m, "HedgeFee", fx_str(c.hedge_fee));
    put(m, "HedgeFall", fx_str(c.hedge_fall));
    put(m, "HedgeFallChance", fx_str(c.hedge_fall_chance));
    put(m, "HedgeBankShare", fx_str(c.hedge_bank_share));
    put(m, "OrderExpiryWeeks", json!(c.order_expiry_weeks));
    put(m, "ForecastEnabled", json!(c.forecast_enabled));
    put(m, "ForecastMinPlan", fx_str(c.forecast_min_plan));
    put(
        m,
        "ForecastHouseholdWeight",
        fx_str(c.forecast_household_weight),
    );
    put(m, "ForecastCover", fx_str(c.forecast_cover));
    put(m, "ForecastPrimary", json!(c.forecast_primary));
    put(m, "InvestmentMinPlan", fx_str(c.investment_min_plan));
    put(m, "MerchantVehicleWear", fx_str(c.merchant_vehicle_wear));
    put(m, "MerchantMaxWeight", long_arr(&c.merchant_max_weight));
}

fn part8(c: &SimulationConfig, m: &mut Fields) {
    put(m, "MerchantMaxSize", long_arr(&c.merchant_max_size));
    put(m, "MerchantUpkeep", long_arr(&c.merchant_upkeep));
    put(m, "MerchantBankruptWeeks", json!(c.merchant_bankrupt_weeks));
    put(
        m,
        "MerchantReserveUpkeepWeeks",
        json!(c.merchant_reserve_upkeep_weeks),
    );
    put(
        m,
        "MerchantSellToHouseholds",
        json!(c.merchant_sell_to_households),
    );
    put(m, "MerchantFlowShare", fx_str(c.merchant_flow_share));
    put(
        m,
        "ProducersPerNode",
        json!(c.producers_per_node.iter().flatten().collect::<Vec<_>>()),
    );
    put(
        m,
        "ProductionVariancePermille",
        json!(c.production_variance_permille),
    );
    put(m, "FacilityStartCash", long_arr(&c.facility_start_cash));
    put(
        m,
        "SpecialtyOutputMultiplier",
        fx_str(c.specialty_output_multiplier),
    );
    put(
        m,
        "SpecialtyGradeUpChance",
        fx_str(c.specialty_grade_up_chance),
    );
    put(
        m,
        "SpecialtyPriceMultiplier",
        fx_str(c.specialty_price_multiplier),
    );
    put(m, "TerrainExtraTurns", json!(c.terrain_extra_turns));
    put(
        m,
        "TerrainCostMultiplier",
        fx_arr(&c.terrain_cost_multiplier),
    );
    put(m, "TerrainDanger", fx_arr(&c.terrain_danger));
    put(m, "RegionDanger", fx_str(c.region_danger));
    put(m, "DangerValueScale", long_str(c.danger_value_scale));
    put(m, "EncounterCashLoss", fx_str(c.encounter_cash_loss));
    put(m, "EncounterCargoLoss", fx_str(c.encounter_cargo_loss));
    put(m, "GuardBaseCost", long_str(c.guard_base_cost));
}

fn part9(c: &SimulationConfig, m: &mut Fields) {
    put(m, "GuardCostShare", fx_str(c.guard_cost_share));
    put(m, "GuardReduction", fx_str(c.guard_reduction));
    put(m, "InsurancePremium", fx_str(c.insurance_premium));
    put(m, "InsuranceRefund", fx_str(c.insurance_refund));
    put(m, "PassageBaseFee", long_str(c.passage_base_fee));
    put(m, "PassagePerUnit", long_str(c.passage_per_unit));
    put(m, "ShipPrice", long_str(c.ship_price));
    put(m, "ShipMaxWeight", long_str(c.ship_max_weight));
    put(m, "ShipMaxSize", long_str(c.ship_max_size));
    put(m, "ShipResaleShare", fx_str(c.ship_resale_share));
    put(m, "RoadCostReduction", fx_str(c.road_cost_reduction));
    put(m, "RoadDangerReduction", fx_str(c.road_danger_reduction));
    put(m, "RoadUpkeep", long_str(c.road_upkeep));
    put(m, "RoadDecayPermille", json!(c.road_decay_permille));
    put(
        m,
        "RoadBuildCostPerTurn",
        long_str(c.road_build_cost_per_turn),
    );
    put(m, "RoadBuildWeeks", json!(c.road_build_weeks));
    put(m, "GroupStartCash", long_arr(&c.group_start_cash));
    put(m, "CompaniesPerNation", json!(c.companies_per_nation));
    put(m, "GuildFee", long_str(c.guild_fee));
    put(
        m,
        "GuildInsuranceDiscount",
        fx_str(c.guild_insurance_discount),
    );
}

fn part10(c: &SimulationConfig, m: &mut Fields) {
    put(m, "GuildLicenseDiscount", fx_str(c.guild_license_discount));
    put(m, "GuildHallMaxWeight", long_str(c.guild_hall_max_weight));
    put(m, "GuildHallMaxSize", long_str(c.guild_hall_max_size));
    put(
        m,
        "BankInitialReserveShare",
        fx_str(c.bank_initial_reserve_share),
    );
    put(
        m,
        "DepositInterestPerYear",
        fx_str(c.deposit_interest_per_year),
    );
    put(m, "ExchangeFee", fx_str(c.exchange_fee));
    put(m, "LoanInterestPerYear", fx_str(c.loan_interest_per_year));
    put(m, "LoanTermWeeks", json!(c.loan_term_weeks));
    put(m, "LoanToValue", fx_str(c.loan_to_value));
    put(m, "LoanGraceWeeks", json!(c.loan_grace_weeks));
    put(m, "MarginCallShare", fx_str(c.margin_call_share));
    put(m, "MarginCallWeeks", json!(c.margin_call_weeks));
    put(m, "AuctionWeeks", json!(c.auction_weeks));
    put(m, "AuctionReserveShare", fx_str(c.auction_reserve_share));
    put(m, "FacilityValue", long_arr(&c.facility_value));
    put(
        m,
        "FacilityRepurposeShare",
        fx_str(c.facility_repurpose_share),
    );
    put(m, "LandPlots", json!(c.land_plots));
    put(m, "FacilityShares", json!(c.facility_shares));
    put(
        m,
        "FacilityMaxListedShares",
        json!(c.facility_max_listed_shares),
    );
    put(m, "LandValue", long_arr(&c.land_value));
}

fn part11(c: &SimulationConfig, m: &mut Fields) {
    put(
        m,
        "InternationalBankReserveShare",
        fx_str(c.international_bank_reserve_share),
    );
    put(m, "TargetReserveRatio", fx_str(c.target_reserve_ratio));
    put(
        m,
        "MaxLoanRateMultiplier",
        fx_str(c.max_loan_rate_multiplier),
    );
    put(m, "StateReserveTarget", fx_str(c.state_reserve_target));
    put(m, "StateSpendingShare", fx_str(c.state_spending_share));
    put(m, "NpcLoanTermWeeks", json!(c.npc_loan_term_weeks));
    put(m, "NpcLoanBufferWeeks", json!(c.npc_loan_buffer_weeks));
    put(
        m,
        "NpcLoanMaxReserveShare",
        fx_str(c.npc_loan_max_reserve_share),
    );
    put(
        m,
        "FinanceHistoryQuarters",
        json!(c.finance_history_quarters),
    );
    put(
        m,
        "FinancialEventChancePermille",
        json!(c.financial_event_chance_permille),
    );
    put(
        m,
        "FinancialEventEndReserveRatio",
        fx_str(c.financial_event_end_reserve_ratio),
    );
    put(m, "BankRunDrain", fx_str(c.bank_run_drain));
    put(m, "BankRunWithdrawShare", fx_str(c.bank_run_withdraw_share));
    put(m, "MarketCreditShare", fx_str(c.market_credit_share));
    put(m, "MarketCreditWeight", fx_str(c.market_credit_weight));
    put(m, "MarketMomentum", fx_str(c.market_momentum));
    put(m, "MarketReversion", fx_str(c.market_reversion));
    put(m, "MarketWeeklyMove", fx_str(c.market_weekly_move));
    put(m, "MarketPremiumFloor", fx_str(c.market_premium_floor));
    put(m, "MarketPremiumCap", fx_str(c.market_premium_cap));
}

fn part12(c: &SimulationConfig, m: &mut Fields) {
    put(m, "MarketIdleWeight", fx_str(c.market_idle_weight));
    put(
        m,
        "MarketPropertyIdleWeight",
        fx_str(c.market_property_idle_weight),
    );
    put(
        m,
        "MarketVentureIdleWeight",
        fx_str(c.market_venture_idle_weight),
    );
    put(m, "MarketRepayShare", fx_str(c.market_repay_share));
    put(m, "MarketCrunchShare", fx_str(c.market_crunch_share));
    put(m, "MarketHeatedPremium", fx_str(c.market_heated_premium));
    put(m, "MarketBurstPremium", fx_str(c.market_burst_premium));
    put(m, "MarketLeverageLimit", fx_str(c.market_leverage_limit));
    put(m, "MarketBurstDrop", fx_str(c.market_burst_drop));
    put(m, "VenturesPerContinent", json!(c.ventures_per_continent));
    put(m, "VentureShares", json!(c.venture_shares));
    put(m, "VentureCapital", long_str(c.venture_capital));
    put(m, "VentureVoyageWeeks", json!(c.venture_voyage_weeks));
    put(m, "VentureLossPermille", json!(c.venture_loss_permille));
    put(m, "VentureMinMargin", fx_str(c.venture_min_margin));
    put(m, "VentureValueYears", json!(c.venture_value_years));
    put(m, "ShortLicenceFee", long_str(c.short_licence_fee));
    put(m, "ShortLicenceDeposit", long_str(c.short_licence_deposit));
    put(m, "ShortLicenceWeeks", json!(c.short_licence_weeks));
    put(m, "ShortOpenShare", fx_str(c.short_open_share));
}

fn part13(c: &SimulationConfig, m: &mut Fields) {
    put(m, "ShortCallShare", fx_str(c.short_call_share));
    put(m, "ShortFee", fx_str(c.short_fee));
    put(m, "ShortMaxLentShares", json!(c.short_max_lent_shares));
    put(m, "StateMaxPerNode", json!(c.state_max_per_node));
    put(m, "LandRentPpm", long_str(c.land_rent_ppm));
    put(m, "LandRentYears", json!(c.land_rent_years));
    put(m, "CrunchLiftsRates", json!(c.crunch_lifts_rates));
    put(m, "GroupListPremium", fx_str(c.group_list_premium));
    put(m, "GroupBuybackPremium", fx_str(c.group_buyback_premium));
    put(m, "BankRunCover", fx_str(c.bank_run_cover));
    put(m, "RateSpikeMultiplier", fx_str(c.rate_spike_multiplier));
    put(
        m,
        "LicensePriceMultiplier",
        fx_str(c.license_price_multiplier),
    );
    put(
        m,
        "LicensedMerchantPermille",
        json!(c.licensed_merchant_permille),
    );
    put(
        m,
        "InternationalMerchantPermille",
        json!(c.international_merchant_permille),
    );
    put(
        m,
        "IntercontinentalMerchantPermille",
        json!(c.intercontinental_merchant_permille),
    );
    put(
        m,
        "InternationalLicensePrice",
        long_str(c.international_license_price),
    );
    put(
        m,
        "IntercontinentalLicensePrice",
        long_str(c.intercontinental_license_price),
    );
    put(m, "TollRate", fx_str(c.toll_rate));
    put(
        m,
        "TollSpecialtySurcharge",
        fx_str(c.toll_specialty_surcharge),
    );
    put(m, "FacilityUpkeep", long_arr(&c.facility_upkeep));
}

fn part14(c: &SimulationConfig, m: &mut Fields) {
    put(m, "FacilityBankruptWeeks", json!(c.facility_bankrupt_weeks));
    put(m, "MerchantTraitsEnabled", json!(c.merchant_traits_enabled));
    put(m, "MerchantRumorsEnabled", json!(c.merchant_rumors_enabled));
    put(m, "MerchantWeighSales", json!(c.merchant_weigh_sales));
    put(m, "MerchantJudgeHops", json!(c.merchant_judge_hops));
    put(m, "MerchantBids", json!(c.merchant_bids));
    put(
        m,
        "MerchantBidsWhileLoaded",
        json!(c.merchant_bids_while_loaded),
    );
    put(m, "MerchantReturnHome", json!(c.merchant_return_home));
    put(
        m,
        "MerchantJudgeCoinUnits",
        json!(c.merchant_judge_coin_units),
    );
    put(m, "MerchantPortCrossers", json!(c.merchant_port_crossers));
    put(m, "InternationalBalance", json!(c.international_balance));
    put(m, "TraderSpread", fx_str(c.trader_spread));
    put(m, "TraderFloatKeepWeeks", json!(c.trader_float_keep_weeks));
    put(m, "TraderFloatPayout", fx_str(c.trader_float_payout));
    put(m, "MerchantFlowWage", fx_str(c.merchant_flow_wage));
    put(m, "MerchantCrossLevel", json!(c.merchant_cross_level));
    put(m, "MerchantCrossFlow", fx_str(c.merchant_cross_flow));
    put(m, "ContinentalProducers", json!(c.continental_producers));
    put(m, "BillCapWeeks", json!(c.bill_cap_weeks));
    put(m, "BillWeeks", json!(c.bill_weeks));
}

fn part15(c: &SimulationConfig, m: &mut Fields) {
    put(m, "BillDiscount", fx_str(c.bill_discount));
    put(m, "BillRepayShare", fx_str(c.bill_repay_share));
    put(m, "BillDefaultWeeks", json!(c.bill_default_weeks));
    put(m, "BillBankFloor", fx_str(c.bill_bank_floor));
    put(m, "BillSaleDiscount", fx_str(c.bill_sale_discount));
    put(m, "BillProducers", json!(c.bill_producers));
    put(m, "BondBuyShare", fx_str(c.bond_buy_share));
    put(m, "BondKeepWeeks", json!(c.bond_keep_weeks));
    put(m, "BondGroupKeepTimes", json!(c.bond_group_keep_times));
    put(m, "StateDebtShare", fx_str(c.state_debt_share));
    put(m, "StateBondInterest", fx_str(c.state_bond_interest));
    put(m, "StateBondWeeks", json!(c.state_bond_weeks));
    put(m, "CompanyDebtShare", fx_str(c.company_debt_share));
    put(m, "CompanyBondInterest", fx_str(c.company_bond_interest));
    put(m, "CompanyBondWeeks", json!(c.company_bond_weeks));
    put(m, "CompanyDefaultWeeks", json!(c.company_default_weeks));
    put(m, "WorkingWeeks", json!(c.working_weeks));
    put(
        m,
        "MerchantGenerationShare",
        fx_str(c.merchant_generation_share),
    );
    put(
        m,
        "MerchantMutationShare",
        fx_str(c.merchant_mutation_share),
    );
    put(
        m,
        "MerchantFitnessCapital",
        fx_str(c.merchant_fitness_capital),
    );
}

fn part16(c: &SimulationConfig, m: &mut Fields) {
    put(m, "PersonalitySpend", fx_arr(&c.personality_spend));
    put(m, "PersonalityMinMargin", fx_arr(&c.personality_min_margin));
    put(m, "PersonalityRange", level_arr(&c.personality_range));
    put(m, "OccupationCapacity", fx_arr(&c.occupation_capacity));
    put(m, "OccupationUpkeep", fx_arr(&c.occupation_upkeep));
    put(
        m,
        "OccupationDistancePower",
        json!(c.occupation_distance_power),
    );
    put(m, "DividendBufferWeeks", json!(c.dividend_buffer_weeks));
    put(m, "DividendShare", fx_str(c.dividend_share));
    put(m, "GroupPayoutShare", fx_str(c.group_payout_share));
    put(m, "LostWarehouseWeeks", json!(c.lost_warehouse_weeks));
    put(m, "PriceAdjustRate", fx_str(c.price_adjust_rate));
    put(m, "PriceMaxStep", fx_str(c.price_max_step));
    put(m, "PriceAnchorRate", fx_str(c.price_anchor_rate));
    put(m, "StockPressure", fx_str(c.stock_pressure));
    put(m, "MinPrice", json!(c.min_price));
    put(m, "MintRatePerYear", fx_str(c.mint_rate_per_year));
    put(m, "InitialGoldReserve", long_str(c.initial_gold_reserve));
    put(
        m,
        "InitialSilverReserve",
        long_str(c.initial_silver_reserve),
    );
    put(m, "InitialCoinSupply", long_str(c.initial_coin_supply));
    put(m, "CoinsPerGoldUnit", long_str(c.coins_per_gold_unit));
}

fn part17(c: &SimulationConfig, m: &mut Fields) {
    put(m, "CoinsPerSilverUnit", long_str(c.coins_per_silver_unit));
    put(
        m,
        "CurrencyEndowmentSpreadPermille",
        json!(c.currency_endowment_spread_permille),
    );
    put(m, "GuidanceDecayPerTurn", fx_str(c.guidance_decay_per_turn));
    put(
        m,
        "GuidanceWeightBuyFacility",
        fx_str(c.guidance_weight_buy_facility),
    );
    put(
        m,
        "GuidanceWeightFoundCompany",
        fx_str(c.guidance_weight_found_company),
    );
    put(
        m,
        "GuidanceWeightBuyLicense",
        fx_str(c.guidance_weight_buy_license),
    );
    put(m, "LodFocusLevel", json!(c.lod_focus_level.name()));
    put(m, "PlayerStartNode", json!(c.player_start_node));
    put(m, "PlayerStartCash", long_str(c.player_start_cash));
    put(m, "CartMaxWeight", long_str(c.cart_max_weight));
    put(m, "CartMaxSize", long_str(c.cart_max_size));
    put(m, "WarehouseMaxWeight", long_str(c.warehouse_max_weight));
    put(m, "WarehouseMaxSize", long_str(c.warehouse_max_size));
}

impl SimulationConfig {
    /// Public fields in C# declaration order, then `EffectiveValues`, as `ParityJson` writes them.
    pub fn to_parity_json(&self) -> Value {
        let mut m = Fields::new();
        part0(self, &mut m);
        part1(self, &mut m);
        part2(self, &mut m);
        part3(self, &mut m);
        part4(self, &mut m);
        part5(self, &mut m);
        part6(self, &mut m);
        part7(self, &mut m);
        part8(self, &mut m);
        part9(self, &mut m);
        part10(self, &mut m);
        part11(self, &mut m);
        part12(self, &mut m);
        part13(self, &mut m);
        part14(self, &mut m);
        part15(self, &mut m);
        part16(self, &mut m);
        part17(self, &mut m);
        let effective: Fields = self
            .effective
            .iter()
            .map(|(k, v)| (k.clone(), Value::String(v.clone())))
            .collect();
        put(&mut m, "EffectiveValues", Value::Object(effective));
        Value::Object(m)
    }
}
