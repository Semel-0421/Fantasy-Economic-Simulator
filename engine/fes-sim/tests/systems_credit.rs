//! `agents.credit` C# unit tests ported (`CreditTests`, `BillMarketTests`). Coin-conservation
//! asserts are not ported (no Rust `ContinentMoney`).
mod common;

use common::{build, sample_config};
use fes_core::{Fx, WorldLevel};
use fes_sim::agents::credit_system::{
    bill_price, bill_room, CreditSystem, BILL_BANK, BILL_HOUSEHOLDS,
};
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::money_ledger::state_excess;
use fes_sim::agents::trader_loans::TraderLoanSystem;
use fes_sim::agents::TraderLoans;
use fes_sim::agents::{
    AgentTable, BankState, BillRoom, BillTable, BondTable, CreditStats, FacilityTable, GroupTable,
    PurseState,
};
use fes_sim::economy::HouseholdCash;
use fes_sim::world::NodeTable;
use fes_sim::{Simulation, System};
use std::sync::Arc;

const ALL_OFF: &[&str] = &[
    "credit.bill_cap_weeks,0",
    "credit.state_debt_permille,0",
    "credit.company_debt_permille,0",
    "credit.working_weeks,0",
    "households.luxury_tax_permille,0",
    "facility.max_listed_shares,0",
    "venture.per_continent,0",
];

/// Only the credit a test names is on.
fn build_with(extras: &[&str]) -> Simulation {
    let key = |l: &str| l.split(',').next().unwrap().to_string();
    let mut lines: Vec<&str> = ALL_OFF
        .iter()
        .copied()
        .filter(|off| !extras.iter().any(|e| key(e) == key(off)))
        .collect();
    lines.extend_from_slice(extras);
    build(&sample_config(&lines), 42)
}

fn run(sim: &mut Simulation) {
    CreditSystem::default().execute(sim.context_mut()).unwrap();
}

fn town(sim: &Simulation, nation: usize) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    *sim.context().world.get::<PurseState>().nation_nodes[nation]
        .iter()
        .find(|&&n| nodes.level_of(n) == WorldLevel::City)
        .unwrap()
}

fn add_bill(sim: &mut Simulation, holder: i32, purse: i32, face: i64, due: i32) {
    let bills = sim.context_mut().world.get_mut::<BillTable>();
    bills.holder.push(holder);
    bills.purse.push(purse);
    bills.face.push(face);
    bills.due_turn.push(due);
}

fn debt_of(sim: &Simulation, issuer: i32) -> i64 {
    let b = sim.context().world.get::<BondTable>();
    (0..b.count())
        .filter(|&r| b.issuer[r] == issuer)
        .map(|r| b.face[r])
        .sum()
}

#[test]
fn bills_fall_due_and_are_paid_within_a_share_of_the_float() {
    let mut sim = build_with(&["credit.bill_cap_weeks,8"]);
    Arc::make_mut(&mut sim.context_mut().services.database)
        .config
        .bill_repay_share = Fx::from_permille(500);
    let turn = sim.context().turn();
    sim.context_mut().world.get_mut::<PurseState>().float[0] = 1_000;
    add_bill(&mut sim, BILL_BANK, 0, 300, turn);
    add_bill(&mut sim, BILL_BANK, 0, 100_000, turn + 1);
    let reserve = sim.context().world.get::<BankState>().reserve[0];
    run(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<BillTable>().count(), 1);
    assert_eq!(w.get::<BillTable>().face[0], 100_000);
    assert_eq!(w.get::<PurseState>().float[0], 700);
    assert_eq!(w.get::<BankState>().reserve[0], reserve + 300);
    let w = &mut sim.context_mut().world;
    w.get_mut::<BillTable>().due_turn[0] = turn;
    w.get_mut::<PurseState>().float[0] = 1_000;
    run(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<BillTable>().face[0], 99_500);
    assert_eq!(w.get::<PurseState>().float[0], 500);
}

#[test]
fn bills_long_overdue_are_lost_to_their_holder() {
    let mut sim = build_with(&["credit.bill_cap_weeks,8"]);
    sim.context_mut().world.get_mut::<PurseState>().float[0] = 0;
    let due = sim.context().turn() - sim.context().services.database.config.bill_default_weeks;
    add_bill(&mut sim, BILL_HOUSEHOLDS, 0, 500, due);
    run(&mut sim);
    assert_eq!(sim.context().world.get::<BillTable>().count(), 0);
    assert_eq!(sim.context().services.get::<CreditStats>().bills_lost, 500);
}

#[test]
fn bill_room_follows_wishes_less_what_is_owed_and_the_bank_keeps_a_floor() {
    let mut sim = build_with(&["credit.bill_cap_weeks,8"]);
    let t = town(&sim, 0);
    let turn = sim.context().turn();
    let w = &mut sim.context_mut().world;
    let nodes = w.get::<PurseState>().nation_nodes[0].clone();
    let cash = w.get_mut::<HouseholdCash>();
    for n in nodes {
        cash.desired[n as usize] = 0;
    }
    cash.desired[t as usize] = 1_000;
    w.get_mut::<BankState>().reserve[0] = 0;
    add_bill(&mut sim, BILL_BANK, 0, 3_000, turn + 5);
    run(&mut sim);
    let room = sim.context().world.get::<BillRoom>();
    assert_eq!(room.room[0], 8 * 1_000 - 3_000);
    assert_eq!(room.bank_room[0], 0);
    sim.context_mut().world.get_mut::<BankState>().reserve[0] = i64::MAX / 4;
    run(&mut sim);
    assert!(sim.context().world.get::<BillRoom>().bank_room[0] > 0);
}

fn clear_nation(sim: &mut Simulation, nation: usize) {
    let w = &mut sim.context_mut().world;
    let nodes = w.get::<PurseState>().nation_nodes[nation].clone();
    let cash = w.get_mut::<HouseholdCash>();
    for n in nodes {
        let n = n as usize;
        cash.cash[n] = 0;
        cash.desired[n] = 0;
        cash.desired_necessity[n] = 0;
    }
}

#[test]
fn state_bonds_move_idle_money_to_short_towns() {
    let mut sim = build_with(&["credit.state_debt_permille,300"]);
    let t = town(&sim, 0) as usize;
    let rich = {
        let nodes = sim.context().services.get::<NodeTable>();
        *sim.context().world.get::<PurseState>().nation_nodes[0]
            .iter()
            .find(|&&n| nodes.level_of(n) == WorldLevel::Village)
            .unwrap() as usize
    };
    clear_nation(&mut sim, 0);
    let cash = sim.context_mut().world.get_mut::<HouseholdCash>();
    cash.cash[rich] = 1_000_000;
    cash.desired_necessity[t] = 1_000;
    run(&mut sim);
    assert_eq!(debt_of(&sim, !0), 1_000);
    let cash = sim.context().world.get::<HouseholdCash>();
    assert_eq!(cash.cash[t], 1_000);
    assert_eq!(cash.cash[rich], 1_000_000 - 1_000);
}

#[test]
fn state_bonds_pay_interest_and_a_part_and_stop_while_the_state_is_behind() {
    let mut sim = build_with(&[
        "credit.state_debt_permille,300",
        "credit.state_bond_interest_permille,520",
        "credit.state_bond_weeks,10",
    ]);
    let t = town(&sim, 0);
    let tu = t as usize;
    sim.context_mut()
        .world
        .get_mut::<BondTable>()
        .add(!0, t, 10_000);
    clear_nation(&mut sim, 0);
    let reserve = sim.context().world.get::<BankState>().reserve[0];
    run(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<BondTable>().face[0], 9_000);
    let got = w.get::<HouseholdCash>().cash[tu];
    assert!((1_095..=1_100).contains(&got), "{got}");
    assert_eq!(w.get::<BankState>().reserve[0], reserve - got);
    let w = &mut sim.context_mut().world;
    let bank = w.get_mut::<BankState>();
    bank.reserve[0] = bank.player_deposit[0];
    let rich = *w.get::<PurseState>().nation_nodes[0]
        .iter()
        .find(|&&n| n != t)
        .unwrap() as usize;
    let cash = w.get_mut::<HouseholdCash>();
    cash.cash[tu] = 0;
    cash.desired_necessity[tu] = 500;
    cash.cash[rich] = 1_000_000;
    run(&mut sim);
    let b = sim.context().world.get::<BondTable>();
    assert_eq!(b.count(), 1);
    assert_eq!(b.face[0], 9_000);
    assert!(b.interest[0] > 0);
}

#[test]
fn groups_feed_their_facilities_from_their_own_money_then_from_bonds() {
    let mut sim = build_with(&["credit.working_weeks,4", "credit.company_debt_permille,500"]);
    let db = sim.context().services.database.clone();
    let w = &mut sim.context_mut().world;
    let purses = w.get::<PurseState>().clone();
    let facilities = w.get::<FacilityTable>();
    let f = (0..facilities.count())
        .find(|&i| {
            facilities.group[i] >= 0
                && facilities.owner[i] == OWNER_NPC
                && db.recipes[db.facilities[facilities.def_index[i] as usize].recipe]
                    .inputs
                    .is_empty()
                && (purses.node_float[facilities.node[i] as usize] as usize) < purses.nation_count
        })
        .unwrap();
    let g = facilities.group[f] as usize;
    let upkeep = fes_sim::agents::asset_prices::facility_upkeep(&db, facilities, f);
    let start = db.config.group_start_cash[w.get::<GroupTable>().kind[g] as usize];
    let facilities = w.get_mut::<FacilityTable>();
    facilities.cash.fill(1_000_000_000);
    facilities.cash[f] = 0;
    w.get_mut::<GroupTable>().cash[g] = start + upkeep;
    run(&mut sim);
    let w = &sim.context().world;
    assert_eq!(
        w.get::<FacilityTable>().cash[f] - debt_of(&sim, g as i32),
        upkeep
    );
    assert_eq!(w.get::<GroupTable>().cash[g], start);
    let nation = purses.node_float[w.get::<GroupTable>().node[g] as usize] as usize;
    let first = purses.nation_nodes[nation][0] as usize;
    let w = &mut sim.context_mut().world;
    let cash = w.get_mut::<HouseholdCash>();
    for &n in &purses.nation_nodes[nation] {
        cash.cash[n as usize] = 0;
    }
    cash.cash[first] = 1_000_000_000;
    cash.desired[first] = 0;
    w.get_mut::<FacilityTable>().cash[f] = 0;
    let before = debt_of(&sim, g as i32);
    run(&mut sim);
    let after = debt_of(&sim, g as i32);
    assert!(after > before, "then bonds");
    assert!(sim.context().world.get::<FacilityTable>().cash[f] >= after - before);
}

#[test]
fn group_too_long_behind_defaults_and_its_holders_lose_the_face() {
    let mut sim = build_with(&[]);
    let t = town(&sim, 0);
    let weeks = sim.context().services.database.config.company_default_weeks;
    let w = &mut sim.context_mut().world;
    let groups = w.get_mut::<GroupTable>();
    let g = (0..groups.count())
        .find(|&i| groups.is_player[i] == 0)
        .unwrap();
    groups.cash[g] = 0;
    let bonds = w.get_mut::<BondTable>();
    bonds.add(g as i32, t, 10_000);
    bonds.behind_weeks[0] = weeks - 1;
    run(&mut sim);
    assert_eq!(sim.context().world.get::<BondTable>().count(), 0);
    assert_eq!(sim.context().services.get::<CreditStats>().defaults, 1);
}

// ---- BillMarketTests ----

const PLENTY: i64 = 1_000_000_000;

#[test]
fn price_follows_what_the_traders_can_pay_and_stays_below_face() {
    let d = Fx::from_permille(150);
    assert_eq!(bill_price(1_000, PLENTY, 1_000, d), 850);
    assert_eq!(bill_price(1_000, 500, 1_000, d), 425);
    assert_eq!(bill_price(1_000, 0, 1_000, d), 0);
    assert_eq!(bill_price(1_000, -5, 1_000, d), 0);
}

#[test]
fn bill_room_never_exceeds_what_the_traders_hold() {
    assert_eq!(bill_room(10_000, 500), 500);
    assert_eq!(bill_room(10_000, -50), 0);
    assert_eq!(bill_room(800, 10_000), 800);
}

/// C# `BillMarketTests.Scene`: one merchant group holding one bill; returns (sim, group, row).
fn scene(cash: i64) -> (Simulation, usize, usize) {
    let mut sim = build(&sample_config(&["credit.bill_cap_weeks,8"]), 1);
    let turn = sim.context().turn();
    let w = &mut sim.context_mut().world;
    let purses = w.get::<PurseState>().clone();
    let agents = w.get_mut::<AgentTable>();
    agents.mode.fill(0);
    let a = (0..agents.count())
        .find(|&a| (purses.node_float[agents.home[a] as usize] as usize) < purses.nation_count)
        .unwrap();
    agents.cash[a] = cash;
    let purse = purses.node_float[agents.home[a] as usize];
    w.get_mut::<PurseState>().float[purse as usize] = PLENTY;
    let households = w.get_mut::<HouseholdCash>();
    for &n in &purses.nation_nodes[purse as usize] {
        households.cash[n as usize] += PLENTY;
    }
    let row = w.get::<BillTable>().count();
    add_bill(&mut sim, a as i32, purse, 1_000, turn + 8);
    (sim, a, row)
}

fn held_by(sim: &Simulation, row: usize, holder: i32) -> i64 {
    let b = sim.context().world.get::<BillTable>();
    (row..b.count())
        .filter(|&r| b.holder[r] == holder)
        .map(|r| b.face[r])
        .sum()
}

#[test]
fn a_group_in_the_red_sells_its_bill_below_face() {
    let (mut sim, a, row) = scene(-100);
    sim.step().unwrap();
    let households = held_by(&sim, row, BILL_HOUSEHOLDS);
    let kept = held_by(&sim, row, a as i32);
    assert!(households > 990);
    assert!(kept < 10);
    assert_eq!(households + kept, 1_000);
    let cash = sim.context().world.get::<AgentTable>().cash[a];
    assert!(cash > 0 && cash < -100 + 1_000, "{cash}");
    assert!(sim.context().services.get::<CreditStats>().bills_sold > 0);
}

#[test]
fn a_group_with_coins_keeps_its_bill() {
    let (mut sim, a, row) = scene(100);
    sim.step().unwrap();
    assert_eq!(sim.context().world.get::<BillTable>().holder[row], a as i32);
    assert_eq!(sim.context().world.get::<AgentTable>().cash[a], 100);
}

// ---- ShareTests (deferred "Listed" test) ----

#[test]
fn a_group_short_of_cash_lists_shares_to_households() {
    let mut sim = build(
        &sample_config(&["credit.working_weeks,4", "credit.company_debt_permille,0"]),
        1,
    );
    for _ in 0..30 {
        sim.step().unwrap();
    }
    let max = sim
        .context()
        .services
        .database
        .config
        .facility_max_listed_shares;
    let w = &sim.context().world;
    let shares = w.get::<fes_sim::agents::ShareTable>();
    let count = w.get::<FacilityTable>().count();
    let listed: i64 = (0..count)
        .map(|f| i64::from(shares.outside(f as i32)))
        .sum();
    assert!(listed > 0, "groups raised working cash by listing shares");
    assert!((0..count).all(|f| shares.outside(f as i32) <= max));
}

// ---- trader loans (TraderLoanTests) ----

fn loans_sim() -> (Simulation, usize) {
    let sim = common::build_sample(&["traders.float_keep_weeks,0"]);
    let t = town(&sim, 0) as usize;
    (sim, t)
}

fn cfg(sim: &mut Simulation) -> &mut fes_data::SimulationConfig {
    &mut Arc::make_mut(&mut sim.context_mut().services.database).config
}

fn run_loans(sim: &mut Simulation) {
    TraderLoanSystem.execute(sim.context_mut()).unwrap();
}

#[test]
fn households_lend_only_what_they_can_spare() {
    let (mut sim, node) = loans_sim();
    cfg(&mut sim).trader_loan_share = Fx::from_permille(100);
    cfg(&mut sim).trader_loan_keep_weeks = 26;
    let cash = sim.context_mut().world.get_mut::<HouseholdCash>();
    cash.desired[node] = 100;
    cash.cash[node] = 2_600;
    let float_before = sim.context().world.get::<PurseState>().float[0];
    run_loans(&mut sim);
    assert_eq!(
        sim.context().world.get::<TraderLoans>().owed_households[node],
        0
    );
    sim.context_mut().world.get_mut::<HouseholdCash>().cash[node] = 12_600;
    run_loans(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<TraderLoans>().owed_households[node], 1_000);
    assert_eq!(w.get::<HouseholdCash>().cash[node], 11_600);
    assert!(w.get::<PurseState>().float[0] > float_before);
}

#[test]
fn traders_pay_interest_and_a_part_within_a_share_of_the_float() {
    let (mut sim, node) = loans_sim();
    let c = cfg(&mut sim);
    c.trader_loan_share = Fx::ZERO;
    c.trader_loan_interest = Fx::from_permille(520);
    c.trader_loan_weeks = 10;
    c.trader_loan_repay_share = Fx::from_permille(500);
    let rate = c.trader_loan_interest;
    let w = &mut sim.context_mut().world;
    w.get_mut::<TraderLoans>().owed_households[node] = 100_000;
    w.get_mut::<HouseholdCash>().cash[node] = 0;
    w.get_mut::<PurseState>().float[0] = 1_000_000;
    let interest = rate.mul_int(100_000) / 52;
    run_loans(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<TraderLoans>().owed_households[node], 90_000);
    assert_eq!(w.get::<TraderLoans>().interest_households[node], 0);
    assert_eq!(w.get::<HouseholdCash>().cash[node], 10_000 + interest);
    let w = &mut sim.context_mut().world;
    w.get_mut::<PurseState>().float[0] = 2_000;
    w.get_mut::<HouseholdCash>().cash[node] = 0;
    let second = rate.mul_int(90_000) / 52;
    run_loans(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<HouseholdCash>().cash[node], 1_000);
    assert_eq!(w.get::<PurseState>().float[0], 1_000);
    assert_eq!(w.get::<TraderLoans>().interest_households[node], 0);
    assert_eq!(
        w.get::<TraderLoans>().owed_households[node],
        90_000 - (1_000 - second)
    );
    let w = &mut sim.context_mut().world;
    w.get_mut::<PurseState>().float[0] = -500;
    w.get_mut::<HouseholdCash>().cash[node] = 0;
    let owed = w.get::<TraderLoans>().owed_households[node];
    run_loans(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<HouseholdCash>().cash[node], 0);
    assert_eq!(w.get::<TraderLoans>().owed_households[node], owed);
    assert!(w.get::<TraderLoans>().interest_households[node] > 0);
}

#[test]
fn households_stop_lending_while_traders_are_behind() {
    let (mut sim, node) = loans_sim();
    cfg(&mut sim).trader_loan_share = Fx::from_permille(100);
    cfg(&mut sim).trader_loan_keep_weeks = 0;
    let weeks = i64::from(cfg(&mut sim).trader_loan_weeks);
    let w = &mut sim.context_mut().world;
    w.get_mut::<HouseholdCash>().cash[node] = 100_000;
    w.get_mut::<TraderLoans>().owed_households[node] = 1_000_000;
    w.get_mut::<PurseState>().float[0] = 0;
    run_loans(&mut sim);
    let w = &sim.context().world;
    assert_eq!(w.get::<TraderLoans>().owed_households[node], 1_000_000);
    assert_eq!(w.get::<HouseholdCash>().cash[node], 100_000);
    sim.context_mut().world.get_mut::<PurseState>().float[0] = 10_000_000;
    run_loans(&mut sim);
    let owed = sim.context().world.get::<TraderLoans>().owed_households[node];
    assert!(
        owed > 1_000_000 - 1_000_000 / weeks,
        "paying up opens lending again"
    );
}

/// `traders.bank_loan_share_permille` is 0 in the shipped data, so this path is reached only here.
#[test]
fn bank_lends_its_excess_and_is_paid_back() {
    let (mut sim, _) = loans_sim();
    cfg(&mut sim).trader_bank_loan_share = Fx::from_permille(100);
    sim.context_mut().world.get_mut::<BankState>().reserve[0] += 1_000_000;
    let reserve = sim.context().world.get::<BankState>().reserve[0];
    let excess = {
        let ctx = sim.context();
        state_excess(
            &ctx.world,
            ctx.services.get::<NodeTable>(),
            ctx.services.get::<fes_sim::world::NodeBanks>(),
            &ctx.services.database.config,
            0,
        )
    };
    assert!(excess > 0);
    run_loans(&mut sim);
    let owed = sim.context().world.get::<TraderLoans>().owed_bank[0];
    assert_eq!(owed, Fx::from_permille(100).mul_int(excess));
    assert_eq!(
        sim.context().world.get::<BankState>().reserve[0],
        reserve - owed
    );
    cfg(&mut sim).trader_bank_loan_share = Fx::ZERO;
    sim.context_mut().world.get_mut::<PurseState>().float[0] = 10_000_000;
    let lower = sim.context().world.get::<BankState>().reserve[0];
    run_loans(&mut sim);
    assert!(sim.context().world.get::<BankState>().reserve[0] > lower);
}
