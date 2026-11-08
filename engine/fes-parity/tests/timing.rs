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

/// Plays turns 0-53 of the full-scale world, checks the first turns' hashes against the C# export,
/// and prints per-turn and per-system times. Run with
/// `cargo test -p fes-parity --release --test timing full_scale -- --ignored --nocapture`.
#[test]
#[ignore]
fn full_scale_turns_0_to_53() {
    use fes_data::FileDataSource;
    use fes_sim::SimulationBuilder;
    use std::time::Instant;

    let dir = fixtures_dir().join("fullscale");
    let source = FileDataSource::new(dir.clone());
    let mut sim = SimulationBuilder::from_data(&source, 1)
        .and_then(|b| b.build())
        .expect("build full-scale world");
    let expected: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(dir.join("turns.json")).expect("turns.json"))
            .expect("json");
    let hash_of =
        |row: &serde_json::Value| -> u64 { row["world_hash"].as_str().unwrap().parse().unwrap() };
    let rows = expected["turns"].as_array().unwrap();
    assert_eq!(sim.world_hash(), hash_of(&rows[0]), "turn 0 world hash");

    let mut turn_times = Vec::new();
    let mut per_system: BTreeMap<&'static str, Vec<Duration>> = BTreeMap::new();
    for turn in 0..54usize {
        let start = Instant::now();
        let times = sim.step_timed().expect("step");
        let total = start.elapsed();
        turn_times.push(total);
        for (id, t) in times {
            per_system.entry(id).or_default().push(t);
        }
        if let Some(row) = rows.get(turn + 1) {
            assert_eq!(
                sim.world_hash(),
                hash_of(row),
                "world hash after turn {turn}"
            );
        }
    }
    let secs = |d: &Duration| d.as_secs_f64();
    let avg = turn_times.iter().map(secs).sum::<f64>() / turn_times.len() as f64;
    let (worst_turn, worst) = turn_times
        .iter()
        .enumerate()
        .max_by(|a, b| a.1.cmp(b.1))
        .unwrap();
    println!(
        "turns 0-53: average {avg:.3} s, worst {:.3} s (turn {worst_turn})",
        secs(worst)
    );
    println!("turn 52 (new year): {:.3} s", secs(&turn_times[52]));
    let mut medians: Vec<(&str, Duration, Duration)> = per_system
        .into_iter()
        .map(|(id, mut v)| {
            v.sort();
            (id, v[v.len() / 2], *v.last().unwrap())
        })
        .collect();
    medians.sort_by_key(|a| std::cmp::Reverse(a.1));
    for (id, median, max) in medians {
        println!(
            "{id}: median {:.2} ms, max {:.2} ms",
            median.as_secs_f64() * 1e3,
            max.as_secs_f64() * 1e3
        );
    }
}
