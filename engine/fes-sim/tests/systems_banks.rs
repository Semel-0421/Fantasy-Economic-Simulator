//! `agents.banks` against the C# unit tests (`BankTests`, `MarginTests`, and
//! `MoneyTests.IncomeTax_DoesNotCount...Loan`). Commands are not ported yet, so loans, deposits and
//! bids are set up as the C# commands' `Apply` leaves them, and the bank system runs alone, one
//! turn per run.
mod common;

use common::{build_sample, continent_count, continent_money};
use fes_core::QualityGrade;
use fes_sim::agents::asset_prices::{land_price, share_price, Refs, PLAYER_HOLDER};
use fes_sim::agents::balance_sheet::{collateral_value, convert, credit_limit, debt};
use fes_sim::agents::bank_npc::merchant_borrower;
use fes_sim::agents::bank_system::{
    AuctionClosed, BankSystem, CollateralKind, CollateralSeized, LoanRepaid, MarginCalled,
};
use fes_sim::agents::cargo::merchant_holder;
use fes_sim::agents::facilities::{OWNER_BANK, OWNER_NPC, OWNER_PLAYER};
use fes_sim::agents::{
    AgentTable, AuctionTable, BankState, CargoTable, FacilityTable, GroupTable, IncomeTaxState,
    LandState, LoanTable, NpcLoanTable, PlayerState, ShareTable,
};
use fes_sim::commands::bank::{RepayLoan, TakeLoan};
use fes_sim::economy::CurrencyState;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{Command, CommandResult, SimEvent, Simulation, System};
use std::sync::Arc;

const CITY: &str = "aur_n1_r1_c1";
const VILLAGE: &str = "aur_n1_r1_c1_v1";
const ABROAD: &str = "bor_n1_r1_c2";
/// C# `BankTests.Build`: reserves move only by what a test does.
const QUIET: &[&str] = &[
    "road.upkeep,0",
    "bank.npc_max_reserve_share_permille,0",
    "finance.event_chance_permille,0",
    "policy.npc_lobby_share_permille,0",
    "bank.state_spending_permille,0",
    "facility.dividend_permille,0",
    "state.defense_permille,0",
    "state.project_share_permille,0",
    "noble.royal_share_permille,0",
    "noble.city_share_permille,0",
    "noble.village_share_permille,0",
    "households.luxury_tax_permille,0",
    "market.credit_share_permille,0",
    "market.momentum_permille,0",
];

fn refs(sim: &Simulation) -> Refs<'_> {
    let services = &sim.context().services;
    Refs {
        db: &services.database,
        nodes: services.get::<NodeTable>(),
        banks: services.get::<NodeBanks>(),
    }
}

fn node(sim: &Simulation, key: &str) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    nodes.try_find(key).expect(key).0
}

fn nation_of(sim: &Simulation, key: &str) -> usize {
    let r = refs(sim);
    r.banks.nation_of(r.nodes, node(sim, key)) as usize
}

fn continent_of(sim: &Simulation, n: i32) -> i32 {
    sim.context().services.get::<NodeTable>().continent[n as usize]
}

/// C# `PlaceAt`: the player at `key` with exactly `coins` of its continent's coin.
fn place_at(sim: &mut Simulation, key: &str, coins: i64) {
    let at = node(sim, key);
    let continent = continent_of(sim, at) as usize;
    let player = sim.context_mut().world.get_mut::<PlayerState>();
    player.coins.fill(0);
    player.node = at;
    player.coins[continent] = coins;
}

fn local_coins(sim: &Simulation) -> i64 {
    let player = sim.world().get::<PlayerState>();
    player.coins[continent_of(sim, player.node) as usize]
}

/// C# `OwnCityFacility`.
fn own_city_facility(sim: &mut Simulation) -> usize {
    let city = node(sim, CITY);
    let facilities = sim.context_mut().world.get_mut::<FacilityTable>();
    let f = (0..facilities.count())
        .find(|&i| facilities.node[i] == city)
        .expect("a city facility");
    facilities.owner[f] = OWNER_PLAYER;
    f
}

/// C# `Borrow`: a `TakeLoan` at the player's branch.
fn borrow(sim: &mut Simulation, amount: i64) {
    let cmd = TakeLoan { amount };
    assert_eq!(CommandResult::Ok, cmd.validate(sim.context()));
    cmd.apply(sim.context_mut());
}

fn add_auction(
    sim: &mut Simulation,
    f: usize,
    nation: usize,
    closes: i32,
    reserve: i64,
    for_player: bool,
) {
    let auctions = sim.context_mut().world.get_mut::<AuctionTable>();
    auctions.facility.push(f as i32);
    auctions.nation.push(nation as i32);
    auctions.closes_turn.push(closes);
    auctions.reserve_price.push(reserve);
    auctions.player_bid.push(0);
    auctions.for_player_loan.push(u8::from(for_player));
}

/// One turn of the bank system; returns the events it published.
fn run(sim: &mut Simulation) -> Vec<Arc<dyn SimEvent>> {
    let ctx = sim.context_mut();
    let before = ctx.events.turn_log().len();
    BankSystem.execute(ctx).expect("banks");
    ctx.dispatch_events().expect("events");
    let events = ctx.events.turn_log()[before..].to_vec();
    ctx.clock_mut().advance();
    events
}

fn of<E: Clone + 'static>(events: &[Arc<dyn SimEvent>]) -> Vec<E> {
    events
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<E>().cloned())
        .collect()
}

/// Runs up to `turns` turns until one publishes an `E`; returns that turn's events.
fn run_until<E: Clone + 'static>(sim: &mut Simulation, turns: i32) -> Vec<Arc<dyn SimEvent>> {
    let mut events = Vec::new();
    for _ in 0..turns {
        events = run(sim);
        if !of::<E>(&events).is_empty() {
            break;
        }
    }
    events
}

#[test]
fn reserves_start_as_a_share_of_coin_supply_and_receive_minted_coins() {
    let mut sim = build_sample(QUIET);
    let nation = nation_of(&sim, CITY);
    let c = continent_of(&sim, node(&sim, CITY)) as usize;
    let share = sim
        .context()
        .services
        .database
        .config
        .bank_initial_reserve_share;
    let supply = sim.world().get::<CurrencyState>().coin_supply[c];
    assert_eq!(
        share.mul_int(supply),
        sim.world().get::<BankState>().reserve[nation]
    );
    run(&mut sim);
    let held = |sim: &Simulation| {
        let bank = sim.world().get::<BankState>();
        bank.reserve[nation] + bank.international_reserve[c]
    };
    let before = held(&sim);
    sim.context_mut()
        .world
        .get_mut::<CurrencyState>()
        .coin_supply[c] += 52_000;
    run(&mut sim);
    assert_eq!(before + 52_000, held(&sim));
    let bank = sim.world().get::<BankState>();
    assert_eq!(supply + 52_000, bank.distributed_supply[c]);
}

#[test]
fn deposits_earn_interest() {
    let mut sim = build_sample(QUIET);
    let nation = nation_of(&sim, CITY);
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.player_deposit[nation] = 100_000;
    bank.reserve[nation] += 100_000;
    run(&mut sim);
    let rate = sim
        .context()
        .services
        .database
        .config
        .deposit_interest_per_year;
    let expected = 100_000 + rate.mul_int(100_000) / 52;
    assert_eq!(
        expected,
        sim.world().get::<BankState>().player_deposit[nation]
    );
}

#[test]
fn a_facility_seized_from_the_player_leaves_a_claim_on_its_warehouse_when_an_npc_buys_it() {
    let mut sim = build_sample(QUIET);
    sim.context_mut().world.get_mut::<FacilityTable>().owner[0] = OWNER_BANK;
    let turn = sim.turn();
    add_auction(&mut sim, 0, 0, turn, 1, true);
    run(&mut sim);
    let facilities = sim.world().get::<FacilityTable>();
    assert_eq!(OWNER_NPC, facilities.owner[0]);
    assert!(facilities.warehouse_claim_until[0] >= sim.turn());
}

#[test]
fn a_facility_the_owner_buys_back_no_longer_has_a_claim_running() {
    let mut sim = build_sample(QUIET);
    let turn = sim.turn();
    let facilities = sim.context_mut().world.get_mut::<FacilityTable>();
    facilities.owner[0] = OWNER_BANK;
    facilities.warehouse_claim_until[0] = turn + 100;
    add_auction(&mut sim, 0, 0, turn, 1, true);
    let world = &mut sim.context_mut().world;
    world.get_mut::<AuctionTable>().player_bid[0] = 1;
    world.get_mut::<PlayerState>().coins.fill(1_000_000);
    run(&mut sim);
    let facilities = sim.world().get::<FacilityTable>();
    assert_eq!(OWNER_PLAYER, facilities.owner[0]);
    assert_eq!(-1, facilities.warehouse_claim_until[0]);
}

#[test]
fn unpaid_loan_leads_to_seizure_and_auction_to_an_npc() {
    let mut sim = build_sample(QUIET);
    place_at(&mut sim, CITY, 0);
    let nation = nation_of(&sim, CITY);
    let f = own_city_facility(&mut sim);
    borrow(&mut sim, 2_000);
    run(&mut sim);
    place_at(&mut sim, VILLAGE, 0); // away from branches, nothing to pay with
    let config = sim.context().services.database.config.clone();
    let seized = of::<CollateralSeized>(&run_until::<CollateralSeized>(
        &mut sim,
        config.loan_grace_weeks + 2,
    ));
    assert_eq!(1, seized.len());
    assert_eq!(CollateralKind::Facility, seized[0].kind);
    assert_eq!(OWNER_BANK, sim.world().get::<FacilityTable>().owner[f]);
    let reserve_price = sim.world().get::<AuctionTable>().reserve_price[0];
    let value = fes_sim::agents::asset_prices::facility_value(
        &sim.context().services.database,
        sim.world().get::<FacilityTable>(),
        f,
    );
    assert_eq!(config.auction_reserve_share.mul_int(value), reserve_price);

    let owed = sim.world().get::<LoanTable>().balance[0];
    let reserve = sim.world().get::<BankState>().reserve[nation];
    let events = run_until::<AuctionClosed>(&mut sim, config.auction_weeks + 1);
    let closed = of::<AuctionClosed>(&events);
    assert_eq!(1, closed.len());
    assert!(!closed[0].won_by_player);
    assert_eq!(reserve_price, closed[0].price);
    assert_eq!(OWNER_NPC, sim.world().get::<FacilityTable>().owner[f]);
    // The sale repays the debt; what is left goes to the bank, not the former owner (DEC-039).
    assert!(reserve_price > owed);
    assert_eq!(1, of::<LoanRepaid>(&events).len());
    assert_eq!(
        reserve + reserve_price,
        sim.world().get::<BankState>().reserve[nation]
    );
    assert_eq!(0, local_coins(&sim));
}

fn deposit_abroad(sim: &mut Simulation, deposit: i64, reserve: Option<i64>) -> usize {
    let abroad = nation_of(sim, ABROAD);
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.player_deposit[abroad] = deposit;
    bank.reserve[abroad] = reserve.unwrap_or(bank.reserve[abroad] + deposit);
    abroad
}

#[test]
fn seizure_takes_deposits_first_and_the_player_can_buy_back_at_auction() {
    let mut sim = build_sample(QUIET);
    place_at(&mut sim, CITY, 0);
    let f = own_city_facility(&mut sim);
    borrow(&mut sim, 3_000);
    run(&mut sim);
    place_at(&mut sim, VILLAGE, 0);
    let abroad = deposit_abroad(&mut sim, 1_000, None);
    let config = sim.context().services.database.config.clone();
    let mut seized = Vec::new();
    for _ in 0..=config.loan_grace_weeks + 20 {
        seized.extend(of::<CollateralSeized>(&run(&mut sim)));
        if seized.iter().any(|s| s.kind == CollateralKind::Facility) {
            break;
        }
    }
    assert_eq!(CollateralKind::Deposit, seized[0].kind);
    assert_eq!(0, sim.world().get::<BankState>().player_deposit[abroad]);
    let auctions = sim.world().get::<AuctionTable>();
    let a = (0..auctions.count())
        .find(|&a| auctions.facility[a] == f as i32)
        .expect("auction");
    let reserve = auctions.reserve_price[a];

    // Instalments keep coming out of the coins carried, so bring more than the bid.
    let coins = reserve + 10_000;
    let at = sim.world().get::<FacilityTable>().node[f];
    let key = sim
        .context()
        .services
        .get::<NodeTable>()
        .key_of(at)
        .to_string();
    place_at(&mut sim, &key, coins);
    sim.context_mut().world.get_mut::<AuctionTable>().player_bid[a] = reserve;
    let mut closed = Vec::new();
    for _ in 0..=config.auction_weeks {
        closed = of::<AuctionClosed>(&run(&mut sim));
        if !closed.is_empty() {
            break;
        }
    }
    assert!(closed[0].won_by_player);
    assert_eq!(OWNER_PLAYER, sim.world().get::<FacilityTable>().owner[f]);
    assert!(local_coins(&sim) <= coins - closed[0].price);
}

#[test]
fn players_loan_is_not_covered_by_another_borrowers_auction() {
    let mut sim = build_sample(QUIET);
    place_at(&mut sim, CITY, 0);
    let f = own_city_facility(&mut sim);
    borrow(&mut sim, 2_000);
    run(&mut sim);
    place_at(&mut sim, VILLAGE, 0);
    let facilities = sim.context_mut().world.get_mut::<FacilityTable>();
    let other = (0..facilities.count())
        .find(|&i| facilities.owner[i] == OWNER_NPC)
        .expect("an NPC facility");
    facilities.owner[other] = OWNER_BANK;
    let nation = nation_of(&sim, CITY);
    let turn = sim.turn();
    add_auction(&mut sim, other, nation, turn + 10_000, 1_000_000, false);
    let grace = sim.context().services.database.config.loan_grace_weeks;
    for _ in 0..=grace + 1 {
        if sim.world().get::<FacilityTable>().owner[f] != OWNER_PLAYER {
            break;
        }
        run(&mut sim);
    }
    assert_eq!(OWNER_BANK, sim.world().get::<FacilityTable>().owner[f]);
}

#[test]
fn seizure_takes_only_the_deposit_coins_the_bank_holds() {
    let mut sim = build_sample(QUIET);
    place_at(&mut sim, CITY, 0);
    own_city_facility(&mut sim);
    borrow(&mut sim, 3_000);
    run(&mut sim);
    place_at(&mut sim, VILLAGE, 0);
    let abroad = deposit_abroad(&mut sim, 1_000, Some(300)); // it lent the rest
    let grace = sim.context().services.database.config.loan_grace_weeks;
    let mut seized = Vec::new();
    for _ in 0..=grace + 1 {
        seized = of::<CollateralSeized>(&run(&mut sim));
        if !seized.is_empty() {
            break;
        }
    }
    assert_eq!(CollateralKind::Deposit, seized[0].kind);
    let bank = sim.world().get::<BankState>();
    assert_eq!(0, bank.reserve[abroad]);
    assert!(bank.player_deposit[abroad] >= 700);
}

#[test]
fn winning_bid_is_not_paid_from_deposit_coins_the_bank_lacks() {
    let mut sim = build_sample(QUIET);
    place_at(&mut sim, CITY, 0);
    let f = own_city_facility(&mut sim);
    sim.context_mut().world.get_mut::<FacilityTable>().owner[f] = OWNER_BANK;
    let home = nation_of(&sim, CITY);
    let lender = nation_of(&sim, ABROAD);
    run(&mut sim);
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.player_deposit[home] = 1_000;
    bank.reserve[home] = 300;
    let turn = sim.turn();
    add_auction(&mut sim, f, lender, turn, 800, false); // the sale's coins go abroad
    sim.context_mut().world.get_mut::<AuctionTable>().player_bid[0] = 800;
    let events = run(&mut sim);
    assert!(!of::<AuctionClosed>(&events)[0].won_by_player);
    assert!(sim.world().get::<BankState>().reserve[home] >= 0);
}

/// Opens the income-tax books, which `agents.income_tax` does on its first run.
fn open_books(sim: &mut Simulation) {
    let world = &mut sim.context_mut().world;
    let facilities = world.get::<FacilityTable>().cash.clone();
    let merchants = world.get::<AgentTable>().cash.clone();
    let books = world.get_mut::<IncomeTaxState>();
    books.facility_start = facilities;
    books.facility_dividends = vec![0; books.facility_start.len()];
    books.merchant_start = merchants;
}

#[test]
fn income_tax_does_not_count_a_facilitys_loan_or_its_repayment() {
    let mut sim = build_sample(&[]);
    open_books(&mut sim);
    run(&mut sim);
    let r = refs(&sim);
    let facilities = sim.world().get::<FacilityTable>();
    let loans = sim.world().get::<NpcLoanTable>();
    let f = (0..facilities.count())
        .find(|&i| {
            facilities.owner[i] == OWNER_NPC
                && loans.find(i as i32).is_none()
                && r.banks.nation_of(r.nodes, facilities.node[i]) >= 0
        })
        .expect("an NPC facility");
    sim.context_mut().world.get_mut::<FacilityTable>().cash[f] = -100;
    let profit = |sim: &Simulation| {
        sim.world().get::<FacilityTable>().cash[f]
            - sim.world().get::<IncomeTaxState>().facility_start[f]
    };
    let before = profit(&sim);
    run(&mut sim);
    assert!(sim.world().get::<NpcLoanTable>().find(f as i32).is_some());
    assert!(
        sim.world().get::<FacilityTable>().cash[f] > 0,
        "it borrowed"
    );
    assert_eq!(before, profit(&sim), "a loan is no profit");

    sim.context_mut().world.get_mut::<FacilityTable>().cash[f] += 100_000;
    let cash = sim.world().get::<FacilityTable>().cash[f];
    let before = profit(&sim);
    run(&mut sim);
    assert!(
        sim.world().get::<FacilityTable>().cash[f] < cash,
        "it repaid"
    );
    assert_eq!(before, profit(&sim), "a repayment is no loss");
}

#[test]
fn income_tax_does_not_count_a_merchants_loan_or_its_repayment() {
    let mut sim = build_sample(&[]);
    open_books(&mut sim);
    run(&mut sim);
    let wheat = sim
        .context()
        .services
        .database
        .try_get_good("wheat", QualityGrade::Mid)
        .expect("wheat");
    let agents = sim.world().get::<AgentTable>();
    let loans = sim.world().get::<NpcLoanTable>();
    let a = (0..agents.count())
        .find(|&i| {
            agents.owner[i] == OWNER_NPC && loans.find(merchant_borrower(i as i32)).is_none()
        })
        .expect("an NPC merchant");
    let world = &mut sim.context_mut().world;
    let agents = world.get_mut::<AgentTable>();
    agents.node[a] = agents.home[a];
    agents.cash[a] = -100;
    world
        .get_mut::<CargoTable>()
        .change(merchant_holder(a as i32), wheat.0, 1_000); // collateral
    let profit = |sim: &Simulation| {
        sim.world().get::<AgentTable>().cash[a]
            - sim.world().get::<IncomeTaxState>().merchant_start[a]
    };
    let before = profit(&sim);
    run(&mut sim);
    let borrower = merchant_borrower(a as i32);
    assert!(sim.world().get::<NpcLoanTable>().find(borrower).is_some());
    assert!(
        sim.world().get::<AgentTable>().cash[a] > -100,
        "it borrowed"
    );
    assert_eq!(before, profit(&sim), "a loan is no profit");

    sim.context_mut().world.get_mut::<AgentTable>().cash[a] += 100_000;
    let cash = sim.world().get::<AgentTable>().cash[a];
    let before = profit(&sim);
    run(&mut sim);
    assert!(sim.world().get::<AgentTable>().cash[a] < cash, "it repaid");
    assert_eq!(before, profit(&sim), "a repayment is no loss");
}

const PLOTS: i32 = 5;

/// C# `MarginTests.GroupFacility`.
fn group_facility(sim: &Simulation) -> usize {
    let r = refs(sim);
    let facilities = sim.world().get::<FacilityTable>();
    (0..facilities.count())
        .find(|&f| {
            facilities.owner[f] == OWNER_NPC
                && facilities.group[f] >= 0
                && facilities.level[f] > 0
                && !r.db.facilities[facilities.def_index[f] as usize].state_owned
                && r.banks.nation_of(r.nodes, facilities.node[f]) >= 0
        })
        .expect("a group facility")
}

/// C# `MarginTests.CityBranch`: the branch with the most land for sale.
fn city_branch(sim: &Simulation) -> usize {
    let r = refs(sim);
    let land = sim.world().get::<LandState>();
    let mut best: Option<usize> = None;
    for n in 0..r.nodes.count() {
        if r.banks.branch_nation(r.nodes, n as i32) < 0 {
            continue;
        }
        if best.is_none_or(|b| land.free(n) > land.free(b)) {
            best = Some(n);
        }
    }
    best.expect("a branch")
}

/// C# `CheapenLand`: plots at the node until their price is at most `permille` of what it was.
fn cheapen_land(sim: &mut Simulation, n: usize, permille: i64) {
    let price = |sim: &Simulation| {
        let r = refs(sim);
        land_price(sim.world(), r.nodes, &r.db.config, n)
    };
    let target = price(sim) * permille / 1000;
    while price(sim) > target {
        sim.context_mut().world.get_mut::<LandState>().plots[n] += 1;
    }
}

fn with_land_loan(sim: &mut Simulation, n: usize, share: i64) -> i32 {
    let c = continent_of(sim, n as i32);
    sim.context_mut().world.get_mut::<PlayerState>().node = n as i32;
    sim.context_mut().world.get_mut::<LandState>().player_plots[n] = PLOTS;
    let limit = credit_limit(sim.world(), refs(sim), c);
    borrow(sim, limit / share);
    c
}

#[test]
fn shares_and_land_are_pledged_at_their_price() {
    let mut sim = build_sample(&[]);
    let f = group_facility(&sim);
    let n = city_branch(&sim);
    let c = continent_of(&sim, n as i32);
    let before = collateral_value(sim.world(), refs(&sim), c);
    let world = &mut sim.context_mut().world;
    world
        .get_mut::<ShareTable>()
        .add(f as i32, PLAYER_HOLDER, 10);
    world.get_mut::<LandState>().player_plots[n] += 2;
    let r = refs(&sim);
    let fc = continent_of(&sim, sim.world().get::<FacilityTable>().node[f]);
    let shares = convert(sim.world(), 10 * share_price(sim.world(), r, f), fc, c);
    let land = 2 * land_price(sim.world(), r.nodes, &r.db.config, n);
    assert_eq!(before + shares + land, collateral_value(sim.world(), r, c));
}

#[test]
fn falling_land_calls_for_margin_then_is_sold() {
    let mut sim = build_sample(&[]);
    let n = city_branch(&sim);
    let c = with_land_loan(&mut sim, n, 1);
    cheapen_land(&mut sim, n, 650);
    let plots = |sim: &Simulation| sim.world().get::<LandState>().player_plots[n];

    let events = run(&mut sim);
    assert_eq!(1, of::<MarginCalled>(&events).len());
    assert_eq!(PLOTS, plots(&sim), "a call first, no sale");
    let weeks = sim.context().services.database.config.margin_call_weeks;
    for t in 1..weeks {
        run(&mut sim);
        assert_eq!(PLOTS, plots(&sim), "week {t} of the call");
    }
    let owed = debt(sim.world(), refs(&sim), c);
    let events = run(&mut sim);
    assert!(plots(&sim) < PLOTS, "the bank sold land");
    assert!(plots(&sim) > 0, "only what brings the debt back");
    let r = refs(&sim);
    assert!(debt(sim.world(), r, c) < owed);
    let ltv = r.db.config.loan_to_value;
    assert!(debt(sim.world(), r, c) <= ltv.mul_int(collateral_value(sim.world(), r, c)));
    let seized = of::<CollateralSeized>(&events);
    assert!(seized.iter().any(|e| e.kind == CollateralKind::Land));
}

#[test]
fn falling_shares_are_sold_to_the_group() {
    let mut sim = build_sample(&[]);
    let f = group_facility(&sim);
    let r = refs(&sim);
    let nation = r
        .banks
        .nation_of(r.nodes, sim.world().get::<FacilityTable>().node[f]);
    let branch = (0..r.nodes.count() as i32)
        .find(|&x| r.banks.branch_nation(r.nodes, x) == nation)
        .expect("a branch");
    let c = continent_of(&sim, branch);
    let world = &mut sim.context_mut().world;
    world.get_mut::<PlayerState>().node = branch;
    world
        .get_mut::<ShareTable>()
        .add(f as i32, PLAYER_HOLDER, 40);
    world.get_mut::<FacilityTable>().level[f] += 4; // dearer for the loan, then back
    let limit = credit_limit(sim.world(), refs(&sim), c);
    borrow(&mut sim, limit);
    let world = &mut sim.context_mut().world;
    world.get_mut::<FacilityTable>().level[f] -= 4;
    let group = world.get::<FacilityTable>().group[f] as usize;
    world.get_mut::<GroupTable>().cash[group] += 1_000_000_000;

    let weeks = sim.context().services.database.config.margin_call_weeks;
    let mut events = Vec::new();
    for _ in 0..=weeks {
        events = run(&mut sim);
    }
    let held = sim
        .world()
        .get::<ShareTable>()
        .held(f as i32, PLAYER_HOLDER);
    assert!(held < 40);
    let seized = of::<CollateralSeized>(&events);
    assert!(seized
        .iter()
        .any(|e| e.kind == CollateralKind::Shares && e.facility == f as i32));
}

#[test]
fn an_overdue_loan_takes_shares_and_land() {
    let mut sim = build_sample(&[]);
    let n = city_branch(&sim);
    with_land_loan(&mut sim, n, 2);
    let continent = continent_of(&sim, n as i32) as usize;
    sim.context_mut().world.get_mut::<PlayerState>().coins[continent] = 0;
    let grace = sim.context().services.database.config.loan_grace_weeks;
    let mut seized = Vec::new();
    for _ in 0..grace + 3 {
        seized.extend(of::<CollateralSeized>(&run(&mut sim)));
    }
    assert!(seized.iter().any(|e| e.kind == CollateralKind::Land));
    assert!(sim.world().get::<LandState>().player_plots[n] < PLOTS);
}

#[test]
fn margin_sales_keep_the_coins() {
    let mut sim = build_sample(&[]);
    let n = city_branch(&sim);
    with_land_loan(&mut sim, n, 1);
    cheapen_land(&mut sim, n, 650);
    let weeks = sim.context().services.database.config.margin_call_weeks;
    for t in 0..=weeks + 1 {
        run(&mut sim);
        for k in 0..continent_count(&sim) {
            let supply = sim.world().get::<CurrencyState>().coin_supply[k];
            assert_eq!(supply, continent_money(&sim, k), "turn {t}");
        }
    }
}

#[test]
fn paying_down_lifts_the_call() {
    let mut sim = build_sample(&[]);
    let n = city_branch(&sim);
    with_land_loan(&mut sim, n, 1);
    cheapen_land(&mut sim, n, 650);
    assert_eq!(1, of::<MarginCalled>(&run(&mut sim)).len());
    let balance = sim.world().get::<LoanTable>().balance[0];
    let continent = continent_of(&sim, n as i32) as usize;
    sim.context_mut().world.get_mut::<PlayerState>().coins[continent] += balance;
    let repay = RepayLoan {
        amount: balance / 2,
    };
    assert_eq!(CommandResult::Ok, repay.validate(sim.context()));
    repay.apply(sim.context_mut());
    let weeks = sim.context().services.database.config.margin_call_weeks;
    for _ in 0..weeks + 2 {
        run(&mut sim);
    }
    assert_eq!(PLOTS, sim.world().get::<LandState>().player_plots[n]);
}
