//! Saving mid-run and loading continues the golden run (C# `GoldenRunTests` save/load).
use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::full_run::{compare_turn, RunFixture};
use fes_sim::{Simulation, SimulationBuilder};
use std::path::PathBuf;

const RUN: &str = "Default_Seed1_ThirtyYears";

fn definitions() -> (GameDatabase, MapDef) {
    let dir = PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ));
    let source = FileDataSource::new(dir);
    (
        GameDatabase::load(&source).expect("database"),
        MapDef::load(&source).expect("map"),
    )
}

#[test]
fn save_load_mid_run_continues() {
    let (db, map) = definitions();
    let run = RunFixture::load(&fixtures_dir().join("runs").join(RUN)).expect("fixture");
    let mut sim = SimulationBuilder::new(db.clone(), map.clone(), run.seed)
        .with_default_modules()
        .and_then(SimulationBuilder::build)
        .expect("build");
    let mut saves = Vec::new();
    for expected in run.turns.iter().take_while(|t| t.turn <= 520) {
        sim.step().expect("step");
        assert_eq!(compare_turn(&sim, expected), None, "turn {}", expected.turn);
        if expected.turn == 52 || expected.turn == 520 {
            saves.push((expected.turn, sim.save()));
        }
    }
    assert_eq!(saves.len(), 2);
    for (at, bytes) in saves {
        let mut loaded = Simulation::load(db.clone(), map.clone(), &bytes).expect("load");
        assert_eq!(loaded.turn(), at);
        for expected in run.turns.iter().filter(|t| t.turn > at).take(10) {
            loaded.step().expect("step");
            assert_eq!(
                compare_turn(&loaded, expected),
                None,
                "after load at {at}, turn {}",
                expected.turn
            );
        }
    }
}
