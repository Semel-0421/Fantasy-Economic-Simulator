//! Agents finance and securities tables: each saved section reads and writes back to the bytes of
//! `start.save` (layout), and the turn-0 build gives the same bytes (build logic), over all 4 runs.
//! `economy.household_cash`, completed by the agents module's money seed, is compared here too.
use fes_core::testing::fixtures_dir;
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::{read_save, RawSection};
use fes_sim::agents::{
    AgentsModule, AssetMarkets, AuctionTable, BankState, BillRoom, BillTable, BondTable,
    FinanceState, IncomeTaxState, LaborState, LandState, LoanTable, MarginState, NpcLoanTable,
    PurseState, ShareTable, ShortLicences, ShortTable, TraderLoans, VentureTable,
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

// `agents.banks` and `agents.purses` are completed by `MoneyLedger.Seed`, `agents.land` counts the
// spawned facilities: the plain comparison proves the seed and the facility spawn too.
section_tests! {
    banks_round_trips_start_save, banks_build_matches_start_save, "agents.banks", BankState;
    purses_round_trips_start_save, purses_build_matches_start_save, "agents.purses", PurseState;
    land_round_trips_start_save, land_build_matches_start_save, "agents.land", LandState;
    margin_round_trips_start_save, margin_build_matches_start_save, "agents.margin", MarginState;
    loans_round_trips_start_save, loans_build_matches_start_save, "agents.loans", LoanTable;
    auctions_round_trips_start_save, auctions_build_matches_start_save, "agents.auctions", AuctionTable;
    npc_loans_round_trips_start_save, npc_loans_build_matches_start_save, "agents.npc_loans", NpcLoanTable;
    trader_loans_round_trips_start_save, trader_loans_build_matches_start_save, "agents.trader_loans", TraderLoans;
    finance_round_trips_start_save, finance_build_matches_start_save, "agents.finance", FinanceState;
    income_tax_round_trips_start_save, income_tax_build_matches_start_save, "agents.income_tax", IncomeTaxState;
    labor_round_trips_start_save, labor_build_matches_start_save, "agents.labor", LaborState;
    bills_round_trips_start_save, bills_build_matches_start_save, "agents.bills", BillTable;
    bill_room_round_trips_start_save, bill_room_build_matches_start_save, "agents.bill_room", BillRoom;
    bonds_round_trips_start_save, bonds_build_matches_start_save, "agents.bonds", BondTable;
    shares_round_trips_start_save, shares_build_matches_start_save, "agents.shares", ShareTable;
    shorts_round_trips_start_save, shorts_build_matches_start_save, "agents.shorts", ShortTable;
    short_licences_round_trips_start_save, short_licences_build_matches_start_save, "agents.short_licences", ShortLicences;
    asset_markets_round_trips_start_save, asset_markets_build_matches_start_save, "agents.asset_markets", AssetMarkets;
    ventures_round_trips_start_save, ventures_build_matches_start_save, "agents.ventures", VentureTable;
}

#[test]
fn household_cash_build_matches_start_save() {
    assert_build_matches("economy.household_cash");
}
