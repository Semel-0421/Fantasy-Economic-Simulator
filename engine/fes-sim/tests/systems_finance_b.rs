//! Finance group B against the recorded C# turns, and the C# unit tests ported (`MoneyTests`
//! income tax, `FacilityClosureTests`, `NobleTests`, `MigrationTests`, `StateProjectTests`,
//! `SubsidySiteTests`, `InvestmentTests`).
mod common;

use common::{build_sample, data_dir};
use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::income_tax::{IncomeTaxCollected, IncomeTaxSystem};
use fes_sim::agents::{AgentTable, FacilityTable, IncomeTaxState, NpcLoanTable};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::{SimEvent, Simulation, System};
use std::sync::Arc;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: &[&str] = &["agents.income_tax"];
const WEEKS_PER_YEAR: i32 = 52;

#[test]
fn finance_b_matches_on_every_sampled_turn() {
    let source = FileDataSource::new(data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, NEW).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        for id in NEW {
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

fn of<E: Clone + 'static>(events: &[Arc<dyn SimEvent>]) -> Vec<E> {
    events
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<E>().cloned())
        .collect()
}

fn step(sim: &mut Simulation) -> Vec<Arc<dyn SimEvent>> {
    sim.step().expect("step");
    sim.context().events.turn_log().to_vec()
}

// ---- income tax (MoneyTests, FacilityClosureTests) ----

#[test]
fn income_tax_is_collected_yearly() {
    let mut sim = build_sample(&[]);
    let mut tax = None;
    for _ in 0..=WEEKS_PER_YEAR {
        tax = of::<IncomeTaxCollected>(&step(&mut sim)).pop();
        if tax.is_some() {
            break;
        }
    }
    let tax = tax.expect("collected");
    assert!(tax.households > 0);
    let books = sim.context().world.get::<IncomeTaxState>();
    assert_eq!(tax.households, books.last_households.iter().sum::<i64>());
    assert_eq!(tax.companies, books.last_companies.iter().sum::<i64>());
}

#[test]
fn income_tax_takes_ten_percent_of_profit_nothing_on_a_loss_and_counts_dividends() {
    let mut sim = build_sample(&[]);
    step(&mut sim);
    let npc: Vec<usize> = {
        let f = sim.context().world.get::<FacilityTable>();
        (0..f.count())
            .filter(|&i| f.owner[i] == OWNER_NPC)
            .collect()
    };
    let (gain, loss) = (npc[0], *npc.last().unwrap());
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let world = &mut sim.context_mut().world;
    let books = world.get_mut::<IncomeTaxState>();
    books.facility_start[gain] = 1_000;
    books.facility_dividends[gain] = 5_000;
    books.facility_start[loss] = 5_000;
    books.facility_dividends[loss] = 0;
    books.restart_merchant(0, 1_000);
    world.get_mut::<FacilityTable>().cash[gain] = 11_000;
    world.get_mut::<FacilityTable>().cash[loss] = 1_000;
    world.get_mut::<AgentTable>().cash[0] = 3_000;
    IncomeTaxSystem.execute(sim.context_mut()).unwrap();
    let world = &sim.context().world;
    assert_eq!(world.get::<FacilityTable>().cash[gain], 11_000 - 1_500);
    assert_eq!(world.get::<FacilityTable>().cash[loss], 1_000);
    assert_eq!(world.get::<AgentTable>().cash[0], 3_000 - 200);
    assert_eq!(world.get::<IncomeTaxState>().facility_start[gain], 9_500);
}

const FORTUNE: i64 = 1_000_000_000;
const WORKING: i64 = 1_000_000;

fn pick(sim: &Simulation, state_owned: bool) -> usize {
    let world = &sim.context().world;
    let f = world.get::<FacilityTable>();
    let loans = world.get::<NpcLoanTable>();
    let db = &sim.context().services.database;
    (0..f.count())
        .find(|&i| {
            f.owner[i] == OWNER_NPC
                && loans.find(i as i32).is_none()
                && db.facilities[f.def_index[i] as usize].state_owned == state_owned
        })
        .expect("facility")
}

/// C# `FacilityClosureTests.Year`: a year whose book shows `result`, cash kept up.
fn year(sim: &mut Simulation, f: usize, result: i64) {
    let y = sim.context().clock().year();
    step(sim);
    while sim.context().clock().year() == y {
        let world = &mut sim.context_mut().world;
        let facilities = world.get_mut::<FacilityTable>();
        if facilities.level[f] > 0 && facilities.cash[f] < WORKING {
            facilities.cash[f] = WORKING;
        }
        let cash = facilities.cash[f];
        let books = world.get_mut::<IncomeTaxState>();
        if books.facility_start.len() > f {
            books.facility_start[f] = cash - result;
        }
        step(sim);
    }
    step(sim);
}

#[test]
fn two_losing_years_close_it() {
    let mut sim = build_sample(&["facility.close_loss_years,2"]);
    let f = pick(&sim, false);
    year(&mut sim, f, -FORTUNE);
    assert!(sim.context().world.get::<FacilityTable>().level[f] > 0);
    year(&mut sim, f, -FORTUNE);
    let facilities = sim.context().world.get::<FacilityTable>();
    assert_eq!(facilities.level[f], 0);
    assert_eq!(facilities.cash[f], 0);
}

#[test]
fn state_facilities_stay_open() {
    let mut sim = build_sample(&["facility.close_loss_years,2"]);
    let f = pick(&sim, true);
    year(&mut sim, f, -FORTUNE);
    year(&mut sim, f, -FORTUNE);
    assert!(sim.context().world.get::<FacilityTable>().level[f] > 0);
}
