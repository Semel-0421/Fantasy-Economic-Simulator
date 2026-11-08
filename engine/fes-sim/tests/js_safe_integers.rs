//! The web app reads every number the engine sends as a JavaScript `number`, exact up to 2^53.
//! This plays `Default_Seed1_ThirtyYears` and checks that no coin balance and no integer in a
//! serialized view or event ever reaches that limit.
#![cfg(feature = "serde")]
use fes_core::testing::fixtures_dir;
use fes_core::NodeId;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::economy::pricing::market_view;
use fes_sim::full_run::RunFixture;
use fes_sim::{views, Simulation, SimulationBuilder};
use serde::Serialize;
use serde_json::Value;
use std::path::PathBuf;

const LIMIT: u64 = 1 << 53;

/// Largest absolute integer anywhere in the value.
fn max_int(v: &Value) -> u64 {
    match v {
        Value::Number(n) => n
            .as_i64()
            .map(i64::unsigned_abs)
            .or_else(|| n.as_u64())
            .unwrap_or(0),
        Value::Array(a) => a.iter().map(max_int).max().unwrap_or(0),
        Value::Object(o) => o.values().map(max_int).max().unwrap_or(0),
        _ => 0,
    }
}

fn largest<T: Serialize>(value: &T) -> u64 {
    max_int(&serde_json::to_value(value).expect("serialize"))
}

/// The quotes as the web app receives them (price, stock, offer).
fn market_max(sim: &Simulation, node: NodeId) -> u64 {
    let quotes: Vec<Value> = market_view(sim, node)
        .quotes
        .iter()
        .map(|q| {
            let offer = views::sell_offer(sim.context(), (q.price.raw() >> 16) as i32);
            serde_json::json!({"price": q.price, "stock": q.stock, "offer": offer})
        })
        .collect();
    max_int(&Value::Array(quotes))
}

fn views_max(sim: &Simulation) -> u64 {
    let ctx = sim.context();
    let purse = views::purse(ctx);
    let node = NodeId(purse.node);
    let coins = purse
        .coins
        .iter()
        .map(|c| c.unsigned_abs())
        .max()
        .unwrap_or(0);
    [
        coins,
        largest(&purse),
        largest(&views::cargo(ctx)),
        largest(&views::contracts(ctx)),
        largest(&views::shares(ctx)),
        largest(&views::licences(ctx)),
        largest(&views::statement(ctx)),
        market_max(sim, node),
    ]
    .into_iter()
    .max()
    .unwrap()
}

#[test]

fn serialized_integers_stay_below_2_pow_53_over_thirty_years() {
    let source = FileDataSource::new(PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    )));
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    let run =
        RunFixture::load(&fixtures_dir().join("runs/Default_Seed1_ThirtyYears")).expect("fixture");
    let mut sim = SimulationBuilder::new(db, map, run.seed)
        .with_default_modules()
        .and_then(SimulationBuilder::build)
        .expect("build");
    let turns = run.turns.last().map_or(0, |t| t.turn);
    let mut max = views_max(&sim);
    for turn in 0..turns {
        let report = sim.step().expect("step");
        max = max.max(largest(&report.events));
        max = max.max(views_max(&sim));
        assert!(max < LIMIT, "turn {turn}: {max} reaches 2^53");
    }
    println!("max serialized integer over {turns} turns: {max}");
}
