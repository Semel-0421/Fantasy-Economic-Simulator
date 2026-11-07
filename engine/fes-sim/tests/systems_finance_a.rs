//! Finance group A against the recorded C# turns, and the C# unit tests ported
//! (`MoneyTests.RichMerchant_PaysWages_ScaledByEmployment`,
//! `StateProjectTests.Defense_PassesWhatItBuysThroughSupply_SoStockFallsOnce`,
//! `InfrastructureTests.NationRoads_*`, `MoneyTests.Employment_FollowsRealSpending`).
mod common;

use common::{build_sample, data_dir};
use fes_core::testing::fixtures_dir;
use fes_core::Fx;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::defense::DefenseSystem;
use fes_sim::agents::labor_market::LaborMarketSystem;
use fes_sim::agents::merchants::capital_start;
use fes_sim::agents::roads::RoadLost;
use fes_sim::agents::{AgentTable, BankState, LaborState, PurseState};
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::roads::{MAX_CONDITION, NO_OWNER};
use fes_sim::world::{NodeBanks, NodeTable, RoadState, RouteTable};
use fes_sim::{Simulation, System};

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: [&str; 9] = [
    "agents.banks",
    "agents.roads",
    "agents.state_spending",
    "agents.labor",
    "agents.defense",
    "agents.ventures",
    "agents.asset_markets",
    "agents.finance",
    "agents.shorts",
];

#[test]
fn finance_a_matches_on_every_sampled_turn() {
    let source = FileDataSource::new(data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, &NEW).expect("replay");
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

fn wage(employment_permille: i64) -> i64 {
    let mut sim = build_sample(&[]);
    let seed = sim.context().seed();
    let config = sim.context().services.database.config.clone();
    let world = &mut sim.context_mut().world;
    let (a, purse, node) = {
        let agents = world.get::<AgentTable>();
        let purses = world.get::<PurseState>();
        let a = (0..agents.count())
            .find(|&i| (purses.node_float[agents.node[i] as usize] as usize) < purses.nation_count)
            .expect("merchant in a nation");
        let node = agents.node[a] as usize;
        (a, purses.node_float[node] as usize, node)
    };
    let start = capital_start(&config, seed, a as i32);
    world.get_mut::<AgentTable>().cash[a] = start + 1_000_000;
    world.get_mut::<HouseholdCash>().cash[node] -= 1_000_000;
    world.get_mut::<LaborState>().employment[purse] = Fx::from_permille(employment_permille).raw();
    LaborMarketSystem::default()
        .execute(sim.context_mut())
        .expect("labor");
    start + 1_000_000 - sim.world().get::<AgentTable>().cash[a]
}

#[test]
fn rich_merchant_pays_wages_scaled_by_employment() {
    let (full, half) = (wage(1000), wage(500));
    assert!((full - 100_000).abs() <= 20, "{full}");
    assert!((half - 50_000).abs() <= 20, "{half}");
}

#[test]
fn defense_passes_what_it_buys_through_supply() {
    let mut sim = build_sample(&[]);
    let world = &mut sim.context_mut().world;
    world.get_mut::<BankState>().reserve.fill(1_000_000_000_000);
    world.get_mut::<MarketStore>().stock.fill(1000);
    let before = world.get::<MarketStore>().clone();
    let mut defense = DefenseSystem::default();
    defense.initialize(sim.context_mut()).expect("init");
    defense.execute(sim.context_mut()).expect("defense");
    let after = sim.world().get::<MarketStore>();
    let mut bought = 0;
    for c in 0..before.stock.len() {
        let units = before.stock[c] - after.stock[c];
        bought += units;
        assert_eq!(before.supply[c] + units, after.supply[c], "supply {c}");
        assert_eq!(before.demand[c] + units, after.demand[c], "demand {c}");
    }
    assert!(bought > 0);
}

/// C# `InfrastructureTests`: without state spending, dividends or the luxury tax, and with idle
/// aggregate merchants paying no tolls, the reserve moves only by road upkeep.
const ROADS_ONLY: &[&str] = &[
    "bank.state_spending_permille,0",
    "facility.dividend_permille,0",
    "state.defense_permille,0",
    "state.project_share_permille,0",
    "households.luxury_tax_permille,0",
    "market.credit_share_permille,0",
    "market.momentum_permille,0",
    "merchants.must_act,0",
    "lod.mode,2",
];

fn node(sim: &Simulation, key: &str) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    nodes.try_find(key).expect(key).0
}

/// C# `TravelCosts.FindRoute`.
fn route(sim: &Simulation, from: &str, to: &str) -> usize {
    let (from, to) = (node(sim, from), node(sim, to));
    let routes = sim.context().services.get::<RouteTable>();
    (0..routes.count())
        .find(|&r| routes.from[r] == from && routes.to[r] == to)
        .expect("a route")
}

fn city_nation(sim: &Simulation) -> usize {
    let services = &sim.context().services;
    let nodes = services.get::<NodeTable>();
    services
        .get::<NodeBanks>()
        .nation_of(nodes, node(sim, "aur_n1_r1_c1")) as usize
}

#[test]
fn nation_roads_are_kept_from_the_reserve_or_decay() {
    let mut sim = build_sample(ROADS_ONLY);
    let config = sim.context().services.database.config.clone();
    let nation = city_nation(&sim);
    let roads = sim.world().get::<RoadState>();
    let own = (0..roads.condition.len())
        .filter(|&r| roads.is_paved(r) && roads.owner[r] == nation as i32)
        .count() as i64;
    assert_eq!(4, own);

    let reserve = sim.world().get::<BankState>().reserve[nation];
    sim.step().expect("step");
    let after = sim.world().get::<BankState>().reserve[nation];
    assert_eq!(reserve - own * config.road_upkeep, after);

    let paved = route(&sim, "aur_n1_r1_c1", "aur_n1_r1_c2");
    sim.context_mut().world.get_mut::<BankState>().reserve[nation] = 0;
    sim.step().expect("step");
    let condition = sim.world().get::<RoadState>().condition[paved];
    assert_eq!(MAX_CONDITION - config.road_decay_permille, condition);

    sim.context_mut().world.get_mut::<RoadState>().condition[paved] = config.road_decay_permille;
    sim.step().expect("step");
    let roads = sim.world().get::<RoadState>();
    assert_eq!(0, roads.condition[paved]);
    assert_eq!(NO_OWNER, roads.owner[paved]);
    let from = node(&sim, "aur_n1_r1_c1");
    let lost = sim
        .context()
        .events
        .turn_log()
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<RoadLost>().copied())
        .any(|e| e.from == from);
    assert!(lost);
}

#[test]
fn nation_roads_are_not_kept_with_the_players_deposit() {
    let mut sim = build_sample(ROADS_ONLY);
    let config = sim.context().services.database.config.clone();
    let nation = city_nation(&sim);
    let paved = route(&sim, "aur_n1_r1_c1", "aur_n1_r1_c2");
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.reserve[nation] = 100_000;
    bank.player_deposit[nation] = 100_000;
    sim.step().expect("step");
    let condition = sim.world().get::<RoadState>().condition[paved];
    assert_eq!(MAX_CONDITION - config.road_decay_permille, condition);
}

/// C# `MoneyTests.Employment_FollowsRealSpending`, over the ported pipeline.
#[test]
fn employment_follows_real_spending() {
    let mut sim = build_sample(&[]);
    let quarter = 13;
    for _ in 0..quarter * 2 {
        sim.step().expect("step");
    }
    let rate = |sim: &Simulation| sim.world().get::<LaborState>().rate(0).raw();
    let steady = rate(&sim);

    sim.context_mut()
        .world
        .get_mut::<LaborState>()
        .long_run_activity[0] *= 4;
    for _ in 0..quarter {
        sim.step().expect("step");
    }
    assert!(rate(&sim) < steady, "spending far below its long-run level");

    let low = rate(&sim);
    sim.context_mut()
        .world
        .get_mut::<LaborState>()
        .long_run_activity[0] /= 16;
    for _ in 0..quarter {
        sim.step().expect("step");
    }
    assert!(rate(&sim) > low, "spending far above its long-run level");
    let max = sim.context().services.database.config.labor_max_employment;
    assert!(rate(&sim) <= max.raw());
}
