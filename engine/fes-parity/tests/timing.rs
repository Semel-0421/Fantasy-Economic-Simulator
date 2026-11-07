//! Per-system timing on the sample world. Run with
//! `cargo test -p fes-parity --release --test timing -- --ignored --nocapture`.
use fes_core::testing::fixtures_dir;
use fes_parity::load_definitions;
use fes_parity::steps::PORTED_SYSTEMS;
use fes_sim::replay::{replay_turn, StepFile};
use std::collections::BTreeMap;
use std::time::Duration;

const RUNS: usize = 20;

#[test]
#[ignore]
fn system_speed_turn_52() {
    let fixtures = fixtures_dir();
    let run_dir = fixtures.join("runs/Default_Seed1_ThirtyYears");
    let (db, map) = load_definitions(&run_dir, &fixtures, None).expect("definitions");
    let step = StepFile::read(&run_dir.join("steps/turn_52.bin.gz")).expect("turn 52");
    let mut times: BTreeMap<String, Vec<Duration>> = BTreeMap::new();
    for _ in 0..RUNS {
        let report = replay_turn(&step, &db, &map, PORTED_SYSTEMS).expect("replay");
        assert!(report.first_difference().is_none());
        for (label, t) in report.timings {
            times.entry(label).or_default().push(t);
        }
    }
    for (label, mut v) in times {
        v.sort();
        println!("{label}: median of {RUNS}: {:?}", v[v.len() / 2]);
    }
}
