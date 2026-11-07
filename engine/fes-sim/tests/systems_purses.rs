//! `agents.household_spending` and `agents.home_surplus` against the recorded C# turns.
use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use std::path::PathBuf;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const PORTED: [&str; 7] = [
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
    "agents.production",
    "economy.pricing",
    "agents.household_spending",
    "agents.home_surplus",
];
const RAN: [&str; 2] = ["agents.household_spending", "agents.home_surplus"];

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

#[test]
fn purse_systems_match_on_every_sampled_turn() {
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
        for id in RAN {
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
