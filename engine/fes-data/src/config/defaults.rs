//! Code defaults, copied from the C# field initializers.

use super::{SimulationConfig, TierCadence, WorldLevel};
use fes_core::Fx;

fn part0(c: &mut SimulationConfig) {
    c.tier_cadences = [
        TierCadence {
            period: 4,
            offset: 0,
        },
        TierCadence {
            period: 4,
            offset: 1,
        },
        TierCadence {
            period: 2,
            offset: 0,
        },
        TierCadence {
            period: 1,
            offset: 0,
        },
        TierCadence {
            period: 1,
            offset: 0,
        },
    ];
    c.grade_price_multiplier = [
        Fx::from_permille(600),
        Fx::ONE,
        Fx::from_permille(1800),
        Fx::from_permille(3500),
    ];
    c.level_demand_weight = [
        Fx::from_int(40),
        Fx::from_int(20),
        Fx::from_int(8),
        Fx::from_int(4),
        Fx::ONE,
    ];
    c.necessity_elasticity = Fx::from_permille(500);
    c.necessity_choke = Fx::from_int(5);
    c.luxury_elasticity = Fx::from_permille(1500);
    c.grade_luxury_share = [Fx::ZERO, Fx::ZERO, Fx::from_permille(500), Fx::ONE];
    c.semi_grade_luxury_share = [
        Fx::from_permille(250),
        Fx::from_permille(650),
        Fx::from_permille(900),
        Fx::ONE,
    ];
    c.max_cheap_demand = Fx::from_int(2);
    c.quintile_income_share = [
        Fx::from_permille(20),
        Fx::from_permille(30),
        Fx::from_permille(50),
        Fx::from_permille(100),
        Fx::from_permille(800),
    ];
    c.default_population = [20000, 10000, 4000, 2000, 500];
    c.default_income_per_head = [
        Fx::from_permille(300),
        Fx::from_permille(300),
        Fx::from_permille(300),
        Fx::from_permille(500),
        Fx::from_permille(2000),
    ];
    c.population_base_growth = Fx::from_permille(8);
    c.population_growth_response = Fx::from_permille(40);
    c.population_max_growth = Fx::from_permille(13);
    c.population_max_decline = Fx::from_permille(40);
    c.income_response = Fx::from_permille(500);
    c.income_max_change = Fx::from_permille(500);
    c.need_memory_weeks = 52;
    c.household_budget_weeks = 26;
}

fn part1(c: &mut SimulationConfig) {
    c.household_alms_share = Fx::from_permille(20);
    c.luxury_wealth_per_year = Fx::from_permille(1000);
    c.luxury_wealth_cap = Fx::from_int(4);
    c.luxury_tax = Fx::from_permille(100);
    c.trader_float_share = Fx::from_permille(100);
    c.trader_loan_share = Fx::ZERO;
    c.trader_loan_keep_weeks = 26;
    c.trader_bank_loan_share = Fx::ZERO;
    c.trader_loan_interest = Fx::from_permille(50);
    c.trader_loan_weeks = 52;
    c.trader_loan_repay_share = Fx::from_permille(250);
    c.producer_float_share = Fx::from_permille(500);
    c.level_wage_weight = [
        Fx::from_permille(3000),
        Fx::from_permille(3000),
        Fx::from_permille(2500),
        Fx::from_permille(2000),
        Fx::ONE,
    ];
    c.noble_royal_share = Fx::from_permille(100);
    c.noble_city_share = Fx::from_permille(100);
    c.noble_village_share = Fx::from_permille(100);
    c.noble_aid_share = Fx::from_permille(200);
    c.noble_protection_share = Fx::from_permille(500);
    c.noble_spend_share = Fx::from_permille(20);
    c.village_wage_away_share = Fx::from_permille(500);
}

fn part2(c: &mut SimulationConfig) {
    c.migration_max_share = Fx::ZERO;
    c.facility_expansion_bonus = Fx::ZERO;
    c.facility_output_wage = Fx::ZERO;
    c.state_public_wage_share = Fx::ZERO;
    c.facility_max_level = 5;
    c.facility_unaided_output = Fx::from_permille(700);
    c.investment_payback_years = 0;
    c.investment_max_per_node = 24;
    c.investment_projects_per_node = 2;
    c.migration_start_free_share = Fx::from_permille(200);
    c.migration_flee_share = Fx::from_permille(10);
    c.migration_broken_contract_factor = 3;
    c.migration_jobs_per_facility_level = 100000;
    c.migration_emancipation_earliest_years = 0;
    c.migration_vacancy_share = Fx::from_permille(200);
    c.migration_emancipation_lead_years = 3;
    c.migration_freed_per_year = Fx::from_permille(100);
    c.migration_redemption_years = 49;
    c.migration_redemption_share = Fx::from_permille(50);
    c.population_urban_penalty = Fx::ZERO;
}

fn part3(c: &mut SimulationConfig) {
    c.labor_base_employment = Fx::from_permille(900);
    c.labor_min_employment = Fx::from_permille(500);
    c.labor_max_employment = Fx::from_permille(1000);
    c.labor_response = Fx::from_permille(500);
    c.labor_long_run_response = Fx::from_permille(125);
    c.labor_merchant_wage = Fx::from_permille(100);
    c.labor_merchant_buffer = Fx::from_permille(1000);
    c.tax_household = Fx::from_permille(100);
    c.tax_company = Fx::from_permille(100);
    c.defense_share = Fx::from_permille(50);
    c.defense_maintenance = Fx::from_permille(200);
    c.defense_interval_weeks = 4;
    c.defense_safety_cost_per_route = 15_000;
    c.defense_max_safety = Fx::from_permille(600);
    c.subsidy_share = Fx::from_permille(300);
    c.subsidy_rate = Fx::from_permille(200);
    c.subsidy_years = 3;
    c.subsidy_max_per_nation = 2;
    c.state_project_share = Fx::from_permille(500);
    c.state_projects_per_year = 3;
}

fn part4(c: &mut SimulationConfig) {
    c.state_project_weeks = 16;
    c.state_trade_score = 100;
    c.state_road_score = 120;
    c.state_farmland_cost = 5_000;
    c.state_mine_cost = 8_000;
    c.state_harbor_cost = 10_000;
    c.state_highway_cost_per_turn = 1_000;
    c.state_land_bonus = Fx::from_permille(100);
    c.state_land_max = 5;
    c.state_harbor_bonus = Fx::from_permille(100);
    c.state_harbor_max = 3;
    c.state_highway_bonus = Fx::from_permille(200);
    c.state_facility_levy = Fx::from_permille(500);
    c.policy_step = Fx::from_permille(100);
    c.policy_max_steps = 5;
    c.policy_move_threshold = Fx::from_permille(100);
    c.policy_resistance = Fx::ONE;
    c.influence_asset_weight = Fx::ONE;
    c.influence_lobby_weight = Fx::from_int(3);
    c.influence_per_job = 50;
}

fn part5(c: &mut SimulationConfig) {
    c.jobs_per_facility_level = 20;
    c.jobs_per_merchant_group = 10;
    c.npc_lobby_share = Fx::from_permille(50);
    c.policy_loss_tilt = Fx::from_int(2);
    c.inflation_opposition = Fx::from_permille(100);
    c.merchants_per_node = [50, 50, 30, 10, 3];
    c.merchant_class_cash = [9500, 1800, 100];
    c.merchant_class_share_permille = [100, 300, 400];
    c.merchant_vehicle_capacity = [Fx::ONE, Fx::ONE, Fx::ONE];
    c.merchant_class_vehicle = [0, 0, 0, 0];
    c.merchant_vehicle_cash_times = [0, 3, 3];
    c.merchant_vehicle_cost = [Fx::ONE, Fx::ONE, Fx::ONE];
    c.merchant_vehicle_danger = [Fx::ONE, Fx::ONE, Fx::ONE];
    c.merchant_vehicle_premium = [Fx::ONE, Fx::ONE, Fx::ONE];
    c.order_lead_weeks = 8;
    c.food_grade_price = [Fx::ZERO, Fx::ZERO, Fx::ZERO, Fx::ZERO];
    c.food_grade_demand = [Fx::ZERO, Fx::ZERO, Fx::ZERO, Fx::ZERO];
    c.contracts_per_nation = 0;
    c.contract_min_stock = 200;
    c.contract_stock_weeks = 26;
}

fn part6(c: &mut SimulationConfig) {
    c.contract_demand_share = Fx::from_permille(500);
    c.contract_credit_weeks = 8;
    c.contract_min_weeks = 8;
    c.contract_max_weeks = 104;
    c.contract_deposit_share = Fx::from_permille(200);
    c.contract_base_discount = Fx::from_permille(50);
    c.contract_discount_per_unit = Fx::from_permille(1);
    c.contract_max_discount = Fx::from_permille(250);
    c.strategic_subsidy_share = Fx::ZERO;
    c.merchant_join_share = Fx::ZERO;
    c.merchant_member_fee = Fx::from_permille(50);
    c.merchant_support_floor = Fx::from_permille(200);
    c.merchant_support_share = Fx::from_permille(100);
    c.merchant_member_premium = Fx::from_permille(500);
    c.lod_mode = 1;
    c.core_absorb = true;
    c.facility_close_loss_years = 2;
    c.core_weight = [3, 2, 1];
    c.core_stacking = false;
    c.core_demand_weighted = true;
}

fn part7(c: &mut SimulationConfig) {
    c.core_wage_per_point = Fx::from_permille(100);
    c.core_demand_per_point = Fx::from_permille(50);
    c.merchant_risk_aversion = 1;
    c.personality_loss_aversion = [0; 3];
    c.merchant_count_trip_costs = false;
    c.merchant_must_act = true;
    c.hedge_floor = Fx::from_permille(850);
    c.hedge_fee = Fx::ZERO;
    c.hedge_fall = Fx::from_permille(150);
    c.hedge_fall_chance = Fx::from_permille(300);
    c.hedge_bank_share = Fx::from_permille(100);
    c.order_expiry_weeks = 52;
    c.forecast_enabled = false;
    c.forecast_min_plan = Fx::from_permille(100);
    c.forecast_household_weight = Fx::ONE;
    c.forecast_cover = Fx::ONE;
    c.forecast_primary = false;
    c.investment_min_plan = Fx::ZERO;
    c.merchant_vehicle_wear = Fx::from_permille(100);
    c.merchant_max_weight = [4000, 4000, 2000, 1000, 400];
}

fn part8(c: &mut SimulationConfig) {
    c.merchant_max_size = [3200, 3200, 1600, 800, 300];
    c.merchant_upkeep = [50, 40, 30, 20, 10];
    c.merchant_bankrupt_weeks = 5;
    c.merchant_reserve_upkeep_weeks = 2;
    c.merchant_sell_to_households = true;
    c.merchant_flow_share = Fx::from_permille(500);
    c.production_variance_permille = 100;
    c.facility_start_cash = [500, 2000, 1000];
    c.specialty_output_multiplier = Fx::from_permille(1250);
    c.specialty_grade_up_chance = Fx::from_permille(200);
    c.specialty_price_multiplier = Fx::from_permille(800);
    c.terrain_extra_turns = [0, 1, 0, 0];
    c.terrain_cost_multiplier = [
        Fx::ONE,
        Fx::from_permille(1500),
        Fx::from_permille(1200),
        Fx::from_permille(1500),
    ];
    c.terrain_danger = [
        Fx::from_permille(50),
        Fx::from_permille(100),
        Fx::from_permille(60),
        Fx::from_permille(80),
    ];
    c.region_danger = Fx::ONE;
    c.danger_value_scale = 2000;
    c.encounter_cash_loss = Fx::from_permille(300);
    c.encounter_cargo_loss = Fx::from_permille(300);
    c.guard_base_cost = 10;
}

fn part9(c: &mut SimulationConfig) {
    c.guard_cost_share = Fx::from_permille(20);
    c.guard_reduction = Fx::from_permille(700);
    c.insurance_premium = Fx::from_permille(30);
    c.insurance_refund = Fx::from_permille(800);
    c.passage_base_fee = 20;
    c.passage_per_unit = 2;
    c.ship_price = 5000;
    c.ship_max_weight = 3000;
    c.ship_max_size = 2400;
    c.ship_resale_share = Fx::from_permille(700);
    c.road_cost_reduction = Fx::from_permille(400);
    c.road_danger_reduction = Fx::from_permille(500);
    c.road_upkeep = 5;
    c.road_decay_permille = 5;
    c.road_build_cost_per_turn = 300;
    c.road_build_weeks = 8;
    c.group_start_cash = [20000, 2000, 50000, 1000];
    c.companies_per_nation = 2;
    c.guild_fee = 200;
    c.guild_insurance_discount = Fx::from_permille(300);
}

fn part10(c: &mut SimulationConfig) {
    c.guild_license_discount = Fx::from_permille(300);
    c.guild_hall_max_weight = 1000;
    c.guild_hall_max_size = 800;
    c.bank_initial_reserve_share = Fx::from_permille(100);
    c.deposit_interest_per_year = Fx::from_permille(20);
    c.exchange_fee = Fx::from_permille(30);
    c.loan_interest_per_year = Fx::from_permille(100);
    c.loan_term_weeks = 52;
    c.loan_to_value = Fx::from_permille(500);
    c.loan_grace_weeks = 4;
    c.margin_call_share = Fx::from_permille(750);
    c.margin_call_weeks = 2;
    c.auction_weeks = 2;
    c.auction_reserve_share = Fx::from_permille(800);
    c.facility_value = [3000, 8000, 5000];
    c.facility_repurpose_share = Fx::from_permille(500);
    c.land_plots = [10, 10, 10, 40, 12];
    c.facility_shares = 100;
    c.facility_max_listed_shares = 49;
    c.land_value = [2000, 2000, 2000, 1500, 500];
}

fn part11(c: &mut SimulationConfig) {
    c.international_bank_reserve_share = Fx::from_permille(20);
    c.target_reserve_ratio = Fx::from_permille(500);
    c.max_loan_rate_multiplier = Fx::from_int(3);
    c.state_reserve_target = Fx::from_int(2);
    c.state_spending_share = Fx::from_permille(20);
    c.npc_loan_term_weeks = 26;
    c.npc_loan_buffer_weeks = 8;
    c.npc_loan_max_reserve_share = Fx::from_permille(100);
    c.finance_history_quarters = 20;
    c.financial_event_chance_permille = 20;
    c.financial_event_end_reserve_ratio = Fx::from_permille(500);
    c.bank_run_drain = Fx::from_permille(200);
    c.bank_run_withdraw_share = Fx::from_permille(100);
    c.market_credit_share = Fx::from_permille(5);
    c.market_credit_weight = Fx::from_permille(250);
    c.market_momentum = Fx::from_permille(900);
    c.market_reversion = Fx::from_permille(300);
    c.market_weekly_move = Fx::from_permille(30);
    c.market_premium_floor = Fx::from_permille(200);
    c.market_premium_cap = Fx::from_int(10);
}

fn part12(c: &mut SimulationConfig) {
    c.market_idle_weight = Fx::from_permille(400);
    c.market_property_idle_weight = Fx::from_permille(20);
    c.market_venture_idle_weight = Fx::from_permille(30);
    c.market_repay_share = Fx::from_permille(200);
    c.market_crunch_share = Fx::from_permille(100);
    c.market_heated_premium = Fx::from_permille(1500);
    c.market_burst_premium = Fx::from_permille(2500);
    c.market_leverage_limit = Fx::from_permille(600);
    c.market_burst_drop = Fx::from_permille(300);
    c.ventures_per_continent = 3;
    c.venture_shares = 100;
    c.venture_capital = 20000;
    c.venture_voyage_weeks = 26;
    c.venture_loss_permille = 100;
    c.venture_min_margin = Fx::from_permille(200);
    c.venture_value_years = 2;
    c.short_licence_fee = 1000;
    c.short_licence_deposit = 5000;
    c.short_licence_weeks = 52;
    c.short_open_share = Fx::from_permille(1500);
}

fn part13(c: &mut SimulationConfig) {
    c.short_call_share = Fx::from_permille(1300);
    c.short_fee = Fx::from_permille(50);
    c.short_max_lent_shares = 20;
    c.state_max_per_node = 24;
    c.land_rent_ppm = 30;
    c.land_rent_years = 10;
    c.crunch_lifts_rates = true;
    c.group_list_premium = Fx::from_permille(1300);
    c.group_buyback_premium = Fx::from_permille(800);
    c.bank_run_cover = Fx::ZERO;
    c.rate_spike_multiplier = Fx::from_int(2);
    c.license_price_multiplier = Fx::from_int(20);
    c.licensed_merchant_permille = 200;
    c.international_merchant_permille = 500;
    c.intercontinental_merchant_permille = 500;
    c.international_license_price = 300;
    c.intercontinental_license_price = 1500;
    c.toll_rate = Fx::from_permille(30);
    c.toll_specialty_surcharge = Fx::from_permille(70);
    c.facility_upkeep = [50, 100, 50];
}

fn part14(c: &mut SimulationConfig) {
    c.facility_bankrupt_weeks = 5;
    c.merchant_traits_enabled = true;
    c.merchant_rumors_enabled = false;
    c.merchant_weigh_sales = true;
    c.merchant_judge_hops = 1;
    c.merchant_bids = false;
    c.merchant_bids_while_loaded = false;
    c.merchant_return_home = false;
    c.merchant_judge_coin_units = 0;
    c.merchant_port_crossers = false;
    c.international_balance = false;
    c.trader_spread = Fx::ZERO;
    c.trader_float_keep_weeks = 0;
    c.trader_float_payout = Fx::from_permille(20);
    c.merchant_flow_wage = Fx::ZERO;
    c.merchant_cross_level = -1;
    c.merchant_cross_flow = Fx::ZERO;
    c.continental_producers = 3;
    c.bill_cap_weeks = 0;
    c.bill_weeks = 8;
}

fn part15(c: &mut SimulationConfig) {
    c.bill_discount = Fx::from_permille(20);
    c.bill_repay_share = Fx::from_permille(250);
    c.bill_default_weeks = 26;
    c.bill_bank_floor = Fx::from_permille(500);
    c.bill_sale_discount = Fx::from_permille(150);
    c.bill_producers = false;
    c.bond_buy_share = Fx::from_permille(5);
    c.bond_keep_weeks = 26;
    c.bond_group_keep_times = 2;
    c.state_debt_share = Fx::ZERO;
    c.state_bond_interest = Fx::from_permille(40);
    c.state_bond_weeks = 1040;
    c.company_debt_share = Fx::ZERO;
    c.company_bond_interest = Fx::from_permille(70);
    c.company_bond_weeks = 520;
    c.company_default_weeks = 26;
    c.working_weeks = 0;
    c.merchant_generation_share = Fx::from_permille(50);
    c.merchant_mutation_share = Fx::from_permille(150);
    c.merchant_fitness_capital = Fx::from_permille(700);
}

fn part16(c: &mut SimulationConfig) {
    c.personality_spend = [Fx::from_permille(500), Fx::from_permille(750), Fx::ONE];
    c.personality_min_margin = [Fx::from_permille(200), Fx::from_permille(100), Fx::ZERO];
    c.personality_range = [
        WorldLevel::Region,
        WorldLevel::Nation,
        WorldLevel::Continent,
    ];
    c.occupation_capacity = [Fx::ONE, Fx::ONE, Fx::from_permille(500)];
    c.occupation_upkeep = [Fx::ONE, Fx::ONE, Fx::from_permille(500)];
    c.occupation_distance_power = [2, 0, 1];
    c.dividend_buffer_weeks = 26;
    c.dividend_share = Fx::from_permille(100);
    c.group_payout_share = Fx::from_permille(800);
    c.lost_warehouse_weeks = 8;
    c.price_adjust_rate = Fx::from_permille(250);
    c.price_max_step = Fx::from_permille(150);
    c.price_anchor_rate = Fx::from_permille(20);
    c.stock_pressure = Fx::from_permille(250);
    c.min_price = 1;
    c.mint_rate_per_year = Fx::from_permille(30);
    c.initial_gold_reserve = 1_000_000;
    c.initial_silver_reserve = 10_000_000;
    c.initial_coin_supply = 5_000_000;
    c.coins_per_gold_unit = 10;
}

fn part17(c: &mut SimulationConfig) {
    c.coins_per_silver_unit = 1;
    c.currency_endowment_spread_permille = 300;
    c.guidance_decay_per_turn = Fx::from_permille(10);
    c.guidance_weight_buy_facility = Fx::ONE;
    c.guidance_weight_found_company = Fx::from_int(5);
    c.guidance_weight_buy_license = Fx::from_int(2);
    c.lod_focus_level = WorldLevel::Region;
    c.player_start_node = String::new();
    c.player_start_cash = 500;
    c.cart_max_weight = 400;
    c.cart_max_size = 300;
    c.warehouse_max_weight = 2000;
    c.warehouse_max_size = 1500;
}

impl SimulationConfig {
    /// Every field at its code default, before any file value is applied.
    pub(super) fn code_defaults() -> Self {
        let mut c = Self::zeroed();
        part0(&mut c);
        part1(&mut c);
        part2(&mut c);
        part3(&mut c);
        part4(&mut c);
        part5(&mut c);
        part6(&mut c);
        part7(&mut c);
        part8(&mut c);
        part9(&mut c);
        part10(&mut c);
        part11(&mut c);
        part12(&mut c);
        part13(&mut c);
        part14(&mut c);
        part15(&mut c);
        part16(&mut c);
        part17(&mut c);
        c.producers_per_node[4][0] = 4;
        c.producers_per_node[4][2] = 2;
        c.producers_per_node[3][1] = 4;
        c
    }
}
