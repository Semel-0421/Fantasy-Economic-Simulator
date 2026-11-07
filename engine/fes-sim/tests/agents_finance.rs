//! Agents finance and securities tables: each saved section reads and writes back to the bytes of
//! `start.save` (layout), and the turn-0 build gives the same bytes (build logic), over all 4 runs.
use fes_core::io::DotNetReader;
use fes_core::testing::fixtures_dir;
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::{read_save, RawSection};
use fes_sim::agents::ledger::{seed, SeedInputs};
use fes_sim::agents::{
    AgentsModule, AssetMarkets, AuctionTable, BankState, BillRoom, BillTable, BondTable,
    FinanceState, IncomeTaxState, LaborState, LandState, LoanTable, MarginState, NpcLoanTable,
    PurseState, ShareTable, ShortLicences, ShortTable, TraderLoans, VentureTable,
};
use fes_sim::economy::{CurrencyState, EconomyModule, HouseholdCash, HouseholdState};
use fes_sim::testing::assert_round_trip_with;
use fes_sim::world::{NodeTable, WorldModule};
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

// Sections the agents module fills only through `MoneyLedger.Seed` or the facility table are proved
// by their own tests below.
section_tests! {
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
fn banks_round_trips_start_save() {
    assert_round_trip_with("agents.banks", fresh::<BankState>);
}

#[test]
fn purses_round_trips_start_save() {
    assert_round_trip_with("agents.purses", fresh::<PurseState>);
}

#[test]
fn land_round_trips_start_save() {
    assert_round_trip_with("agents.land", fresh::<LandState>);
}

/// Everything in `agents.banks` except the international reserve is set by the build.
#[test]
fn banks_build_matches_start_save_except_seeded_reserve() {
    for_each_run(|dir, builder, saved| {
        let built = builder.world.get::<BankState>();
        let mut expected = built.clone();
        let section = saved_section(saved, "agents.banks");
        expected
            .read(&mut DotNetReader::new(&section.bytes), section.version)
            .expect("read saved");
        assert!(
            expected.international_reserve != built.international_reserve,
            "seeded reserve"
        );
        expected.international_reserve = built.international_reserve.clone();
        assert_eq!(&expected, built, "run {}", dir.display());
    });
}

/// `agents.purses` holds only the floats, which `MoneyLedger.Seed` fills; the build gives their length.
#[test]
fn purses_build_has_one_float_per_purse() {
    for_each_run(|_, builder, saved| {
        let built = builder.world.get::<PurseState>();
        let mut expected = built.clone();
        let section = saved_section(saved, "agents.purses");
        expected
            .read(&mut DotNetReader::new(&section.bytes), section.version)
            .expect("read saved");
        assert_eq!(expected.float.len(), built.count());
    });
}

/// Land's `used` counts facilities, which are built by a later task: feed the saved `used` column
/// back as the facilities' nodes and require the rest (plots, player plots) to build the same bytes.
#[test]
fn land_build_matches_start_save_given_the_facility_nodes() {
    for_each_run(|dir, builder, saved| {
        let section = saved_section(saved, "agents.land");
        let mut read = builder.world.get::<LandState>().clone();
        read.read(&mut DotNetReader::new(&section.bytes), section.version)
            .expect("read saved");
        let nodes = builder.services.get::<NodeTable>();
        let facility_nodes: Vec<i32> = (0..nodes.count())
            .flat_map(|n| std::iter::repeat_n(n as i32, read.used[n] as usize))
            .collect();
        let config = &builder.services.database.config;
        let built = LandState::new(nodes, config, &facility_nodes);
        assert!(facility_nodes.len() > 1, "facilities exist");
        assert_eq!(built, read, "run {}", dir.display());
    });
}

/// `MoneyLedger.Seed` splits each continent's coin supply after the other tables exist. The coins
/// held by the player, facilities, merchants, groups, ventures and noble houses are built by later
/// tasks, so they are recovered from the saved world as the supply minus every coin in the tables
/// seeded here. With them, seeding the built world must give the saved bytes of `agents.banks`,
/// `agents.purses` and `economy.household_cash`, which proves the whole split (international share,
/// float share and weights, household weights, rounding remainders, `StartYear`).
#[test]
fn seed_reproduces_banks_purses_and_household_cash() {
    for_each_run(|dir, mut builder, saved| {
        let other = other_money_from(&builder, saved);
        let (mut purses, mut bank, mut households) = (
            builder.world.get::<PurseState>().clone(),
            builder.world.get::<BankState>().clone(),
            builder.world.get::<HouseholdCash>().clone(),
        );
        let population = builder.world.get::<HouseholdState>().population.clone();
        let nodes = builder.services.get::<NodeTable>();
        let currency = builder.world.get::<CurrencyState>();
        let config = &builder.services.database.config;
        let inputs = SeedInputs {
            nodes,
            config,
            currency,
            population: &population,
            other_money: &other,
        };
        seed(&inputs, &mut purses, &mut bank, &mut households);
        *builder.world.get_mut::<PurseState>() = purses;
        *builder.world.get_mut::<BankState>() = bank;
        *builder.world.get_mut::<HouseholdCash>() = households;
        let built = builder.world.sections();
        for key in ["agents.banks", "agents.purses", "economy.household_cash"] {
            assert_section_eq(dir, &built, saved, key);
        }
    });
}

/// Per continent: coin supply less the coins in banks, purse floats and households of the saved world.
fn other_money_from(builder: &SimBuilder, saved: &[RawSection]) -> Vec<i64> {
    let read = |key: &str, c: &mut dyn Component| {
        let s = saved_section(saved, key);
        c.read(&mut DotNetReader::new(&s.bytes), s.version)
            .expect("read saved");
    };
    let (mut bank, mut purses, mut households) = (
        builder.world.get::<BankState>().clone(),
        builder.world.get::<PurseState>().clone(),
        builder.world.get::<HouseholdCash>().clone(),
    );
    read("agents.banks", &mut bank);
    read("agents.purses", &mut purses);
    read("economy.household_cash", &mut households);
    let nodes = builder.services.get::<NodeTable>();
    let currency = builder.world.get::<CurrencyState>();
    (0..currency.continent_count())
        .map(|c| {
            let continent = c as i32;
            let mut money = bank.international_reserve[c];
            money += (0..purses.nation_count)
                .filter(|&n| purses.continent_of(n) == continent)
                .map(|n| bank.reserve[n])
                .sum::<i64>();
            money += (0..purses.count())
                .filter(|&p| purses.continent_of(p) == continent)
                .map(|p| purses.float[p])
                .sum::<i64>();
            money += (0..nodes.count())
                .filter(|&n| nodes.continent[n] == continent)
                .map(|n| households.cash[n] + households.spent[n])
                .sum::<i64>();
            currency.coin_supply[c] - money
        })
        .collect()
}
