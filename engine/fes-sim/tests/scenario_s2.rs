//! Scenario 2 "Shares and Bonds" (spec 5.4) played on the full-scale world: the golden path (buy
//! the workshop before its orders, sell on the rumour, buy the nation's bonds), the passive path,
//! the holder who stays through the losses, and the dividend and coupon presentations.
#![cfg(feature = "scenario")]
mod scenario_play;

use fes_sim::commands::bonds::BuyBond;
use fes_sim::commands::securities::{BuyShare, SellShare};
use fes_sim::{SimEventRecord as E, Simulation};
use scenario_play::*;

const WORKSHOP: i32 = 130;
const NATION: i32 = -1;

fn s2() -> Simulation {
    start(def("s2_shares_and_bonds"))
}

/// Presses Next week until week `w` has ended.
fn until(sim: &mut Simulation, w: i32, all: &mut Vec<E>) {
    while statement(sim).weeks < w {
        all.extend(week(sim));
    }
}

fn buy_workshop(sim: &mut Simulation) {
    order(
        sim,
        Box::new(BuyShare {
            facility: WORKSHOP,
            count: 49,
        }),
    );
}

fn buy_bonds(sim: &mut Simulation) -> i64 {
    let amount = coins(sim) / 100 * 100;
    order(
        sim,
        Box::new(BuyBond {
            issuer: NATION,
            amount,
        }),
    );
    amount
}

/// The presentation payment of a scripted event: its `coins`.
fn paid(events: &[E], id: &str) -> i64 {
    dialogs(events)
        .into_iter()
        .find(|d| d.0 == id)
        .map_or(0, |d| d.1)
}

#[test]
fn golden_path_profits_from_the_orders_the_dividend_and_the_coupon() {
    let mut sim = s2();
    assert_eq!(coins(&sim), 10_000);
    let mut ev = Vec::new();
    buy_workshop(&mut sim);
    until(&mut sim, 7, &mut ev);
    assert!(dialogs(&ev).iter().any(|d| d.0 == "rumour"));
    order(
        &mut sim,
        Box::new(SellShare {
            facility: WORKSHOP,
            count: 49,
        }),
    );
    until(&mut sim, 9, &mut ev);
    let face = buy_bonds(&mut sim);
    assert!(face >= 10_000, "face {face}");
    ev.extend(finish(&mut sim));
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!(paid(&ev, "dividend"), 49 * 10, "dividend presentation");
    // 8% of the face still held in week 17 (the weekly payments repay a little principal).
    let coupon = paid(&ev, "coupon");
    assert!(
        coupon > face * 79 / 1000 && coupon <= face * 8 / 100,
        "coupon {coupon}"
    );
    assert_eq!(s.weeks, 22);
    assert!(s.net_profit >= 3250, "golden must clear 1.3x target: {s:?}");
    assert_eq!(ended(&ev), Some((s.net_profit, 2)));
    println!("s2 golden: {s:?}");
}

#[test]
fn scheduled_decisions_are_never_more_than_three_weeks_apart() {
    use fes_sim::scenario::Trigger;
    let def = def("s2_shares_and_bonds");
    let mut weeks = vec![0, def.weeks];
    for event in &def.events {
        let week = match event.trigger {
            Trigger::Week(w) => w,
            _ => event.fallback_week.expect("every event needs a deadline"),
        };
        weeks.push(week);
    }
    weeks.sort();
    for pair in weeks.windows(2) {
        assert!(pair[1] - pair[0] <= 3, "quiet gap: {pair:?}");
    }
}

#[test]
fn holding_through_the_losses_gives_back_the_gain() {
    let mut sim = s2();
    let mut ev = Vec::new();
    buy_workshop(&mut sim);
    until(&mut sim, 7, &mut ev);
    let before = statement(&sim).net_profit;
    until(&mut sim, 8, &mut ev);
    let after = statement(&sim).net_profit;
    assert!(after < before - 2000, "losses: {before} -> {after}");
    assert_honest(&statement(&sim));
}

#[test]
fn ignoring_everything_ends_with_an_honest_statement() {
    let mut sim = s2();
    let ev = finish(&mut sim);
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!(
        (s.weeks, s.net_profit, s.revenue, s.holdings),
        (22, 0, 0, 0)
    );
    assert_eq!(ended(&ev), Some((0, 0)));
    assert_eq!(paid(&ev, "dividend"), 0, "no shares, no dividend");
    assert_eq!(paid(&ev, "coupon"), 0, "no bonds, no coupon");
    for id in ["review_bonds", "coupon_choice"] {
        assert!(dialogs(&ev).iter().any(|d| d.0 == id), "missing {id}");
    }
}
