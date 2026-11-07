//! Finance group A against the recorded C# turns, and the C# unit tests ported
//! (`MoneyTests.RichMerchant_PaysWages_ScaledByEmployment`,
//! `StateProjectTests.Defense_PassesWhatItBuysThroughSupply_SoStockFallsOnce`).
mod common;

use common::{build_sample, data_dir};
use fes_core::testing::fixtures_dir;
use fes_core::Fx;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::defense::DefenseSystem;
use fes_sim::agents::labor_market::LaborMarketSystem;
use fes_sim::agents::merchants::capital_start;
use fes_sim::agents::{AgentTable, BankState, LaborState, PurseState};
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::System;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: [&str; 4] = [
    "agents.roads",
    "agents.state_spending",
    "agents.labor",
    "agents.defense",
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
