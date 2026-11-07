//! `economy.pricing` against the recorded C# turns, plus the market view (spec 7.2).
use fes_core::testing::fixtures_dir;
use fes_core::{Fx, NodeId};
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::economy::pricing::market_view;
use fes_sim::economy::MarketStore;
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::SimulationBuilder;
use std::path::PathBuf;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const PORTED: [&str; 5] = [
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
    "agents.production",
    "economy.pricing",
];

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

#[test]
fn pricing_matches_on_every_sampled_turn() {
    let source = FileDataSource::new(data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, &PORTED).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        assert!(
            report.steps.iter().any(
                |s| matches!(s, StepResult::Equal { label } if label == "system:economy.pricing")
            ),
            "turn {t}: economy.pricing did not run in Rust"
        );
    }
}

#[test]
fn market_view_copies_prices() {
    let source = FileDataSource::new(data_dir());
    let mut sim = SimulationBuilder::from_data(&source, 42)
        .expect("builder")
        .build()
        .expect("build");
    sim.step().unwrap();
    let node = 0usize;
    let view = market_view(&sim, NodeId(node as i32));
    let markets = sim.world().get::<MarketStore>();
    let start = markets.market_cell_start[node] as usize;
    let count = markets.market_cell_count[node] as usize;
    assert_eq!(view.quotes.len(), count);
    for (i, q) in view.quotes.iter().enumerate() {
        assert_eq!(q.good.0, markets.cell_good[start + i]);
        assert_eq!(q.price, Fx::from_int(i64::from(markets.price[start + i])));
        assert_eq!(q.stock, i64::from(markets.stock[start + i]));
    }
    // A plain copy: changing the world afterwards does not change the view.
    let before = view.clone();
    sim.step().unwrap();
    assert_eq!(view, before);
    fn is_send<T: Send + Clone>(_: &T) {}
    is_send(&view);
}
