use fes_core::testing::fixtures_dir;
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::read_save;
use fes_sim::economy::{
    CurrencyState, EconomyModule, HouseholdCash, HouseholdState, MarketModifierTable, MarketStore,
};
use fes_sim::testing::assert_round_trip_with;
use fes_sim::world::WorldModule;
use fes_sim::{Component, Module, Services, SimBuilder, SimClock, WorldState};
use std::path::PathBuf;
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

fn overridden_source(run: &std::path::Path) -> Overridden {
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

/// A builder holding the world and economy modules' registrations for `seed`.
fn registered(source: &dyn DataSource, seed: u64) -> SimBuilder {
    let db = GameDatabase::load(source).expect("database");
    let map = MapDef::load(source).expect("map");
    let mut world = WorldState::new();
    world.add(SimClock::new(seed)).expect("clock");
    let mut builder = SimBuilder::new(world, Services::new(Arc::new(db), Arc::new(map)));
    WorldModule.register(&mut builder).expect("world");
    EconomyModule.register(&mut builder).expect("economy");
    builder
}

fn shipped_builder() -> SimBuilder {
    registered(&FileDataSource::new(data_dir()), 1)
}

fn fresh<C: Component + Clone>() -> C {
    shipped_builder().world.get::<C>().clone()
}

/// Builds each run's world and compares section `key` with the one in its `start.save`.
fn assert_build_matches(key: &str) {
    let runs = fixtures_dir().join("runs");
    let mut checked = 0;
    for entry in std::fs::read_dir(&runs).expect("runs dir") {
        let dir = entry.expect("entry").path();
        let save_path = dir.join("start.save");
        if !save_path.exists() {
            continue;
        }
        let data = std::fs::read(&save_path).expect("start.save");
        let (header, saved) = read_save(&data).expect("save");
        let builder = registered(&overridden_source(&dir), header.seed);
        let built = builder.world.sections();
        let ours = built.iter().find(|s| s.key == key).expect("built section");
        let expected = saved.iter().find(|s| s.key == key).expect("saved section");
        assert_eq!(
            ours.version,
            expected.version,
            "version in {}",
            dir.display()
        );
        assert_eq!(ours.bytes, expected.bytes, "bytes in {}", dir.display());
        checked += 1;
    }
    assert_eq!(checked, 4);
}

#[test]
fn economy_markets_round_trips_start_save() {
    assert_round_trip_with("economy.markets", fresh::<MarketStore>);
}

#[test]
fn economy_markets_build_matches_start_save() {
    assert_build_matches("economy.markets");
}

#[test]
fn economy_modifiers_round_trips_start_save() {
    assert_round_trip_with("economy.modifiers", MarketModifierTable::default);
}

#[test]
fn economy_modifiers_build_matches_start_save() {
    assert_build_matches("economy.modifiers");
}

#[test]
fn economy_currency_round_trips_start_save() {
    assert_round_trip_with("economy.currency", fresh::<CurrencyState>);
}

#[test]
fn economy_currency_build_matches_start_save() {
    assert_build_matches("economy.currency");
}

#[test]
fn economy_households_round_trips_start_save() {
    assert_round_trip_with("economy.households", fresh::<HouseholdState>);
}

#[test]
fn economy_households_build_matches_start_save() {
    assert_build_matches("economy.households");
}

#[test]
fn economy_household_cash_round_trips_start_save() {
    assert_round_trip_with("economy.household_cash", fresh::<HouseholdCash>);
}

// `economy.household_cash` is completed by `MoneyLedger.Seed` in the agents module, so its build is
// compared against `start.save` with that module (tests/agents_finance.rs).
