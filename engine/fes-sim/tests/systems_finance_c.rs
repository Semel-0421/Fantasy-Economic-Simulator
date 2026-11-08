//! Finance group C against the recorded C# turns, and the C# unit tests ported
//! (`InvestmentTests` orders, contracts and associations, `ForecastTests`, `CreditTests`,
//! `TraderLoanTests`, `PolicyTests`, `MerchantTests` generations).
mod common;

use common::{build, build_sample, sample_config};
use fes_core::testing::fixtures_dir;
use fes_core::Fx;
use fes_core::QualityGrade;
use fes_core::WorldLevel;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::bank_seizure::trader_offer;
use fes_sim::agents::contract_system::{ContractRoute, ContractSystem};
use fes_sim::agents::demand_forecast::DemandForecastSystem;
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::groups::{city_of, KIND_COMPANY, KIND_GUILD};
use fes_sim::agents::merchant_membership::MerchantMembershipSystem;
use fes_sim::agents::merchant_vehicles::{CART, PACK};
use fes_sim::agents::order_system::MerchantVehicleBought;
use fes_sim::agents::order_system::QUARTER_WEEKS;
use fes_sim::agents::production::lines::find_nearest_market;
use fes_sim::agents::strategic_subsidy::StrategicSubsidySystem;
use fes_sim::agents::{AgentTable, OrderTable};
use fes_sim::agents::{BankState, ContractTable, GroupTable, PolicyState};
use fes_sim::agents::{DemandForecast, FacilityTable, PurseState};
use fes_sim::economy::HouseholdState;
use fes_sim::economy::MarketStore;
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::NodeTable;
use fes_sim::System;
use fes_sim::{SimEvent, Simulation};
use std::sync::Arc;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: &[&str] = &[
    "agents.orders",
    "agents.demand_forecast",
    "agents.merchant_membership",
];
/// Earlier askers of this turn's bond offers (C# `BondOffers`) run in Rust too, so credit sees
/// the offers they left (see `plan4-audit.md`).
const OFFER_USERS: &[&str] = &[
    "agents.ventures",
    "agents.asset_markets",
    "agents.investment",
];
const WEEKS_PER_YEAR: i32 = 52;

#[test]
fn finance_c_matches_on_every_sampled_turn() {
    let source = FileDataSource::new(common::data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let rust: Vec<&str> = OFFER_USERS.iter().chain(NEW).copied().collect();
        let report = replay_turn(&step, &db, &map, &rust).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        for id in NEW {
            let label = format!("system:{id}");
            assert!(
                report
                    .steps
                    .iter()
                    .any(|s| matches!(s, StepResult::Equal { label: l } if *l == label)),
                "turn {t}: {id} did not run in Rust"
            );
        }
    }
}

fn of<E: Clone + 'static>(events: &[Arc<dyn SimEvent>]) -> Vec<E> {
    events
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<E>().cloned())
        .collect()
}

fn step(sim: &mut Simulation) -> Vec<Arc<dyn SimEvent>> {
    sim.step().expect("step");
    sim.context().events.turn_log().to_vec()
}

// ---- orders (InvestmentTests) ----

#[test]
fn merchants_buy_better_vehicles_from_the_market() {
    let mut sim = build_sample(&[]);
    let mut bought = 0;
    for _ in 0..3 * WEEKS_PER_YEAR {
        bought += of::<MerchantVehicleBought>(&step(&mut sim)).len();
    }
    assert!(bought > 0);
    let agents = sim.context().world.get::<AgentTable>();
    let mut kinds = agents.vehicle.clone();
    kinds.sort_unstable();
    kinds.dedup();
    assert!(kinds.len() > 1, "merchants differ in what they carry with");
}

#[test]
fn order_reserves_stock_then_is_collected_after_the_lead_time() {
    let mut sim = build_sample(&[]);
    let db = sim.context().services.database.clone();
    let cart = db.try_get_good("cart", QualityGrade::Mid).expect("cart").0;
    let lead = db.config.order_lead_weeks;
    let (home, cell) = {
        let world = &sim.context().world;
        let nodes = sim.context().services.get::<NodeTable>();
        let markets = world.get::<MarketStore>();
        let home = world.get::<AgentTable>().home[0];
        let mut m = home;
        while markets.try_find_cell(m as usize, cart).is_none() {
            m = nodes.parent[m as usize];
        }
        (home, markets.try_find_cell(m as usize, cart).unwrap())
    };
    let world = &mut sim.context_mut().world;
    world.get_mut::<MarketStore>().stock[cell] = 5;
    let agents = world.get_mut::<AgentTable>();
    agents.cash[0] = 1_000_000;
    agents.vehicle[0] = PACK;
    world
        .get_mut::<OrderTable>()
        .add(0, home, cell as i32, 1, 0, CART);
    step(&mut sim);
    assert_eq!(sim.context().world.get::<AgentTable>().vehicle[0], PACK);
    for _ in 0..=lead {
        step(&mut sim);
    }
    assert_eq!(sim.context().world.get::<AgentTable>().vehicle[0], CART);
}

// ---- demand forecast (ForecastTests) ----

/// A quarter's planning on empty markets where nothing has lately sold.
fn at_quarter(lines: &[&str]) -> Simulation {
    let mut sim = build_sample(lines);
    loop {
        step(&mut sim);
        if sim.context().turn() % QUARTER_WEEKS == 0 {
            break;
        }
    }
    let world = &mut sim.context_mut().world;
    world.get_mut::<MarketStore>().stock.fill(0);
    let forecast = world.get_mut::<DemandForecast>();
    forecast.forecast.fill(0);
    forecast.stock_at_start.fill(0);
    forecast.produced.fill(0);
    sim
}

fn plan(sim: &mut Simulation) {
    DemandForecastSystem::default()
        .execute(sim.context_mut())
        .unwrap();
}

fn config_mut(sim: &mut Simulation) -> &mut fes_data::SimulationConfig {
    &mut Arc::make_mut(&mut sim.context_mut().services.database).config
}

/// The cells the NPC facilities of a kind read their plans at, one per market.
fn plan_cells(sim: &Simulation, facility: &str) -> Vec<usize> {
    let ctx = sim.context();
    let db = &ctx.services.database;
    let nodes = ctx.services.get::<NodeTable>();
    let markets = ctx.world.get::<MarketStore>();
    let facilities = ctx.world.get::<FacilityTable>();
    let mut cells = Vec::new();
    for f in 0..facilities.count() {
        let def = &db.facilities[facilities.def_index[f] as usize];
        if def.key != facility || facilities.owner[f] != OWNER_NPC {
            continue;
        }
        let good = db.recipes[def.recipe].outputs[0].good.0;
        let (_, cell) = find_nearest_market(nodes, markets, facilities.node[f], good).unwrap();
        if !cells.contains(&cell) {
            cells.push(cell);
        }
    }
    assert!(!cells.is_empty(), "no {facility}");
    cells
}

fn plan_of(sim: &Simulation, facility: &str) -> i64 {
    sim.context().world.get::<DemandForecast>().plan[plan_cells(sim, facility)[0]]
}

fn min_plan(sim: &Simulation) -> i64 {
    sim.context()
        .services
        .database
        .config
        .forecast_min_plan
        .raw()
}

fn stock_all(sim: &mut Simulation, good: &str, grade: QualityGrade, quantity: i32) {
    let id = sim
        .context()
        .services
        .database
        .try_get_good(good, grade)
        .unwrap()
        .0;
    let markets = sim.context_mut().world.get_mut::<MarketStore>();
    for c in 0..markets.cell_count() {
        if markets.cell_good[c] == id {
            markets.stock[c] = quantity;
        }
    }
}

#[test]
fn plan_follows_what_households_want_though_nothing_sold() {
    let mut sim = at_quarter(&[]);
    plan(&mut sim);
    assert!(plan_of(&sim, "bakery") > min_plan(&sim));
    config_mut(&mut sim).forecast_household_weight = Fx::ZERO;
    plan(&mut sim);
    assert_eq!(plan_of(&sim, "bakery"), min_plan(&sim));
}

#[test]
fn plan_carries_demand_up_the_chain_to_raw_goods() {
    let mut sim = at_quarter(&[]);
    plan(&mut sim);
    for kind in ["mill", "wheat_farm", "barley_farm", "iron_mine"] {
        assert!(plan_of(&sim, kind) > min_plan(&sim), "{kind}");
    }
}

#[test]
fn plan_stock_at_the_market_cuts_it_to_the_least() {
    let mut sim = at_quarter(&[]);
    stock_all(&mut sim, "wheat", QualityGrade::Mid, 10_000_000);
    plan(&mut sim);
    assert_eq!(plan_of(&sim, "wheat_farm"), min_plan(&sim));
    assert!(plan_of(&sim, "mill") > min_plan(&sim));
}

#[test]
fn plan_stock_elsewhere_does_not_stop_makers_serving_their_own_market() {
    let mut sim = at_quarter(&["production.forecast_cover_permille,0"]);
    let cells = plan_cells(&sim, "dairy");
    assert!(cells.len() > 1);
    plan(&mut sim);
    let before = sim.context().world.get::<DemandForecast>().plan[cells[1]];
    let glut = cells[0];
    let db = sim.context().services.database.clone();
    let markets = sim.context_mut().world.get_mut::<MarketStore>();
    let node = (0..markets.market_count())
        .find(|&n| {
            let start = markets.market_cell_start[n] as usize;
            glut >= start && glut < start + markets.market_cell_count[n] as usize
        })
        .unwrap();
    for grade in [
        QualityGrade::Low,
        QualityGrade::Mid,
        QualityGrade::High,
        QualityGrade::Best,
    ] {
        let cheese = db.try_get_good("cheese", grade).unwrap().0;
        if let Some(c) = markets.try_find_cell(node, cheese) {
            markets.stock[c] = 10_000_000;
        }
    }
    plan(&mut sim);
    let p = &sim.context().world.get::<DemandForecast>().plan;
    assert!(p[glut] < p[cells[1]]);
    assert!(p[cells[1]] > min_plan(&sim));
    assert!(p[cells[1]] <= before);
}

#[test]
fn plan_demand_of_markets_without_makers_falls_to_makers_with_capacity_to_spare() {
    let mut sim = at_quarter(&["production.forecast_cover_permille,0"]);
    plan(&mut sim);
    let before = plan_of(&sim, "dairy");
    assert!(before < Fx::ONE_RAW);
    let seats: Vec<usize> = {
        let nodes = sim.context().services.get::<NodeTable>();
        (0..nodes.count())
            .filter(|&n| nodes.is_seat(n as i32))
            .collect()
    };
    let population = &mut sim
        .context_mut()
        .world
        .get_mut::<HouseholdState>()
        .population;
    for n in seats {
        population[n] *= 20;
    }
    plan(&mut sim);
    assert!(plan_of(&sim, "dairy") > before);
}

#[test]
fn plan_leaves_primary_producers_running_when_they_do_not_plan() {
    let mut sim = build_sample(&["production.forecast_primary,0"]);
    for _ in 0..=QUARTER_WEEKS {
        step(&mut sim);
    }
    assert_eq!(plan_of(&sim, "wheat_farm"), Fx::ONE_RAW);
}

#[test]
fn plan_adds_open_orders_from_anywhere_in_the_nation() {
    let mut sim = at_quarter(&[]);
    let db = sim.context().services.database.clone();
    let cart = db.try_get_good("cart", QualityGrade::Mid).unwrap().0;
    plan(&mut sim);
    assert_eq!(plan_of(&sim, "cartwright"), min_plan(&sim));
    let turn = sim.context().turn();
    let world = &mut sim.context_mut().world;
    let facilities = world.get::<FacilityTable>();
    let f = (0..facilities.count())
        .find(|&i| db.facilities[facilities.def_index[i] as usize].key == "cartwright")
        .unwrap();
    let purses = world.get::<PurseState>();
    let nation = purses.node_float[facilities.node[f] as usize] as usize;
    let markets = world.get::<MarketStore>();
    let order_node = *purses.nation_nodes[nation]
        .iter()
        .find(|&&n| markets.try_find_cell(n as usize, cart).is_some())
        .unwrap();
    let cell = markets.try_find_cell(order_node as usize, cart).unwrap();
    world
        .get_mut::<OrderTable>()
        .add(-1, order_node, cell as i32, 100_000, turn, 0);
    plan(&mut sim);
    assert_eq!(plan_of(&sim, "cartwright"), Fx::ONE_RAW);
}

// ---- merchant membership (InvestmentTests) ----

#[test]
fn association_helps_a_member_running_short() {
    let mut sim = build_sample(&[]);
    let world = &mut sim.context_mut().world;
    let groups = world.get_mut::<GroupTable>();
    let guild = (0..groups.count())
        .find(|&g| groups.kind[g] == KIND_GUILD)
        .unwrap();
    groups.cash[guild] = 100_000;
    let agents = world.get_mut::<AgentTable>();
    let a = (0..agents.count())
        .find(|&i| agents.owner[i] == OWNER_NPC)
        .unwrap();
    agents.association[a] = guild as i32;
    agents.cash[a] = 0;
    MerchantMembershipSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let world = &sim.context().world;
    let given = 100_000 - world.get::<GroupTable>().cash[guild];
    assert!(given > 0);
    assert_eq!(world.get::<AgentTable>().cash[a], given, "no coins made");
}

#[test]
fn every_group_joins_the_richest_open_guild_or_company_when_all_join() {
    let mut sim = build_sample(&["merchants.join_permille,1000"]);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    {
        let agents = sim.context_mut().world.get_mut::<AgentTable>();
        agents.association.fill(-1);
    }
    MerchantMembershipSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let ctx = sim.context();
    let nodes = ctx.services.get::<NodeTable>();
    let agents = ctx.world.get::<AgentTable>();
    let groups = ctx.world.get::<GroupTable>();
    for a in 0..agents.count() {
        if agents.owner[a] != OWNER_NPC {
            continue;
        }
        let home = agents.home[a];
        let city = city_of(nodes, home);
        let (kind, at) = if city >= 0 {
            (KIND_GUILD, city)
        } else {
            (KIND_COMPANY, nodes.ancestor_at(home, WorldLevel::Nation))
        };
        let open: Vec<usize> = (0..groups.count())
            .filter(|&g| groups.kind[g] == kind && groups.node[g] == at && groups.is_player[g] == 0)
            .collect();
        let expected = open.iter().copied().fold(-1i32, |best, g| {
            if best < 0 || groups.cash[g] > groups.cash[best as usize] {
                g as i32
            } else {
                best
            }
        });
        assert_eq!(agents.association[a], expected, "agent {a}");
    }
}

// ---- contracts (ContractRuleTests, InvestmentTests) ----

fn signed_year(lines: &[&str]) -> Simulation {
    let mut sim = build(&sample_config(lines), 1);
    for _ in 0..WEEKS_PER_YEAR {
        step(&mut sim);
    }
    // In the Rust sample run of seed 1 the groups owning facilities at the buying markets hold
    // no cash by the first new year, so they are given some to sign with.
    for c in &mut sim.context_mut().world.get_mut::<GroupTable>().cash {
        *c += 1_000_000;
    }
    step(&mut sim);
    assert!(sim.context().world.get::<ContractTable>().count() > 0);
    sim
}

#[test]
fn a_contract_runs_for_the_weeks_its_deposit_pays_for() {
    let sim = signed_year(&[]);
    let c = sim.context().world.get::<ContractTable>();
    let share = sim
        .context()
        .services
        .database
        .config
        .contract_deposit_share;
    let signed = sim.context().turn() - 1;
    for r in 0..c.count() {
        let weekly = i64::from(c.quantity[r]) * c.unit_price[r];
        let weeks = i64::from(c.end_turn[r] - signed - 1);
        assert_eq!(c.prepaid[r], share.mul_int(weekly * weeks), "row {r}");
    }
}

#[test]
fn an_unlicensed_merchant_signs_for_no_licensed_good() {
    let mut sim = build(&sample_config(&["license.merchant_share_permille,0"]), 1);
    for _ in 0..WEEKS_PER_YEAR {
        step(&mut sim);
    }
    for c in &mut sim.context_mut().world.get_mut::<GroupTable>().cash {
        *c += 1_000_000;
    }
    sim.context_mut()
        .world
        .get_mut::<PolicyState>()
        .licensed
        .fill(1);
    step(&mut sim);
    let c = sim.context().world.get::<ContractTable>();
    for r in 0..c.count() {
        assert!(c.seller[r] >= 0 && c.buyer[r] >= 0, "row {r}");
    }
}

#[test]
fn contract_deposit_is_drawn_down_and_the_rest_returns_at_the_end() {
    let mut sim = build_sample(&[]);
    let db = sim.context().services.database.clone();
    let wheat = db.try_get_good("wheat", QualityGrade::Mid).unwrap().0;
    let nodes = sim.context().services.get::<NodeTable>();
    let from = (0..nodes.count())
        .find(|&n| nodes.level_of(n as i32) == WorldLevel::Village)
        .unwrap();
    let to = nodes.parent[from];
    let world = &mut sim.context_mut().world;
    let markets = world.get_mut::<MarketStore>();
    let cell = markets.try_find_cell(from, wheat).unwrap();
    let to_cell = markets.try_find_cell(to as usize, wheat).unwrap();
    markets.stock[cell] = 0;
    let groups = world.get_mut::<GroupTable>();
    groups.cash[0] = 5000;
    let buyer = groups.cash[1];
    let route = ContractRoute {
        from_cell: cell as i32,
        to_cell: to_cell as i32,
        from_node: from as i32,
        to_node: to,
    };
    world
        .get_mut::<ContractTable>()
        .add(0, 1, route, 1, 10, 0, 500);
    step(&mut sim);
    assert_eq!(sim.context().world.get::<ContractTable>().count(), 0);
    assert!(sim.context().world.get::<GroupTable>().cash[1] >= buyer + 500 - 1000);
}

#[test]
fn contract_delivers_at_its_price() {
    let mut sim = build_sample(&[]);
    let db = sim.context().services.database.clone();
    let wheat = db.try_get_good("wheat", QualityGrade::Mid).unwrap().0;
    let nodes = sim.context().services.get::<NodeTable>();
    let from = (0..nodes.count())
        .find(|&n| nodes.level_of(n as i32) == WorldLevel::Village)
        .unwrap();
    let to = nodes.parent[from];
    let world = &mut sim.context_mut().world;
    let markets = world.get_mut::<MarketStore>();
    let cell = markets.try_find_cell(from, wheat).unwrap();
    let to_cell = markets.try_find_cell(to as usize, wheat).unwrap();
    markets.stock[cell] = 1000;
    let (buy, sell_list) = (i64::from(markets.price[cell]), markets.price[to_cell]);
    let unit = buy + 1;
    world.get_mut::<PurseState>().float.fill(1_000_000_000);
    let groups = world.get_mut::<GroupTable>();
    groups.cash[0] = 1_000_000;
    groups.cash[1] = 1_000_000;
    let route = ContractRoute {
        from_cell: cell as i32,
        to_cell: to_cell as i32,
        from_node: from as i32,
        to_node: to,
    };
    world
        .get_mut::<ContractTable>()
        .add(0, 1, route, 10, unit, 100, 0);
    ContractSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let sell = i64::from(trader_offer(db.config.trader_spread, sell_list));
    let world = &sim.context().world;
    assert_eq!(world.get::<MarketStore>().stock[cell], 990);
    assert_eq!(
        world.get::<GroupTable>().cash[0],
        1_000_000 + 10 * (unit - buy)
    );
    assert_eq!(
        world.get::<GroupTable>().cash[1],
        1_000_000 + 10 * (sell - unit)
    );
}

// ---- strategic subsidy (C# StrategicSubsidySystem) ----

#[test]
fn strategic_subsidy_moves_reserve_to_groups_without_making_coins() {
    let mut sim = build_sample(&["state.strategic_subsidy_permille,500"]);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let world = &mut sim.context_mut().world;
    for r in &mut world.get_mut::<BankState>().reserve {
        *r += 10_000_000;
    }
    let before_groups: i64 = world.get::<GroupTable>().cash.iter().sum();
    let before_reserve: i64 = world.get::<BankState>().reserve.iter().sum();
    StrategicSubsidySystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let world = &sim.context().world;
    let paid = world.get::<GroupTable>().cash.iter().sum::<i64>() - before_groups;
    assert!(paid > 0);
    assert_eq!(
        world.get::<BankState>().reserve.iter().sum::<i64>(),
        before_reserve - paid
    );
}
