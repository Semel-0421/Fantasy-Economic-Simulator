//! The whole turn-0 build: the merchants, facilities, groups, guild memberships and genetics
//! sections, the world hash of every run against `turn0.json`, the save bytes against
//! `start.save`, load/save identity and stepping with no systems.
use fes_core::testing::fixtures_dir;
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::{read_save, RawSection};
use fes_sim::agents::{
    AgentTable, FacilityTable, GroupTable, GuildMembershipTable, MerchantGenetics,
};
use fes_sim::testing::assert_round_trip_with;
use fes_sim::world::NodeTable;
use fes_sim::{Component, Simulation, SimulationBuilder, TURN0_KEYS};
use std::path::{Path, PathBuf};

/// Bytes of the save header: magic, format version, data hash, seed, turn.
const HEADER_BYTES: usize = 4 + 4 + 8 + 8 + 4;

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

struct Run {
    dir: PathBuf,
    bytes: Vec<u8>,
    seed: u64,
    saved: Vec<RawSection>,
}

fn runs() -> Vec<Run> {
    let mut out = Vec::new();
    for entry in std::fs::read_dir(fixtures_dir().join("runs")).expect("runs dir") {
        let dir = entry.expect("entry").path();
        let Ok(bytes) = std::fs::read(dir.join("start.save")) else {
            continue;
        };
        let (header, saved) = read_save(&bytes).expect("save");
        out.push(Run {
            dir,
            bytes,
            seed: header.seed,
            saved,
        });
    }
    assert_eq!(out.len(), 4);
    out
}

fn build(run: &Run) -> Simulation {
    SimulationBuilder::from_data(&overridden_source(&run.dir), run.seed)
        .expect("builder")
        .build()
        .expect("build")
}

fn load_parts(run: &Run) -> (GameDatabase, MapDef) {
    let source = overridden_source(&run.dir);
    (
        GameDatabase::load(&source).expect("database"),
        MapDef::load(&source).expect("map"),
    )
}

fn fresh<C: Component + Clone>() -> C {
    SimulationBuilder::from_data(&FileDataSource::new(data_dir()), 1)
        .expect("builder")
        .build()
        .expect("build")
        .world()
        .get::<C>()
        .clone()
}

fn assert_build_matches(key: &str) {
    for run in runs() {
        let built = build(&run).world().sections();
        let ours = built.iter().find(|s| s.key == key).expect("built section");
        let expected = run.saved.iter().find(|s| s.key == key).expect("saved");
        assert_eq!(ours.version, expected.version, "version of {key}");
        assert!(
            ours.bytes == expected.bytes,
            "bytes of {key} differ in {}",
            run.dir.display()
        );
    }
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
    merchants_round_trips_start_save, merchants_build_matches_start_save, "agents.merchants", AgentTable;
    facilities_round_trips_start_save, facilities_build_matches_start_save, "agents.facilities", FacilityTable;
    groups_round_trips_start_save, groups_build_matches_start_save, "agents.groups", GroupTable;
    guild_memberships_round_trips_start_save, guild_memberships_build_matches_start_save, "agents.guild_memberships", GuildMembershipTable;
    genetics_round_trips_start_save, genetics_build_matches_start_save, "agents.merchant_genetics", MerchantGenetics;
}

#[test]
fn build_matches_turn0_world_hash() {
    for run in runs() {
        let sim = build(&run);
        let text = std::fs::read_to_string(run.dir.join("turn0.json")).expect("turn0.json");
        let json: serde_json::Value = serde_json::from_str(&text).expect("json");
        let expected: u64 = json["world_hash"]
            .as_str()
            .expect("world_hash")
            .parse()
            .expect("u64");
        assert_eq!(sim.world_hash(), expected, "run {}", run.dir.display());
        let keys: Vec<String> = sim.world().sections().into_iter().map(|s| s.key).collect();
        assert_eq!(keys, TURN0_KEYS, "turn-0 keys");
        let saved = sim.save();
        assert!(
            saved[HEADER_BYTES..] == run.bytes[HEADER_BYTES..],
            "save bytes differ in {}",
            run.dir.display()
        );
    }
}

#[test]
fn load_then_save_is_identity() {
    for run in runs() {
        let (db, map) = load_parts(&run);
        let sim = Simulation::load(db, map, &run.bytes).expect("load");
        assert!(sim.save() == run.bytes, "run {}", run.dir.display());
    }
}

#[test]
fn step_without_state_changing_systems_only_advances_the_clock() {
    let run = &runs()[0];
    // Production, pricing and merchants change state every turn; without them no ported system does on turn 0.
    let mut sim = SimulationBuilder::from_data(&overridden_source(&run.dir), run.seed)
        .expect("builder")
        .remove_system("agents.production")
        .remove_system("economy.pricing")
        .remove_system("economy.currency")
        .remove_system("agents.merchants")
        .remove_system("agents.banks")
        .remove_system("agents.roads")
        .remove_system("agents.state_spending")
        .remove_system("agents.labor")
        .remove_system("agents.defense")
        .remove_system("agents.ventures")
        .remove_system("agents.income_tax")
        .remove_system("agents.nobles")
        .remove_system("agents.migration")
        .remove_system("agents.state_projects")
        .remove_system("agents.subsidies")
        .remove_system("agents.investment")
        .remove_system("agents.merchant_vehicles")
        .remove_system("agents.orders")
        .remove_system("agents.demand_forecast")
        .remove_system("agents.merchant_membership")
        .remove_system("agents.asset_markets")
        .remove_system("agents.finance")
        .remove_system("agents.shorts")
        .build()
        .expect("build");
    let before = sim.world().sections();
    let report = sim.step().expect("step");
    assert_eq!(report.turn, 0);
    assert!(report.events.is_empty());
    assert_eq!(sim.turn(), 1);
    let after = sim.world().sections();
    for (a, b) in before.iter().zip(&after) {
        if a.key != "core.clock" {
            assert_eq!(a, b, "{} changed", a.key);
        }
    }
}

#[test]
fn facilities_stand_on_the_map() {
    let run = &runs()[0];
    let sim = build(run);
    let nodes = sim.context().services.get::<NodeTable>().count() as i32;
    let facilities = sim.world().get::<FacilityTable>();
    assert!(facilities.count() > 0);
    assert!(facilities.node.iter().all(|&n| (0..nodes).contains(&n)));
    assert!(
        facilities.group.iter().all(|&g| g >= 0),
        "every NPC facility has an owner"
    );
}
