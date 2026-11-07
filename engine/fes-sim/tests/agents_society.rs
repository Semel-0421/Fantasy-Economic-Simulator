//! Agents trade, society and policy tables: each saved section reads and writes back to the bytes
//! of `start.save` (layout), and the turn-0 build gives the same bytes (build logic), over all 4 runs.
use fes_core::testing::fixtures_dir;
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::{read_save, RawSection};
use fes_sim::agents::{
    AgentsModule, CargoTable, ContractTable, DemandForecast, DevelopmentState, GuidanceState,
    LicenseTable, LodState, MerchantTripStats, MigrationState, NobleState, OrderTable, PlayerShip,
    PlayerState, PolicyState,
};
use fes_sim::economy::EconomyModule;
use fes_sim::testing::assert_round_trip_with;
use fes_sim::world::WorldModule;
use fes_sim::{Component, Module, Services, SimBuilder, SimClock, WorldState};
use std::path::{Path, PathBuf};
use std::sync::Arc;

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

/// The shipped data with `config.csv` rows replaced the way C# `TestWorlds.SampleConfig` does.
struct Overridden {
    inner: FileDataSource,
    config: String,
}

impl DataSource for Overridden {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        if name == CONFIG_FILE {
            Ok(self.config.clone())
        } else {
            self.inner.read_text(name)
        }
    }
}

fn overridden_source(run: &Path) -> Overridden {
    let text = std::fs::read_to_string(run.join("turns.json")).expect("turns.json");
    let json: serde_json::Value = serde_json::from_str(&text).expect("json");
    let lines: Vec<String> = json["config"]
        .as_array()
        .expect("config")
        .iter()
        .map(|v| v.as_str().expect("line").to_string())
        .collect();
    let inner = FileDataSource::new(data_dir());
    let base = inner.read_text(CONFIG_FILE).expect("config.csv");
    let mut kept: Vec<&str> = base.lines().collect();
    for line in &lines {
        let prefix = format!("{},", line.split(',').next().unwrap_or(""));
        kept.retain(|l| !l.starts_with(&prefix));
    }
    let mut config = kept.join("\n").trim_end().to_string();
    for line in &lines {
        config.push('\n');
        config.push_str(line);
    }
    config.push('\n');
    Overridden { inner, config }
}

/// A builder holding the world, economy and agents modules' registrations for `seed`.
fn registered(source: &dyn DataSource, seed: u64) -> SimBuilder {
    let db = GameDatabase::load(source).expect("database");
    let map = MapDef::load(source).expect("map");
    let mut world = WorldState::new();
    world.add(SimClock::new(seed)).expect("clock");
    let mut builder = SimBuilder::new(world, Services::new(Arc::new(db), Arc::new(map)));
    WorldModule.register(&mut builder).expect("world");
    EconomyModule.register(&mut builder).expect("economy");
    AgentsModule.register(&mut builder).expect("agents");
    builder
}

fn fresh<C: Component + Clone>() -> C {
    registered(&FileDataSource::new(data_dir()), 1)
        .world
        .get::<C>()
        .clone()
}

/// Calls `check(run dir, builder for that run, saved sections)` for every run with a `start.save`.
fn for_each_run(mut check: impl FnMut(&Path, SimBuilder, &[RawSection])) {
    let runs = fixtures_dir().join("runs");
    let mut checked = 0;
    for entry in std::fs::read_dir(&runs).expect("runs dir") {
        let dir = entry.expect("entry").path();
        let Ok(data) = std::fs::read(dir.join("start.save")) else {
            continue;
        };
        let (header, saved) = read_save(&data).expect("save");
        let builder = registered(&overridden_source(&dir), header.seed);
        check(&dir, builder, &saved);
        checked += 1;
    }
    assert_eq!(checked, 4);
}

fn saved_section<'a>(saved: &'a [RawSection], key: &str) -> &'a RawSection {
    saved.iter().find(|s| s.key == key).expect("saved section")
}

fn assert_section_eq(dir: &Path, built: &[RawSection], saved: &[RawSection], key: &str) {
    let ours = built.iter().find(|s| s.key == key).expect("built section");
    let expected = saved_section(saved, key);
    assert_eq!(ours.version, expected.version, "version of {key}");
    assert_eq!(
        ours.bytes,
        expected.bytes,
        "bytes of {key} in {}",
        dir.display()
    );
}

/// Builds each run's world and compares section `key` with the one in its `start.save`.
fn assert_build_matches(key: &str) {
    for_each_run(|dir, builder, saved| {
        assert_section_eq(dir, &builder.world.sections(), saved, key);
    });
}

macro_rules! section_tests {
    ($($round_trip:ident, $build:ident, $key:expr, $ty:ty;)+) => {$(
        #[test]
        fn $round_trip() {
            assert_round_trip_with($key, fresh::<$ty>);
        }

        #[test]
        fn $build() {
            assert_build_matches($key);
        }
    )+};
}

section_tests! {
    player_round_trips_start_save, player_build_matches_start_save, "agents.player", PlayerState;
    cargo_round_trips_start_save, cargo_build_matches_start_save, "agents.cargo", CargoTable;
    licenses_round_trips_start_save, licenses_build_matches_start_save, "agents.licenses", LicenseTable;
    ship_round_trips_start_save, ship_build_matches_start_save, "agents.ship", PlayerShip;
    orders_round_trips_start_save, orders_build_matches_start_save, "agents.orders", OrderTable;
    demand_forecast_round_trips_start_save, demand_forecast_build_matches_start_save, "agents.demand_forecast", DemandForecast;
    contracts_round_trips_start_save, contracts_build_matches_start_save, "agents.contracts", ContractTable;
    development_round_trips_start_save, development_build_matches_start_save, "agents.development", DevelopmentState;
    nobles_round_trips_start_save, nobles_build_matches_start_save, "agents.nobles", NobleState;
    migration_round_trips_start_save, migration_build_matches_start_save, "agents.migration", MigrationState;
    policy_round_trips_start_save, policy_build_matches_start_save, "agents.policy", PolicyState;
    guidance_round_trips_start_save, guidance_build_matches_start_save, "agents.guidance", GuidanceState;
}

#[test]
fn unsaved_state_is_allocated_and_not_a_section() {
    for_each_run(|_, builder, saved| {
        assert!(builder.services.try_get::<LodState>().is_some());
        assert!(builder.services.try_get::<MerchantTripStats>().is_some());
        assert!(saved
            .iter()
            .all(|s| !s.key.contains("lod") && !s.key.contains("trip")));
        let nodes = builder.services.get::<fes_sim::world::NodeTable>().count();
        assert_eq!(builder.services.get::<LodState>().detail.len(), nodes);
    });
}

#[test]
fn unknown_start_node_is_rejected_with_the_c_message() {
    use fes_data::{DataSource, FileDataSource};
    struct Named(FileDataSource);
    impl DataSource for Named {
        fn exists(&self, n: &str) -> bool {
            self.0.exists(n)
        }
        fn read_text(&self, n: &str) -> Result<String, fes_data::DataError> {
            let t = self.0.read_text(n)?;
            Ok(if n == fes_data::database::CONFIG_FILE {
                format!("{}\nplayer.start_node,nowhere\n", t.trim_end())
            } else {
                t
            })
        }
    }
    let db = fes_data::database::GameDatabase::load(&Named(FileDataSource::new(data_dir())))
        .expect("database");
    let map = fes_data::MapDef::load(&FileDataSource::new(data_dir())).expect("map");
    let mut world = fes_sim::WorldState::new();
    world.add(fes_sim::SimClock::new(1)).expect("clock");
    let mut b = SimBuilder::new(world, Services::new(Arc::new(db), Arc::new(map)));
    WorldModule.register(&mut b).expect("world");
    EconomyModule.register(&mut b).expect("economy");
    let err = AgentsModule.register(&mut b).expect_err("must fail");
    assert_eq!(
        err.to_string(),
        "player.start_node 'nowhere' is not on the map."
    );
}
