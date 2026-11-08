//! The command cases of C# `BankTests`: deposits, purchases from the deposit, exchange, loans,
//! the auction guard and the bank state surviving save and load. Merchants are idle, as in C#.
mod common;

use common::sample_with_config;
use fes_sim::agents::asset_prices::{facility_value, Refs};
use fes_sim::agents::balance_sheet::{collateral_value, credit_limit};
use fes_sim::agents::facilities::{FacilityTable, OWNER_BANK, OWNER_PLAYER};
use fes_sim::agents::{AuctionTable, BankState, LoanTable, PlayerState};
use fes_sim::commands::bank::{BankTransfer, Exchange, RepayLoan, TakeLoan};
use fes_sim::commands::trade::BuyGoods;
use fes_sim::economy::{CurrencyState, MarketStore};
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{Command, SimEventRecord, Simulation, SimulationBuilder, TurnReport};

const CITY: &str = "aur_n1_r1_c1";
const VILLAGE: &str = "aur_n1_r1_c1_v1";

const QUIET: &str = "key,value\nroad.upkeep,0\nbank.npc_max_reserve_share_permille,0\nfinance.event_chance_permille,0\npolicy.npc_lobby_share_permille,0\nbank.state_spending_permille,0\nfacility.dividend_permille,0\nstate.defense_permille,0\nstate.project_share_permille,0\nnoble.royal_share_permille,0\nnoble.city_share_permille,0\nnoble.village_share_permille,0\nhouseholds.luxury_tax_permille,0\nmarket.credit_share_permille,0\nmarket.momentum_permille,0\n";

fn quiet(seed: u64) -> Simulation {
    let source = sample_with_config(QUIET);
    SimulationBuilder::from_data(&source, seed)
        .expect("builder")
        .remove_system("agents.merchants")
        .build()
        .expect("build")
}

fn node(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .get::<NodeTable>()
        .try_find(key)
        .expect(key)
        .0
}

fn continent_of(sim: &Simulation, n: i32) -> usize {
    sim.context().services.get::<NodeTable>().continent[n as usize] as usize
}

fn nation_of(sim: &Simulation, key: &str) -> usize {
    let ctx = sim.context();
    ctx.services
        .get::<NodeBanks>()
        .nation_of(ctx.services.get::<NodeTable>(), node(sim, key)) as usize
}

fn place_at(sim: &mut Simulation, key: &str, coins: i64) {
    let at = node(sim, key);
    let continent = continent_of(sim, at);
    let player = sim.context_mut().world.get_mut::<PlayerState>();
    player.coins.fill(0);
    player.node = at;
    player.coins[continent] = coins;
}

fn local_coins(sim: &Simulation) -> i64 {
    let p = sim.world().get::<PlayerState>();
    p.coins[continent_of(sim, p.node)]
}

fn deposit_of(sim: &Simulation, nation: usize) -> i64 {
    sim.world().get::<BankState>().player_deposit[nation]
}

fn reserve_of(sim: &Simulation, nation: usize) -> i64 {
    sim.world().get::<BankState>().reserve[nation]
}

fn named(report: &TurnReport, name: &str) -> usize {
    report.events.iter().filter(|e| e.name() == name).count()
}

fn rejected(report: &TurnReport) -> usize {
    named(report, "CommandRejected")
}

fn turn(sim: &mut Simulation, cmds: Vec<Box<dyn Command>>) -> TurnReport {
    for c in cmds {
        sim.queue(c);
    }
    sim.step().expect("step")
}

fn own_city_facility(sim: &mut Simulation) -> usize {
    let city = node(sim, CITY);
    let t = sim.context_mut().world.get_mut::<FacilityTable>();
    let f = (0..t.count())
        .find(|&i| t.node[i] == city)
        .expect("facility");
    t.owner[f] = OWNER_PLAYER;
    f
}

#[test]
fn deposits_are_made_at_branches_and_earn_interest() {
    let mut sim = quiet(42);
    place_at(&mut sim, CITY, 200_000);
    let nation = nation_of(&sim, CITY);
    let reserve = reserve_of(&sim, nation);
    let report = turn(
        &mut sim,
        vec![
            Box::new(BankTransfer { amount: 100_000 }),
            Box::new(BankTransfer { amount: -200_000 }), // more than the account holds
        ],
    );
    assert_eq!(1, rejected(&report));
    assert_eq!(100_000, local_coins(&sim));
    let rate = sim
        .context()
        .services
        .database
        .config
        .deposit_interest_per_year;
    assert_eq!(
        100_000 + rate.mul_int(100_000) / 52,
        deposit_of(&sim, nation)
    );
    assert_eq!(reserve + 100_000, reserve_of(&sim, nation));

    turn(&mut sim, vec![Box::new(BankTransfer { amount: -40_000 })]);
    assert_eq!(140_000, local_coins(&sim));

    place_at(&mut sim, VILLAGE, 100);
    let report = turn(&mut sim, vec![Box::new(BankTransfer { amount: 50 })]);
    assert_eq!(1, rejected(&report), "villages have no branch");
}

#[test]
fn purchases_at_a_branch_can_be_paid_from_the_deposit() {
    let mut sim = quiet(42);
    for _ in 0..3 {
        sim.step().expect("step");
    }
    place_at(&mut sim, CITY, 5_000);
    let nation = nation_of(&sim, CITY);
    turn(&mut sim, vec![Box::new(BankTransfer { amount: 5_000 })]);
    let city = node(&sim, CITY) as usize;
    let (good, cell) = {
        let markets = sim.world().get::<MarketStore>();
        let cell = markets.market_cell_start[city] as usize;
        (markets.cell_good[cell], cell)
    };
    sim.context_mut().world.get_mut::<MarketStore>().stock[cell] = 100;
    let deposit = deposit_of(&sim, nation);
    let report = turn(
        &mut sim,
        vec![Box::new(BuyGoods {
            good,
            quantity: 2,
            pay_from_deposit: true,
        })],
    );
    let trades: Vec<_> = report
        .events
        .iter()
        .filter_map(|e| match e {
            SimEventRecord::GoodsTraded(t) => Some(*t),
            _ => None,
        })
        .collect();
    assert_eq!(1, trades.len());
    assert_eq!(2, trades[0].filled);
    assert_eq!(0, local_coins(&sim));
    let left = deposit - 2 * i64::from(trades[0].price);
    let rate = sim
        .context()
        .services
        .database
        .config
        .deposit_interest_per_year;
    assert_eq!(left + rate.mul_int(left) / 52, deposit_of(&sim, nation));
}

#[test]
fn exchange_happens_at_the_international_bank_with_a_fee() {
    let mut sim = quiet(42);
    place_at(&mut sim, CITY, 10_000);
    let home = continent_of(&sim, node(&sim, CITY));
    let away = 1 - home;
    let gross = sim
        .world()
        .get::<CurrencyState>()
        .convert(1_000, home, away);
    let fee = sim
        .context()
        .services
        .database
        .config
        .exchange_fee
        .mul_int(gross);
    let exchange = |amount| {
        Box::new(Exchange {
            from_continent: home as i32,
            to_continent: away as i32,
            amount,
        }) as Box<dyn Command>
    };
    let report = turn(&mut sim, vec![exchange(1_000)]);
    let done: Vec<_> = report
        .events
        .iter()
        .filter_map(|e| match e {
            SimEventRecord::CoinsExchanged(c) => Some(*c),
            _ => None,
        })
        .collect();
    assert_eq!(1, done.len());
    assert_eq!(gross - fee, done[0].received);
    let player = sim.world().get::<PlayerState>();
    assert_eq!(9_000, player.coins[home]);
    assert_eq!(gross - fee, player.coins[away]);

    place_at(&mut sim, "aur_n1_r1_c2", 1_000); // a branch, but not the international bank
    let report = turn(&mut sim, vec![exchange(100)]);
    assert_eq!(1, rejected(&report));
}

/// The player's collateral value and credit limit on the continent where the player stands.
fn limits(sim: &Simulation) -> (i64, i64) {
    let ctx = sim.context();
    let r = Refs {
        db: &ctx.services.database,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    };
    let c = r.nodes.continent[ctx.world.get::<PlayerState>().node as usize];
    (
        collateral_value(&ctx.world, r, c),
        credit_limit(&ctx.world, r, c),
    )
}

#[test]
fn loans_are_limited_by_collateral_and_the_reserve() {
    let mut sim = quiet(42);
    place_at(&mut sim, CITY, 0);
    let nation = nation_of(&sim, CITY);
    assert_eq!(0, limits(&sim).1);
    let report = turn(&mut sim, vec![Box::new(TakeLoan { amount: 100 })]);
    assert_eq!(1, rejected(&report), "nothing to pledge");

    let f = own_city_facility(&mut sim);
    let value = facility_value(
        &sim.context().services.database,
        sim.world().get::<FacilityTable>(),
        f,
    );
    let (collateral, limit) = limits(&sim);
    assert_eq!(value, collateral);
    let ltv = sim.context().services.database.config.loan_to_value;
    assert_eq!(ltv.mul_int(value), limit);

    let reserve = reserve_of(&sim, nation);
    let report = turn(
        &mut sim,
        vec![
            Box::new(TakeLoan { amount: limit + 1 }),
            Box::new(TakeLoan { amount: 2_000 }),
            Box::new(TakeLoan { amount: 100 }), // one loan per bank
        ],
    );
    assert_eq!(2, rejected(&report));
    let taken: Vec<_> = report
        .events
        .iter()
        .filter_map(|e| match e {
            SimEventRecord::LoanTaken(l) => Some(*l),
            _ => None,
        })
        .collect();
    assert_eq!(1, taken.len());
    let weekly = taken[0].weekly_payment;
    assert_eq!(2_000 + 200, taken[0].balance); // 10% a year over a 52-week term
    assert_eq!((2_200 + 51) / 52, weekly);
    // The first instalment is collected in the same turn, from the coins just borrowed.
    assert_eq!(2_000 - weekly, local_coins(&sim));
    assert_eq!(reserve - 2_000 + weekly, reserve_of(&sim, nation));
    let loans = sim.world().get::<LoanTable>();
    assert_eq!(1, loans.count());
    assert_eq!(2_200 - weekly, loans.balance[0]);
    assert_eq!(0, loans.overdue_weeks[0]);
    let balance = loans.balance[0];

    repay_and_run_dry(&mut sim, nation, balance);
}

fn repay_and_run_dry(sim: &mut Simulation, nation: usize, balance: i64) {
    let continent = continent_of(sim, node(sim, CITY));
    sim.context_mut().world.get_mut::<PlayerState>().coins[continent] = 10_000;
    let report = turn(sim, vec![Box::new(RepayLoan { amount: balance })]);
    assert_eq!(1, named(&report, "LoanRepaid"));
    sim.context_mut().world.get_mut::<BankState>().reserve[nation] = 10;
    let report = turn(sim, vec![Box::new(TakeLoan { amount: 100 })]);
    assert_eq!(1, rejected(&report), "the bank's reserve is too small");
}

#[test]
fn a_facility_held_for_auction_cannot_go_bankrupt() {
    let mut sim = quiet(42);
    place_at(&mut sim, CITY, 0);
    let f = own_city_facility(&mut sim);
    let weeks = sim
        .context()
        .services
        .database
        .config
        .facility_bankrupt_weeks;
    let closes = sim.turn() + 10;
    let w = &mut sim.context_mut().world;
    let t = w.get_mut::<FacilityTable>();
    t.owner[f] = OWNER_BANK;
    t.cash[f] = -100_000;
    t.negative_cash_weeks[f] = weeks;
    let a = w.get_mut::<AuctionTable>();
    a.facility.push(f as i32);
    a.nation.push(0);
    a.closes_turn.push(closes);
    a.reserve_price.push(1);
    a.player_bid.push(0);
    a.for_player_loan.push(0);
    let report = sim.step().expect("step");
    assert!(named(&report, "FacilityBankrupt") == 0);
    let t = sim.world().get::<FacilityTable>();
    assert_eq!(OWNER_BANK, t.owner[f]);
    assert_eq!(0, t.negative_cash_weeks[f]);
    assert_eq!(1, sim.world().get::<AuctionTable>().facility.len());
}

#[test]
fn bank_state_survives_save_and_load() {
    let mut sim = quiet(42);
    place_at(&mut sim, CITY, 5_000);
    own_city_facility(&mut sim);
    turn(
        &mut sim,
        vec![
            Box::new(BankTransfer { amount: 1_000 }),
            Box::new(TakeLoan { amount: 1_000 }),
        ],
    );
    let services = &sim.context().services;
    let copy = Simulation::load_with(
        (*services.database).clone(),
        (*services.map).clone(),
        &sim.save(),
        |b| b.remove_system("agents.merchants"),
    )
    .expect("load");
    assert_eq!(sim.world_hash(), copy.world_hash());
    assert_eq!(1, copy.world().get::<LoanTable>().count());
}
