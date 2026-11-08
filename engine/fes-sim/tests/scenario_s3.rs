//! Scenario 3 "Licences and Trade Abroad" (spec 5.5) played on the full-scale world: the golden
//! path (trade licence, insured and guarded trip, the foreign licence, sales, home in time), the
//! passive path, and the refusals without either licence.
#![cfg(feature = "scenario")]
mod scenario_play;

use fes_sim::commands::licenses::{BuyLicense, BuyTradeLicense};
use fes_sim::commands::trade::SellGoods;
use fes_sim::commands::travel::Travel;
use fes_sim::commands::CommandResult;
use fes_sim::{SimEventRecord as E, Simulation};
use scenario_play::*;

const HOME: &str = "k0n0r0";
const INTERNATIONAL: i32 = -1;

fn s3() -> Simulation {
    start(def("s3_licences_abroad"))
}

fn hop(sim: &mut Simulation, to: &str, protected: bool, ev: &mut Vec<E>) {
    go(sim, to, protected, protected);
    ev.extend(week(sim));
    arrive(sim, ev);
}

/// Trade licence and 60 g19_0 bought at home, then the trip to k0n1.
fn abroad(sim: &mut Simulation, protected: bool, ev: &mut Vec<E>) -> i32 {
    let g = good(sim, "g19_0");
    order(
        sim,
        Box::new(BuyTradeLicense {
            kind: INTERNATIONAL,
        }),
    );
    buy(sim, g, 60);
    ev.extend(week(sim));
    for to in ["k0n0", "k0", "k0n1"] {
        hop(sim, to, protected, ev);
    }
    g
}

fn refused(r: CommandResult) -> String {
    match r {
        CommandResult::Error(e) => e,
        CommandResult::Ok => "accepted".into(),
    }
}

#[test]
fn golden_path_sells_abroad_under_licence_and_returns_in_time() {
    let mut sim = s3();
    assert_eq!(coins(&sim), 30_000);
    let mut ev = Vec::new();
    let g = abroad(&mut sim, true, &mut ev);
    assert_eq!(held(&sim, g), 60, "guards beat the raid off");
    order(&mut sim, Box::new(BuyLicense { good: g }));
    while held(&sim, g) > 0 {
        let left = held(&sim, g);
        sell(&mut sim, g, left);
        ev.extend(week(&mut sim));
    }
    let sales = ev
        .iter()
        .filter(|e| matches!(e, E::GoodsTraded(t) if !t.bought))
        .count();
    assert!(sales >= 2, "the thin purses buy over weeks");
    for to in ["k0", "k0n0", HOME] {
        hop(&mut sim, to, false, &mut ev);
    }
    let home_week = statement(&sim).weeks;
    assert!(home_week <= 24, "home in week {home_week}");
    ev.extend(finish(&mut sim));
    let s = statement(&sim);
    assert_honest(&s);
    assert!(s.net_profit >= 1300, "golden must clear 1.3x target: {s:?}");
    assert!(s.costs.other >= 300 + 2000, "licences are costs: {s:?}");
    assert_eq!(ended(&ev), Some((s.net_profit, 3)));
    println!("s3 golden: home in week {home_week}, {s:?}");
}

#[test]
fn without_the_trade_licence_cargo_may_not_leave_the_nation() {
    let mut sim = s3();
    let g = good(&sim, "g19_0");
    buy(&mut sim, g, 60);
    week(&mut sim);
    let to = node(&sim, "k0n0");
    let leave = Travel {
        destination: to,
        guards: false,
        insurance: false,
    };
    assert_eq!(
        refused(sim.preview(&leave)),
        "accepted",
        "k0n0 is still home"
    );
    go(&mut sim, "k0n0", false, false);
    let mut ev = Vec::new();
    week(&mut sim);
    arrive(&mut sim, &mut ev);
    let out = Travel {
        destination: node(&sim, "k0"),
        guards: false,
        insurance: false,
    };
    assert_eq!(
        refused(sim.preview(&out)),
        "Carrying cargo out of this nation needs its trade license."
    );
}

#[test]
fn without_the_foreign_licence_the_sale_is_refused() {
    let mut sim = s3();
    let mut ev = Vec::new();
    let g = abroad(&mut sim, true, &mut ev);
    let sale = SellGoods {
        good: g,
        quantity: 10,
    };
    assert_eq!(
        refused(sim.preview(&sale)),
        "Selling this good here needs this nation's license."
    );
}

#[test]
fn ignoring_everything_ends_with_an_honest_statement() {
    let mut sim = s3();
    let ev = finish(&mut sim);
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!((s.weeks, s.net_profit, s.revenue), (24, 0, 0));
    assert_eq!(ended(&ev), Some((0, 0)));
    assert!(!dialogs(&ev).iter().any(|d| d.0 == "abroad"));
}
