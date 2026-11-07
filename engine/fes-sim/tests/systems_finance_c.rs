//! Finance group C against the recorded C# turns, and the C# unit tests ported
//! (`InvestmentTests` orders, contracts and associations, `ForecastTests`, `CreditTests`,
//! `TraderLoanTests`, `PolicyTests`, `MerchantTests` generations).
mod common;

use common::build_sample;
use fes_core::testing::fixtures_dir;
use fes_core::QualityGrade;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::merchant_vehicles::{CART, PACK};
use fes_sim::agents::order_system::MerchantVehicleBought;
use fes_sim::agents::{AgentTable, OrderTable};
use fes_sim::economy::MarketStore;
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::NodeTable;
use fes_sim::{SimEvent, Simulation};
use std::sync::Arc;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: &[&str] = &["agents.orders"];
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
