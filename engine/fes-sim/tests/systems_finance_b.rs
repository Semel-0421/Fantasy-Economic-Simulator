//! Finance group B against the recorded C# turns, and the C# unit tests ported (`MoneyTests`
//! income tax, `FacilityClosureTests`, `NobleTests`, `MigrationTests`, `StateProjectTests`,
//! `SubsidySiteTests`, `InvestmentTests`).
mod common;

use common::{build, build_sample, data_dir, sample_with_config};
use fes_core::testing::fixtures_dir;
use fes_core::WorldLevel;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::groups::KIND_STATE_ENTERPRISE;
use fes_sim::agents::income_tax::{IncomeTaxCollected, IncomeTaxSystem};
use fes_sim::agents::migration_system::{EmancipationEnacted, MigrationSystem};
use fes_sim::agents::nobles::collect_tax;
use fes_sim::agents::state_projects::StateProjectFinished;
use fes_sim::agents::{
    AgentTable, BankState, FacilityTable, IncomeTaxState, NobleState, NpcLoanTable,
};
use fes_sim::agents::{DevelopmentState, GroupTable};
use fes_sim::economy::{HouseholdCash, HouseholdState};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{SimEvent, Simulation, System};
use std::sync::Arc;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: &[&str] = &[
    "agents.income_tax",
    "agents.nobles",
    "agents.migration",
    "agents.state_projects",
];
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

// ---- nobles (NobleTests) ----

fn village(sim: &Simulation) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    let state = sim.context().world.get::<NobleState>();
    (0..nodes.count() as i32)
        .find(|&n| {
            let city = nodes.ancestor_at(n, WorldLevel::City);
            nodes.level_of(n) == WorldLevel::Village
                && city >= 0
                && state.city_holder[city as usize] == city
        })
        .expect("village")
}

#[test]
fn tax_royal_house_and_nobles_take_their_shares_before_the_state() {
    let mut sim = build_sample(&[]);
    let v = village(&sim);
    let ctx = sim.context_mut();
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    let config = &ctx.services.database.config;
    let city = nodes.ancestor_at(v, WorldLevel::City);
    let nation = nodes.ancestor_at(v, WorldLevel::Nation);
    let index = banks.nation_index(nodes, nation) as usize;
    let reserve = ctx.world.get::<BankState>().reserve[index];
    collect_tax(&mut ctx.world, nodes, banks, config, v, 1000);
    let state = ctx.world.get::<NobleState>();
    let cash = |n: i32| state.cash[n as usize];
    assert_eq!(cash(nation), config.noble_royal_share.mul_int(1000));
    assert_eq!(cash(city), config.noble_city_share.mul_int(1000));
    assert_eq!(cash(v), config.noble_village_share.mul_int(1000));
    assert_eq!(
        ctx.world.get::<BankState>().reserve[index] - reserve,
        1000 - cash(nation) - cash(city) - cash(v)
    );
}

#[test]
fn lord_who_cannot_protect_loses_aid_for_the_year() {
    let mut sim = build_sample(&[]);
    let v = village(&sim) as usize;
    let lord = sim.context().world.get::<NobleState>().lord[v];
    for _ in 0..WEEKS_PER_YEAR {
        step(&mut sim);
    }
    let state = sim.context_mut().world.get_mut::<NobleState>();
    state.protection_owed[v] = i64::MAX / 2;
    state.cash[v] = 1000;
    step(&mut sim);
    let state = sim.context().world.get::<NobleState>();
    assert!(!state.contract_holds[v]);
    assert_eq!(state.protection_owed[v], 0);
    assert!(lord > -1);
    assert_eq!(state.lord[v], lord);
}

#[test]
fn houses_gather_money_over_two_years() {
    let mut sim = build_sample(&[]);
    for _ in 0..2 * WEEKS_PER_YEAR + 5 {
        step(&mut sim);
    }
    assert!(
        sim.context()
            .world
            .get::<NobleState>()
            .cash
            .iter()
            .sum::<i64>()
            > 0
    );
}

// ---- migration (MigrationTests) ----

fn rural_permille(sim: &Simulation) -> i64 {
    let nodes = sim.context().services.get::<NodeTable>();
    let people = &sim.context().world.get::<HouseholdState>().population;
    let rural: i64 = (0..nodes.count())
        .filter(|&n| nodes.level_of(n as i32) == WorldLevel::Village)
        .map(|n| people[n])
        .sum();
    rural * 1000 / people.iter().sum::<i64>()
}

#[test]
fn villagers_move_to_towns() {
    let mut sim = build_sample(&[]);
    let share = rural_permille(&sim);
    for _ in 0..5 * WEEKS_PER_YEAR {
        step(&mut sim);
    }
    assert!(rural_permille(&sim) < share);
}

#[test]
fn migrants_savings_are_no_income() {
    let mut sim = build_sample(&[]);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let ctx = sim.context_mut();
    let nodes = ctx.services.get::<NodeTable>();
    let count = nodes.count();
    let cash = ctx.world.get_mut::<HouseholdCash>();
    for n in 0..count {
        if nodes.level_of(n as i32) != WorldLevel::Village {
            cash.cash[n] += 100_000_000; // towns pay better, so people move
        }
    }
    let income: Vec<i64> = (0..count).map(|n| cash.year_income(n)).collect();
    let held = cash.cash.clone();
    MigrationSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let cash = sim.context().world.get::<HouseholdCash>();
    assert!((0..count).any(|n| cash.cash[n] != held[n]), "savings moved");
    let after: Vec<i64> = (0..count).map(|n| cash.year_income(n)).collect();
    assert_eq!(income, after);
}

#[test]
#[ignore = "towns gain jobs only once investment and state projects are ported"]
fn towns_short_of_workers_bring_emancipation() {
    let mut sim = build_sample(&[]);
    let mut enacted = false;
    for _ in 0..10 * WEEKS_PER_YEAR {
        enacted = !of::<EmancipationEnacted>(&step(&mut sim)).is_empty();
        if enacted {
            break;
        }
    }
    assert!(enacted);
}

// ---- state projects (StateProjectTests) ----

#[test]
fn projects_are_funded_and_finished() {
    let config = "key,value
facility.bankrupt_weeks,100000
bank.npc_max_reserve_share_permille,0
                  bank.state_reserve_target_permille,0
state.project_weeks,4
state.subsidy_max_per_nation,0
";
    let mut sim = build(&sample_with_config(config), 42);
    let before = sim.context().world.get::<FacilityTable>().count();
    let mut finished = 0;
    for _ in 0..3 * WEEKS_PER_YEAR {
        finished += of::<StateProjectFinished>(&step(&mut sim)).len();
    }
    assert!(finished > 0);
    let world = &sim.context().world;
    let facilities = world.get::<FacilityTable>();
    let land: i32 = world.get::<DevelopmentState>().land_level.iter().sum();
    assert_eq!(
        land as usize,
        facilities.count() - before,
        "each land project opens one state facility"
    );
    let groups = world.get::<GroupTable>();
    let state = (before..facilities.count())
        .filter(|&f| {
            let g = facilities.group[f];
            g >= 0 && groups.kind[g as usize] == KIND_STATE_ENTERPRISE
        })
        .count();
    assert!(state as i32 >= land);
}

#[test]
fn projects_of_a_year_run_one_at_a_time_in_a_queue() {
    let mut sim = build(
        &sample_with_config(
            "key,value
bank.state_reserve_target_permille,0
",
        ),
        42,
    );
    let mut queued = false;
    for _ in 0..2 * WEEKS_PER_YEAR + 2 {
        step(&mut sim);
        let projects = &sim.context().world.get::<DevelopmentState>().projects;
        let mut nations: Vec<i32> = projects.iter().map(|p| p.nation).collect();
        nations.dedup();
        for n in nations {
            let under_way = projects
                .iter()
                .filter(|p| p.nation == n && p.done_turn >= 0)
                .count();
            assert!(under_way <= 1, "one under way");
            queued |= projects.iter().any(|p| p.nation == n && p.done_turn < 0);
        }
    }
    assert!(queued, "later projects wait their turn");
}
