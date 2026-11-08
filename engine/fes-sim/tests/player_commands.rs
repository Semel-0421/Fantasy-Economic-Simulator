//! Player commands (C# `PlayerRuleTests`, `LandTests`, `FacilityRepurposeTests`,
//! `TradeLicenseTests`, `MarginTests` and the command cases of `SimulationTests`).
mod common;

use common::build_sample;
use fes_core::{GuidanceAxis, QualityGrade, WorldLevel};
use fes_sim::agents::asset_prices::{asking_price, land_price, Refs};
use fes_sim::agents::balance_sheet::credit_limit;
use fes_sim::agents::banks::{AuctionTable, BankState, LoanTable};
use fes_sim::agents::cargo::{CargoTable, PLAYER_CART};
use fes_sim::agents::facilities::{FacilityTable, OWNER_NPC, OWNER_PLAYER};
use fes_sim::agents::finance::{event_kind, FinanceState};
use fes_sim::agents::groups::GroupTable;
use fes_sim::agents::guidance::GuidanceState;
use fes_sim::agents::investment::{may_stand, repurpose_cost};
use fes_sim::agents::land::LandState;
use fes_sim::agents::licenses::{LicenseTable, TRADE_LICENSE_INTERCONTINENTAL};
use fes_sim::agents::loans::NpcLoanTable;
use fes_sim::agents::player::PlayerState;
use fes_sim::agents::ship::PlayerShip;
use fes_sim::commands::bank::{Bid, RepayLoan, TakeLoan};
use fes_sim::commands::facility::{
    BuyFacility, FoundCompany, RepurposeFacility, SetFacilityControl,
};
use fes_sim::commands::licenses::BuyTradeLicense;
use fes_sim::commands::places::{BuyPlot, SellPlot};
use fes_sim::commands::trade::BuyGoods;
use fes_sim::commands::travel::cargo_may_leave;
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{
    views, Command, CommandApplied, CommandRejected, CommandResult, PlayerCommand, Simulation,
};
use std::sync::Arc;

const PLENTY: i64 = 1_000_000_000;

fn error(cmd: &dyn Command, sim: &Simulation) -> Option<String> {
    match cmd.validate(sim.context()) {
        CommandResult::Ok => None,
        CommandResult::Error(e) => Some(e),
    }
}

fn ok(cmd: &dyn Command, sim: &Simulation) -> bool {
    error(cmd, sim).is_none()
}

fn nodes(sim: &Simulation) -> NodeTable {
    sim.context().services.get::<NodeTable>().clone()
}

fn player(sim: &mut Simulation) -> &mut PlayerState {
    sim.context_mut().world.get_mut::<PlayerState>()
}

fn coins_at(sim: &mut Simulation, node: i32) -> &mut i64 {
    let c = nodes(sim).continent[node as usize] as usize;
    &mut player(sim).coins[c]
}

fn for_sale(sim: &Simulation, state_owned: bool) -> usize {
    let f = sim.world().get::<FacilityTable>();
    let db = &sim.context().services.database;
    (0..f.count())
        .find(|&i| {
            f.owner[i] == OWNER_NPC
                && db.facilities[f.def_index[i] as usize].state_owned == state_owned
        })
        .expect("a facility")
}

fn elsewhere(sim: &Simulation, node: i32) -> i32 {
    if node == 0 { 1 } else { 0 }.min(nodes(sim).count() as i32 - 1)
}

fn asked(sim: &Simulation, f: usize) -> i64 {
    let ctx = sim.context();
    let r = Refs {
        db: &ctx.services.database,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    };
    asking_price(&ctx.world, r, f)
}

fn offer(sim: &mut Simulation, f: usize, axis: u8) -> BuyFacility {
    let price = asked(sim, f);
    let node = sim.world().get::<FacilityTable>().node[f];
    *coins_at(sim, node) += price;
    BuyFacility {
        facility: f as i32,
        price,
        axis,
    }
}

#[test]
fn a_facility_is_bought_standing_at_it() {
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, false);
    let node = sim.world().get::<FacilityTable>().node[f];
    let buy = offer(&mut sim, f, GuidanceAxis::Trade as u8);
    player(&mut sim).node = elsewhere(&sim, node);
    assert!(!ok(&buy, &sim));
    player(&mut sim).node = node;
    assert!(ok(&buy, &sim));
    player(&mut sim).travel_destination = elsewhere(&sim, node);
    assert!(!ok(&buy, &sim), "not while travelling");
}

#[test]
fn the_state_does_not_sell_its_facilities() {
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, true);
    player(&mut sim).node = sim.world().get::<FacilityTable>().node[f];
    let buy = offer(&mut sim, f, 0);
    assert_eq!(
        error(&buy, &sim).as_deref(),
        Some("The state does not sell this facility.")
    );
}

#[test]
fn a_bid_is_made_standing_at_the_facility() {
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, false);
    let node = sim.world().get::<FacilityTable>().node[f];
    let turn = sim.turn();
    let a = sim.context_mut().world.get_mut::<AuctionTable>();
    a.facility.push(f as i32);
    a.nation.push(0);
    a.closes_turn.push(turn + 10);
    a.reserve_price.push(1);
    a.player_bid.push(0);
    a.for_player_loan.push(0);
    let bid = Bid {
        facility: f as i32,
        amount: 5,
    };
    player(&mut sim).node = elsewhere(&sim, node);
    assert!(!ok(&bid, &sim));
    player(&mut sim).node = node;
    assert!(ok(&bid, &sim));
}

#[test]
fn a_company_is_founded_standing_at_its_headquarters() {
    let mut sim = build_sample(&[]);
    let node = player(&mut sim).node;
    *coins_at(&mut sim, node) += 100;
    let found = FoundCompany {
        headquarters: node,
        focus: GuidanceAxis::Trade as u8,
        cost: 100,
    };
    assert!(ok(&found, &sim));
    player(&mut sim).node = elsewhere(&sim, node);
    assert!(!ok(&found, &sim));
}

#[test]
fn arguments_outside_their_range_are_refused() {
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, false);
    player(&mut sim).node = sim.world().get::<FacilityTable>().node[f];
    let buy = offer(&mut sim, f, 99);
    assert_eq!(error(&buy, &sim).as_deref(), Some("Unknown guidance axis."));
    let node = player(&mut sim).node;
    *coins_at(&mut sim, node) += 100;
    let found = FoundCompany {
        headquarters: node,
        focus: 99,
        cost: 100,
    };
    assert!(!ok(&found, &sim));
    sim.context_mut().world.get_mut::<FacilityTable>().owner[f] = OWNER_PLAYER;
    let bad = SetFacilityControl {
        facility: f as i32,
        mode: 9,
    };
    assert!(!ok(&bad, &sim));
    let good = SetFacilityControl {
        facility: f as i32,
        mode: 1,
    };
    assert!(ok(&good, &sim));
}

/// The player at a branch with a market, a deposit and no coins; returns (good, nation).
fn at_a_branch_with_deposit(sim: &mut Simulation, deposit: i64) -> (i32, usize) {
    let n = nodes(sim);
    let banks = sim.context().services.get::<NodeBanks>().clone();
    let node = {
        let m = sim.world().get::<MarketStore>();
        (0..n.count() as i32)
            .find(|&x| banks.branch_nation(&n, x) >= 0 && m.market_cell_count[x as usize] > 0)
            .expect("a branch with a market")
    };
    let nation = banks.branch_nation(&n, node) as usize;
    let world = &mut sim.context_mut().world;
    let bank = world.get_mut::<BankState>();
    bank.player_deposit[nation] = deposit;
    bank.reserve[nation] += deposit;
    let m = world.get_mut::<MarketStore>();
    let cell = m.market_cell_start[node as usize] as usize;
    m.stock[cell] = 1_000_000;
    m.price[cell] = 1;
    let good = m.cell_good[cell];
    let p = world.get_mut::<PlayerState>();
    p.node = node;
    p.coins.iter_mut().for_each(|c| *c = 0);
    (good, nation)
}

#[test]
fn buying_from_a_deposit_keeps_the_bank_run_limit() {
    let mut sim = build_sample(&[]);
    let (good, nation) = at_a_branch_with_deposit(&mut sim, 100);
    sim.context_mut().world.get_mut::<FinanceState>().event[nation] = event_kind::BANK_RUN;
    let config = &mut Arc::make_mut(&mut sim.context_mut().services.database).config;
    config.cart_max_weight = i64::MAX / 4;
    config.cart_max_size = i64::MAX / 4;
    let cap = config.bank_run_withdraw_share.mul_int(100);
    let buy = BuyGoods {
        good,
        quantity: 100,
        pay_from_deposit: true,
    };
    assert!(ok(&buy, &sim));
    buy.apply(sim.context_mut());
    assert_eq!(
        100 - cap,
        sim.world().get::<BankState>().player_deposit[nation]
    );
    let again = BuyGoods {
        good,
        quantity: 1,
        pay_from_deposit: true,
    };
    assert!(!ok(&again, &sim), "once a week");
}

#[test]
fn a_docked_ship_takes_more_than_the_cart() {
    let mut sim = build_sample(&[]);
    let (good, _) = at_a_branch_with_deposit(&mut sim, 0);
    let node = player(&mut sim).node;
    *coins_at(&mut sim, node) = 1_000_000;
    let config = sim.context().services.database.config.clone();
    let (weight, size) = {
        let m = sim.world().get::<MarketStore>();
        (
            i64::from(m.good_weight[good as usize]).max(1),
            i64::from(m.good_size[good as usize]).max(1),
        )
    };
    let cart_fits = (config.cart_max_weight / weight).min(config.cart_max_size / size) as i32;
    let buy = BuyGoods {
        good,
        quantity: cart_fits + 10,
        pay_from_deposit: false,
    };
    buy.apply(sim.context_mut());
    let held = |sim: &Simulation| {
        sim.world()
            .get::<CargoTable>()
            .quantity_of(PLAYER_CART, good)
    };
    assert_eq!(cart_fits, held(&sim));
    let ship = sim.context_mut().world.get_mut::<PlayerShip>();
    ship.owned = true;
    ship.docked_at = node;
    buy.apply(sim.context_mut());
    assert_eq!(2 * cart_fits + 10, held(&sim));
}

fn village(sim: &Simulation) -> i32 {
    let n = nodes(sim);
    (0..n.count() as i32)
        .find(|&x| n.level_of(x) == WorldLevel::Village)
        .expect("a village")
}

#[test]
fn the_player_buys_a_plot_and_sells_it_back_at_the_price() {
    let mut sim = build_sample(&[]);
    let n = village(&sim);
    player(&mut sim).node = n;
    *coins_at(&mut sim, n) = PLENTY;
    sim.context_mut().world.get_mut::<HouseholdCash>().cash[n as usize] += PLENTY;
    let price_now = |sim: &Simulation| {
        let ctx = sim.context();
        land_price(
            &ctx.world,
            ctx.services.get::<NodeTable>(),
            &ctx.services.database.config,
            n as usize,
        )
    };
    let price = price_now(&sim);
    let cash = sim.world().get::<HouseholdCash>().cash[n as usize];
    assert_eq!(None, error(&BuyPlot, &sim));
    BuyPlot.apply(sim.context_mut());
    assert_eq!(1, sim.world().get::<LandState>().player_plots[n as usize]);
    assert_eq!(PLENTY - price, *coins_at(&mut sim, n));
    assert_eq!(
        cash + price,
        sim.world().get::<HouseholdCash>().cash[n as usize]
    );
    let back = price_now(&sim);
    assert_eq!(None, error(&SellPlot, &sim));
    SellPlot.apply(sim.context_mut());
    assert_eq!(0, sim.world().get::<LandState>().player_plots[n as usize]);
    assert_eq!(PLENTY - price + back, *coins_at(&mut sim, n));
    assert!(error(&SellPlot, &sim).is_some(), "nothing left to sell");
}

#[test]
fn no_free_plot_cannot_be_bought() {
    let mut sim = build_sample(&[]);
    let n = village(&sim);
    player(&mut sim).node = n;
    *coins_at(&mut sim, n) = PLENTY;
    let land = sim.context_mut().world.get_mut::<LandState>();
    land.player_plots[n as usize] = land.free(n as usize);
    assert_eq!(
        Some("No land is for sale here."),
        error(&BuyPlot, &sim).as_deref()
    );
}

fn npc_facility(sim: &Simulation) -> usize {
    let f = sim.world().get::<FacilityTable>();
    let loans = sim.world().get::<NpcLoanTable>();
    let db = &sim.context().services.database;
    (0..f.count())
        .find(|&i| {
            f.owner[i] == OWNER_NPC
                && f.level[i] > 0
                && loans.find(i as i32).is_none()
                && !db.facilities[f.def_index[i] as usize].state_owned
        })
        .expect("an NPC facility")
}

fn other_use(sim: &Simulation, f: usize) -> usize {
    let t = sim.world().get::<FacilityTable>();
    let db = &sim.context().services.database;
    let n = nodes(sim);
    db.facilities
        .iter()
        .position(|d| {
            d.index != t.def_index[f]
                && may_stand(d, &n, t.node[f])
                && !db.recipes[d.recipe].outputs.is_empty()
        })
        .expect("another use")
}

#[test]
fn the_new_owner_changes_its_use_at_level_one_and_pays_the_builders() {
    let mut sim = build_sample(&[]);
    let f = npc_facility(&sim);
    let target = other_use(&sim, f);
    let node = {
        let t = sim.context_mut().world.get_mut::<FacilityTable>();
        t.owner[f] = OWNER_PLAYER;
        t.repurposable[f] = 1;
        t.level[f] = 3;
        t.node[f]
    };
    player(&mut sim).node = node;
    *coins_at(&mut sim, node) = PLENTY;
    let db = Arc::clone(&sim.context().services.database);
    let def = &db.facilities[target];
    let command = RepurposeFacility {
        facility: f as i32,
        def_key: def.key.clone(),
    };
    assert_eq!(None, error(&command, &sim));
    command.apply(sim.context_mut());
    let t = sim.world().get::<FacilityTable>();
    assert_eq!(def.index, t.def_index[f]);
    assert_eq!(1, t.level[f], "new equipment starts small");
    assert_eq!(0, t.repurposable[f], "once");
    assert_eq!(
        PLENTY - repurpose_cost(def, &db.config),
        *coins_at(&mut sim, node)
    );
    let again = RepurposeFacility {
        facility: f as i32,
        def_key: db.facilities[0].key.clone(),
    };
    assert!(error(&again, &sim).is_some());
}

#[test]
fn a_facility_not_taken_over_cannot_change_its_use() {
    let mut sim = build_sample(&[]);
    let f = npc_facility(&sim);
    let target = other_use(&sim, f);
    let node = {
        let t = sim.context_mut().world.get_mut::<FacilityTable>();
        t.owner[f] = OWNER_PLAYER;
        t.node[f]
    };
    player(&mut sim).node = node;
    *coins_at(&mut sim, node) = PLENTY;
    let key = sim.context().services.database.facilities[target]
        .key
        .clone();
    let command = RepurposeFacility {
        facility: f as i32,
        def_key: key,
    };
    assert_eq!(
        Some("The facility may not change its use."),
        error(&command, &sim).as_deref()
    );
}

#[test]
fn cargo_leaves_a_nation_only_with_its_license() {
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let seat = (0..n.count() as i32)
        .find(|&x| n.ancestor_at(x, WorldLevel::Nation) >= 0 && n.is_seat(x))
        .expect("a seat");
    player(&mut sim).node = seat;
    let nation = n.ancestor_at(seat, WorldLevel::Nation);
    let to = (0..n.count() as i32)
        .find(|&x| {
            let other = n.ancestor_at(x, WorldLevel::Nation);
            other >= 0 && other != nation && n.continent[x as usize] != n.continent[seat as usize]
        })
        .expect("another continent's nation");
    assert!(
        cargo_may_leave(sim.context(), seat, to),
        "an empty cart crosses"
    );
    let wheat = sim
        .context()
        .services
        .database
        .try_get_good("wheat", QualityGrade::Mid)
        .expect("wheat");
    sim.context_mut()
        .world
        .get_mut::<CargoTable>()
        .change(PLAYER_CART, wheat.0, 1);
    assert!(!cargo_may_leave(sim.context(), seat, to));
    *coins_at(&mut sim, seat) += 100_000;
    let buy = BuyTradeLicense {
        kind: TRADE_LICENSE_INTERCONTINENTAL,
    };
    assert!(ok(&buy, &sim));
    buy.apply(sim.context_mut());
    assert!(cargo_may_leave(sim.context(), seat, to));
    let licences = views::licences(sim.context());
    assert_eq!(1, licences.len());
    assert_eq!(TRADE_LICENSE_INTERCONTINENTAL, licences[0].base_good);
    assert_eq!(1, sim.world().get::<LicenseTable>().count());
}

#[test]
fn a_license_pays_the_nation_without_making_coins() {
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let seat = (0..n.count() as i32)
        .find(|&x| n.ancestor_at(x, WorldLevel::Nation) >= 0 && n.is_seat(x))
        .expect("a seat");
    player(&mut sim).node = seat;
    *coins_at(&mut sim, seat) += 100_000;
    let continent = n.continent[seat as usize] as usize;
    let money = common::continent_money(&sim, continent);
    BuyTradeLicense {
        kind: fes_sim::agents::licenses::TRADE_LICENSE_INTERNATIONAL,
    }
    .apply(sim.context_mut());
    assert_eq!(money, common::continent_money(&sim, continent));
}

#[test]
fn a_loan_is_taken_up_to_the_credit_limit_and_repaid() {
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let banks = sim.context().services.get::<NodeBanks>().clone();
    let branch = (0..n.count() as i32)
        .filter(|&x| banks.branch_nation(&n, x) >= 0)
        .max_by_key(|&x| (sim.world().get::<LandState>().free(x as usize), -x))
        .expect("a branch");
    player(&mut sim).node = branch;
    sim.context_mut().world.get_mut::<LandState>().player_plots[branch as usize] = 20;
    let limit = {
        let ctx = sim.context();
        let r = Refs {
            db: &ctx.services.database,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
        };
        credit_limit(&ctx.world, r, n.continent[branch as usize])
    };
    assert!(limit > 0);
    assert_eq!(
        Some("Not enough collateral."),
        error(&TakeLoan { amount: limit + 1 }, &sim).as_deref()
    );
    let take = TakeLoan { amount: limit };
    assert_eq!(None, error(&take, &sim));
    take.apply(sim.context_mut());
    assert_eq!(
        Some("This bank's loan is not repaid yet."),
        error(&take, &sim).as_deref()
    );
    let balance = sim.world().get::<LoanTable>().balance[0];
    assert!(balance > limit, "interest is added up front");
    *coins_at(&mut sim, branch) += balance;
    let repay = RepayLoan {
        amount: balance / 2,
    };
    assert_eq!(None, error(&repay, &sim));
    repay.apply(sim.context_mut());
    assert_eq!(
        balance - balance / 2,
        sim.world().get::<LoanTable>().balance[0]
    );
    RepayLoan { amount: balance }.apply(sim.context_mut());
    assert_eq!(0, sim.world().get::<LoanTable>().count());
    assert_eq!(views::purse(sim.context()).loans.len(), 0);
}

#[test]
fn bought_facility_and_founded_company_guide_expansion() {
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, false);
    let node = sim.world().get::<FacilityTable>().node[f];
    player(&mut sim).node = node;
    let price = asked(&sim, f);
    *coins_at(&mut sim, node) += price + 300;
    sim.queue(Box::new(BuyFacility {
        facility: f as i32,
        price: 0,
        axis: GuidanceAxis::Industry as u8,
    }));
    sim.queue(
        PlayerCommand::BuyFacility {
            facility: f as i32,
            price,
            axis: GuidanceAxis::Industry as u8,
        }
        .into_command(),
    );
    sim.queue(
        PlayerCommand::FoundCompany {
            headquarters: node,
            focus: GuidanceAxis::Trade as u8,
            cost: 200,
        }
        .into_command(),
    );
    let report = sim.step().expect("step");
    let rejected = report
        .events
        .iter()
        .filter(|e| e.type_name.ends_with("CommandRejected"))
        .count();
    let applied = report
        .events
        .iter()
        .filter(|e| e.type_name.ends_with("CommandApplied"))
        .count();
    assert_eq!((1, 2), (rejected, applied));
    let t = sim.world().get::<FacilityTable>();
    assert_eq!(OWNER_PLAYER, t.owner[f]);
    let company = t.group[f];
    assert!(company >= 0);
    assert_eq!(
        1,
        sim.world().get::<GroupTable>().is_player[company as usize]
    );
    let guidance = sim.world().get::<GuidanceState>();
    assert!(guidance.weights[GuidanceAxis::Industry as usize] > 0);
    assert!(guidance.weights[GuidanceAxis::Trade as usize] > 0);
    assert_eq!(node, guidance.focus_node);
    let _ = (
        std::any::type_name::<CommandApplied>(),
        std::any::type_name::<CommandRejected>(),
    );
}

#[test]
fn player_commands_are_plain_data() {
    fn send<T: Send + Clone + 'static>() {}
    send::<PlayerCommand>();
    send::<views::PurseView>();
    send::<views::CargoView>();
    send::<views::SharesView>();
    send::<views::StatementView>();
    send::<views::ContractView>();
    send::<views::LicenceView>();
    let sim = build_sample(&[]);
    let cmd = PlayerCommand::BankTransfer { amount: 0 }.into_command();
    assert_eq!(
        Some("Amount must not be zero."),
        error(cmd.as_ref(), &sim).as_deref()
    );
    let purse = views::purse(sim.context());
    assert_eq!(purse.coins, sim.world().get::<PlayerState>().coins);
    assert!(views::cargo(sim.context()).cart.is_empty());
    let _ = views::statement(sim.context());
    let _ = views::shares(sim.context());
    let _ = views::contracts(sim.context());
}

#[test]
fn the_asking_price_follows_what_a_facility_earns() {
    use fes_sim::agents::production::lines::find_nearest_market;
    let mut sim = build_sample(&[]);
    let f = for_sale(&sim, false);
    let (cell, value, quantity) = {
        let ctx = sim.context();
        let t = ctx.world.get::<FacilityTable>();
        let db = &ctx.services.database;
        let output = db.recipes[db.facilities[t.def_index[f] as usize].recipe].outputs[0];
        let (_, cell) = find_nearest_market(
            ctx.services.get::<NodeTable>(),
            ctx.world.get::<MarketStore>(),
            t.node[f],
            output.good.0,
        )
        .expect("a market");
        let value = fes_sim::agents::asset_prices::facility_value(db, t, f) + t.cash[f].max(0);
        (cell, value, i64::from(output.quantity))
    };
    sim.context_mut().world.get_mut::<MarketStore>().price[cell] = 1;
    let poor = asked(&sim, f);
    sim.context_mut().world.get_mut::<MarketStore>().price[cell] = 1000;
    let rich = asked(&sim, f);
    assert!(poor >= value, "a seller never asks less than its value");
    assert!(rich > poor + 52 * 2 * quantity * 500);
}

#[test]
fn travel_sets_the_player_off_charges_the_trip_and_arrives() {
    use fes_sim::commands::travel::{find_route, try_quote, Travel};
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let from = n.try_find("aur_n1_r1_c1_v1").expect("village").0;
    let to = n.try_find("aur_n1_r1_c1").expect("city").0;
    player(&mut sim).node = from;
    player(&mut sim).coins.fill(0);
    let travel = Travel {
        destination: to,
        guards: false,
        insurance: false,
    };
    assert_eq!(
        Some("Unknown destination."),
        error(
            &Travel {
                destination: -1,
                ..travel.clone()
            },
            &sim
        )
        .as_deref()
    );
    assert_eq!(
        Some("Not enough coins for the trip."),
        error(&travel, &sim).as_deref()
    );
    *coins_at(&mut sim, from) = 10_000;
    let quote = try_quote(sim.context(), to, false, false).expect("a route");
    assert!(ok(&travel, &sim));
    travel.apply(sim.context_mut());
    let p = sim.world().get::<PlayerState>();
    let route = find_route(
        sim.context().services.get::<fes_sim::world::RouteTable>(),
        from,
        to,
    );
    let turns = sim
        .context()
        .services
        .get::<fes_sim::world::RouteTable>()
        .travel_turns[route as usize];
    assert_eq!(to, p.travel_destination);
    assert_eq!(sim.turn() + turns, p.arrival_turn);
    let paid = quote.departure_cost() + quote.toll;
    assert_eq!(10_000 - paid, p.coins[n.continent[from as usize] as usize]);
    assert_eq!(Some("Already travelling."), error(&travel, &sim).as_deref());
    for _ in 0..turns + 1 {
        sim.step().expect("step");
    }
    let p = sim.world().get::<PlayerState>();
    assert_eq!(to, p.node);
    assert!(!p.is_travelling());
}
