//! Build-speed measurement for the sample world. Run with
//! `cargo test -p fes-sim --release -- --ignored build_speed --nocapture`.
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::SimulationBuilder;
use std::sync::Arc;
use std::time::{Duration, Instant};

const RUNS: usize = 20;

fn median(mut times: Vec<Duration>) -> Duration {
    times.sort();
    times[times.len() / 2]
}

#[test]
#[ignore]
fn build_speed() {
    let dir = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    );
    let source = FileDataSource::new(dir.into());
    let db = Arc::new(GameDatabase::load(&source).expect("database"));
    let map = Arc::new(MapDef::load(&source).expect("map"));

    let mut builds = Vec::new();
    let mut clones = Vec::new();
    for _ in 0..RUNS {
        let start = Instant::now();
        let sim = SimulationBuilder::shared(db.clone(), map.clone(), 12345)
            .with_default_modules()
            .and_then(SimulationBuilder::build)
            .expect("build");
        builds.push(start.elapsed());
        std::hint::black_box(sim.world_hash());

        let start = Instant::now();
        let copy = ((*db).clone(), (*map).clone());
        clones.push(start.elapsed());
        std::hint::black_box(copy);
    }
    println!(
        "SimulationBuilder::build median of {RUNS}: {:?}",
        median(builds)
    );
    println!(
        "GameDatabase+MapDef clone median of {RUNS}: {:?}",
        median(clones)
    );
}
