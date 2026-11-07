use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::full_run::{compare_turn, play_run, section_hash, sha256, RunFixture, TurnHashes};
use fes_sim::SimulationBuilder;
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

fn fixture() -> RunFixture {
    RunFixture::load(&fixtures_dir().join("runs").join(RUN)).expect("fixture")
}

#[test]
fn sha256_matches_known_vectors() {
    let hex = |d: [u8; 32]| d.iter().map(|b| format!("{b:02x}")).collect::<String>();
    assert_eq!(
        hex(sha256(b"abc")),
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    );
    assert_eq!(
        hex(sha256(b"")),
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    );
}

#[test]
fn turn0_matches_without_stepping() {
    let (db, map) = definitions();
    let run = fixture();
    let report = play_run(&db, &map, &run, Some(0)).expect("play");
    assert_eq!(report.turns_compared, 1);
    assert_eq!(report.first_divergence(), None);
}

#[test]
fn first_divergence_names_turn_and_section() {
    let (db, map) = definitions();
    let mut run = fixture();
    let mut sim = SimulationBuilder::new(db.clone(), map.clone(), run.seed)
        .with_default_modules()
        .and_then(SimulationBuilder::build)
        .expect("build");
    sim.step().expect("step");
    // Turn 1 as this build computes it, except for one deliberately wrong section hash.
    let mut wrong = TurnHashes {
        turn: 1,
        world_hash: sim.world_hash(),
        sections: sim
            .world()
            .sections()
            .into_iter()
            .map(|s| (s.key.clone(), section_hash(&s.bytes)))
            .collect(),
    };
    assert_eq!(compare_turn(&sim, &wrong), None);
    *wrong.sections.get_mut("economy.markets").unwrap() ^= 1;
    run.turns = vec![wrong.clone()];
    let report = play_run(&db, &map, &run, None).expect("play");
    assert_eq!(report.first_divergence(), Some((1, "economy.markets")));
    assert_eq!(report.final_hash_matches, None);
    // A wrong world hash alone is named `world_hash`.
    *wrong.sections.get_mut("economy.markets").unwrap() ^= 1;
    wrong.world_hash ^= 1;
    run.turns = vec![wrong];
    let report = play_run(&db, &map, &run, None).expect("play");
    assert_eq!(report.first_divergence(), Some((1, "world_hash")));
}
