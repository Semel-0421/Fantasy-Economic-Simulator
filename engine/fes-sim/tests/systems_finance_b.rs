//! Finance group B against the recorded C# turns, and the C# unit tests ported (`MoneyTests`
//! income tax, `FacilityClosureTests`, `NobleTests`, `MigrationTests`, `StateProjectTests`,
//! `SubsidySiteTests`, `InvestmentTests`).
mod common;

use common::{build, build_sample, data_dir, sample_with_config};
use fes_core::testing::fixtures_dir;
use fes_core::IndustryTier;
use fes_core::WorldLevel;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::development::SubsidyPolicy;
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::groups::KIND_STATE_ENTERPRISE;
use fes_sim::agents::income_tax::{IncomeTaxCollected, IncomeTaxSystem};
use fes_sim::agents::investment::{output_scale, FacilityFounded};
use fes_sim::agents::merchant_vehicles::{MerchantVehicleSystem, CART, PACK};
use fes_sim::agents::migration_system::{EmancipationEnacted, MigrationSystem};
use fes_sim::agents::nobles::collect_tax;
use fes_sim::agents::places::{allowed, project_kind};
use fes_sim::agents::state_projects::{StateProjectFinished, StateProjectSystem};
use fes_sim::agents::subsidies::{SubsidyEnacted, SubsidySystem};
use fes_sim::agents::OrderTable;
use fes_sim::agents::PurseState;
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
    "agents.subsidies",
    "agents.investment",
    "agents.merchant_vehicles",
];
/// Earlier askers of this turn's bond offers (C# `BondOffers`) run in Rust too, so investment sees
/// the offers they left (see `plan4-audit.md`).
const OFFER_USERS: &[&str] = &["agents.ventures", "agents.asset_markets"];
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
        let rust: Vec<&str> = OFFER_USERS.iter().chain(NEW).copied().collect();
        let report = replay_turn(&step, &db, &map, &rust).expect("replay");
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

// ---- subsidies (StateProjectTests, SubsidySiteTests) ----

#[test]
fn subsidy_opens_producers() {
    let mut sim = build(
        &sample_with_config(
            "key,value
bank.state_reserve_target_permille,0
",
        ),
        42,
    );
    let before = sim.context().world.get::<FacilityTable>().count();
    let mut enacted = 0;
    for _ in 0..4 * WEEKS_PER_YEAR {
        enacted += of::<SubsidyEnacted>(&step(&mut sim)).len();
    }
    assert!(enacted > 0);
    assert!(sim.context().world.get::<FacilityTable>().count() > before);
}

/// C# `SubsidySiteTests.Ready`: a new year, a rich nation 0, and one subsidy on the good of the
/// first type that `fits`.
fn ready(
    lines: &[&str],
    fits: impl Fn(&fes_data::defs::FacilityDef) -> bool,
) -> (Simulation, usize) {
    let mut lines = lines.to_vec();
    lines.push("state.subsidy_max_per_nation,1");
    let mut sim = build_sample(&lines);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let db = Arc::clone(&sim.context().services.database);
    let def = db
        .facilities
        .iter()
        .find(|d| !d.state_owned && !db.recipes[d.recipe].outputs.is_empty() && fits(d))
        .expect("def");
    let good = db.recipes[def.recipe].outputs[0].good.0;
    let world = &mut sim.context_mut().world;
    let development = world.get_mut::<DevelopmentState>();
    development.subsidies.clear();
    development.subsidies.push(SubsidyPolicy {
        nation: 0,
        good,
        end_year: 9_999,
    });
    world.get_mut::<BankState>().reserve[0] += 10_000_000;
    (sim, def.index as usize)
}

/// Necessities met in permille at each of the nation's nodes; the rest at 1000.
fn shortage(sim: &mut Simulation, short: &[(i32, i64)]) {
    let world = &mut sim.context_mut().world;
    let nodes = world.get::<PurseState>().nation_nodes[0].clone();
    let households = world.get_mut::<HouseholdState>();
    for n in nodes {
        households.necessity_wanted[n as usize] = 1000;
        households.necessity_met[n as usize] = 1000;
    }
    for &(n, met) in short {
        households.necessity_met[n as usize] = met;
    }
}

fn live_at(sim: &Simulation, node: i32) -> usize {
    let f = sim.context().world.get::<FacilityTable>();
    (0..f.count())
        .filter(|&i| f.node[i] == node && f.level[i] > 0)
        .count()
}

fn open(sim: &mut Simulation) -> i32 {
    let before = sim.context().world.get::<FacilityTable>().count();
    SubsidySystem::default().execute(sim.context_mut()).unwrap();
    let f = sim.context().world.get::<FacilityTable>();
    if f.count() > before {
        f.node[f.count() - 1]
    } else {
        -1
    }
}

fn cities(sim: &Simulation, def: usize) -> Vec<i32> {
    let nodes = sim.context().services.get::<NodeTable>();
    let def = &sim.context().services.database.facilities[def];
    sim.context().world.get::<PurseState>().nation_nodes[0]
        .iter()
        .copied()
        .filter(|&n| nodes.level_of(n) == WorldLevel::City && allowed(def, nodes, n))
        .collect()
}

fn workshop(d: &fes_data::defs::FacilityDef) -> bool {
    !d.core_only && d.tier != IndustryTier::Primary
}

#[test]
fn a_node_at_the_cap_is_passed_over_for_the_next_shortest() {
    // The cap is the facility count at the first city, read from an identical world first.
    let (probe, def) = ready(&[], workshop);
    let first = cities(&probe, def);
    assert!(first.len() >= 2);
    let cap = format!("state.max_per_node,{}", live_at(&probe, first[0]));
    let (mut sim, def) = ready(&[&cap], workshop);
    let c = cities(&sim, def);
    shortage(&mut sim, &[(c[0], 0), (c[1], 500)]);
    assert_eq!(open(&mut sim), c[1]);
}

#[test]
fn with_no_room_anywhere_no_producer_opens() {
    let (mut sim, _) = ready(&["state.max_per_node,0"], workshop);
    assert_eq!(open(&mut sim), -1);
}

#[test]
fn a_workshop_for_a_core_goes_to_the_core_shortest_of_necessities() {
    let (mut sim, def) = ready(&[], |d| d.core_only);
    let nodes = sim.context().services.get::<NodeTable>();
    let banks = sim.context().services.get::<NodeBanks>();
    let capital = nodes.core[banks.nation_node(nodes, 0) as usize];
    let seats = cities(&sim, def);
    assert!(seats.contains(&capital));
    let Some(&other) = seats.iter().find(|&&n| n != capital) else {
        return; // the nation has no second core town
    };
    let village = *sim.context().world.get::<PurseState>().nation_nodes[0]
        .iter()
        .find(|&&n| nodes.level_of(n) == WorldLevel::Village)
        .expect("village");
    shortage(&mut sim, &[(village, 0), (other, 300), (capital, 600)]);
    assert_eq!(
        open(&mut sim),
        other,
        "not the capital, and not the village where it cannot stand"
    );
}

fn land_project_at(sim: &Simulation, node: i32) -> bool {
    sim.context()
        .world
        .get::<DevelopmentState>()
        .projects
        .iter()
        .any(|p| {
            p.node == node && (p.kind == project_kind::FARMLAND || p.kind == project_kind::MINE)
        })
}

/// A new year with nation 0 rich and short of farmland at its first city; the planner runs once.
fn plan_with_cap(cap: &str) -> (Simulation, i32) {
    let mut sim = build_sample(&[cap]);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let nodes = sim.context().services.get::<NodeTable>();
    let nation = sim.context().world.get::<PurseState>().nation_nodes[0].clone();
    let city = *nation
        .iter()
        .find(|&&n| nodes.level_of(n) == WorldLevel::City)
        .unwrap();
    let short: Vec<(i32, i64)> = nation
        .iter()
        .map(|&n| (n, if n == city { 0 } else { 200 }))
        .collect();
    shortage(&mut sim, &short);
    let world = &mut sim.context_mut().world;
    world.get_mut::<BankState>().reserve[0] += 10_000_000;
    world.get_mut::<DevelopmentState>().projects.clear();
    StateProjectSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    (sim, city)
}

#[test]
fn a_land_project_passes_over_a_node_at_the_cap() {
    let (sim, city) = plan_with_cap("state.max_per_node,999");
    assert!(
        land_project_at(&sim, city),
        "without a cap the neediest town gets the land project"
    );
    let cap = format!("state.max_per_node,{}", live_at(&sim, city));
    let (sim, city) = plan_with_cap(&cap);
    assert!(!land_project_at(&sim, city));
}

// ---- investment (InvestmentTests) ----

#[test]
fn investment_founds_and_expands() {
    let mut sim = build_sample(&[]);
    let at_start = sim.context().world.get::<FacilityTable>().count();
    let mut founded = 0;
    for _ in 0..3 * WEEKS_PER_YEAR {
        founded += of::<FacilityFounded>(&step(&mut sim)).len();
    }
    assert!(founded > 0);
    let facilities = sim.context().world.get::<FacilityTable>();
    assert!(facilities.count() >= at_start + founded);
    let max = sim.context().services.database.config.facility_max_level;
    assert!(facilities.level.iter().all(|&l| l <= max));
}

#[test]
fn expansion_output_grows_faster_than_the_level() {
    let sim = build_sample(&["facility.expansion_bonus_permille,250"]);
    let config = &sim.context().services.database.config;
    assert_eq!(output_scale(1, config), fes_core::Fx::ONE);
    assert_eq!(output_scale(2, config), fes_core::Fx::from_permille(2500));
    assert_eq!(output_scale(5, config), fes_core::Fx::from_int(10));
}

// ---- merchant vehicles (C# MerchantVehicleSystem; InvestmentTests.Merchants_BuyBetterVehicles
// needs agents.orders, Task 6) ----

#[test]
fn a_rich_group_on_pack_animals_orders_a_cart_once() {
    let mut sim = build_sample(&["merchants.vehicle_wear_permille,0"]);
    step(&mut sim);
    while !sim.context().clock().is_new_year() {
        step(&mut sim);
    }
    let agents = sim.context_mut().world.get_mut::<AgentTable>();
    let a = (0..agents.count())
        .find(|&a| agents.mode[a] == 1)
        .expect("automated group");
    agents.vehicle[a] = PACK;
    agents.cash[a] = 1_000_000_000;
    let orders = |sim: &Simulation| {
        let o = sim.context().world.get::<OrderTable>();
        (0..o.count())
            .filter(|&r| o.buyer[r] == a as i32)
            .map(|r| o.vehicle[r])
            .collect::<Vec<_>>()
    };
    sim.context_mut()
        .world
        .get_mut::<OrderTable>()
        .buyer
        .retain(|_| true);
    let before = orders(&sim).len();
    MerchantVehicleSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    let placed = orders(&sim);
    assert_eq!(placed.len(), before + 1);
    assert_eq!(*placed.last().unwrap(), CART);
    MerchantVehicleSystem::default()
        .execute(sim.context_mut())
        .unwrap();
    assert_eq!(orders(&sim).len(), before + 1, "one order at a time");
}
