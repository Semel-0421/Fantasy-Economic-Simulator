//! Scenario 1 "First Venture" (spec 5.3) played on the full-scale world: the golden path to three
//! times the starting coins, the passive path, the protected opening, and the scripted raid met
//! with guards, with insurance only and with nothing.
#![cfg(feature = "scenario")]
mod scenario_play;

use fes_sim::commands::travel::{LOSS_CARGO, LOSS_NONE};
use fes_sim::{SimEventRecord as E, Simulation};
use scenario_play::*;

const VILLAGE: &str = "k0n0r0c0v1";
const TOWN: &str = "k0n0r0c0";
const REGION: &str = "k0n0r0";
const WORKSHOP: &str = "k0n0r0c1";

fn s1() -> Simulation {
    start(def("s1_first_venture"))
}

/// Grain from the village to the town, sold; the tool contract accepted and the tools bought.
/// Returns the events so far.
fn to_the_contract(sim: &mut Simulation) -> Vec<E> {
    let grain = good(sim, "g1_0");
    let tools = good(sim, "g6_0");
    let mut ev = Vec::new();
    buy(sim, grain, 30);
    go(sim, TOWN, false, false);
    ev.extend(week(sim));
    arrive(sim, &mut ev);
    assert_eq!(held(sim, grain), 30);
    sell(sim, grain, 30);
    ev.extend(week(sim));
    assert!(
        dialogs(&ev).iter().any(|d| d.0 == "offer"),
        "offer at the town"
    );
    sim.accept_contract("tools").expect("accept");
    buy(sim, tools, 18);
    go(sim, REGION, false, false);
    ev.extend(week(sim));
    arrive(sim, &mut ev);
    assert_eq!(held(sim, tools), 18);
    ev
}

/// The trip that meets the raid; returns its encounter.
fn raided_trip(sim: &mut Simulation, guards: bool, insurance: bool) -> (Vec<E>, E) {
    let mut ev = Vec::new();
    go(sim, WORKSHOP, guards, insurance);
    ev.extend(week(sim));
    arrive(sim, &mut ev);
    let encounter = ev
        .iter()
        .find(|e| matches!(e, E::TripEncounter(_)))
        .cloned()
        .expect("the scripted raid");
    (ev, encounter)
}

#[test]
fn golden_path_reaches_three_times_the_starting_coins() {
    let mut sim = s1();
    assert_eq!(coins(&sim), 500);
    assert_eq!(player(&sim).node, node(&sim, VILLAGE));
    let mut ev = to_the_contract(&mut sim);
    let (trip, encounter) = raided_trip(&mut sim, true, true);
    ev.extend(trip);
    let E::TripEncounter(x) = encounter else {
        unreachable!()
    };
    assert_eq!(
        (x.loss, x.loss_value),
        (LOSS_NONE, 0),
        "guards beat the raid off"
    );
    ev.extend(week(&mut sim));
    assert!(sim.scenario_contracts()[0].delivered);
    assert!(
        ev.iter().any(|e| matches!(e,
            E::ScenarioDialog { event, text, .. }
            if event == "delivered" && text.contains("Promised reward: 1,500 coins.")
        )),
        "delivery dialog must state the promise separately from payment"
    );
    assert!(
        ev.iter().any(|e| matches!(e,
            E::ScenarioDialog { event, coins: 1500, .. } if event == "tools"
        )),
        "the separate delivery payment keeps the actual amount"
    );
    let weeks_to_goal = statement(&sim).weeks;
    assert!(coins(&sim) >= 3 * 500, "coins {}", coins(&sim));
    ev.extend(finish(&mut sim));
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!(s.weeks, 20);
    assert!(s.net_profit >= 1300, "golden must clear 1.3x target: {s:?}");
    assert_eq!(ended(&ev), Some((s.net_profit, 3)));
    assert!(s.costs.protection > 0 && s.costs.losses == 0, "{s:?}");
    println!("s1 golden: goal reached in week {weeks_to_goal}, {s:?}");
}

#[test]
fn ignoring_everything_ends_with_an_honest_statement() {
    let mut sim = s1();
    let ev = finish(&mut sim);
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!((s.weeks, s.net_profit, s.revenue), (20, 0, 0));
    assert_eq!(ended(&ev), Some((0, 0)));
    let fired: Vec<String> = dialogs(&ev).into_iter().map(|d| d.0).collect();
    assert_eq!(
        fired,
        ["offer", "warning"],
        "fallback weeks still show the offer and warning"
    );
    let c = &sim.scenario_contracts()[0];
    assert!(!c.accepted && !c.delivered);
    assert!(sim.step().is_err(), "no week after the end");
}

#[test]
fn the_first_sale_pays_one_and_a_half_times_the_price_paid_then_the_market() {
    // Tools bought and sold back at the same town: the market's offer is below what was paid.
    let mut sim = s1();
    let tools = good(&sim, "g6_0");
    let mut skipped = Vec::new();
    go(&mut sim, TOWN, false, false);
    week(&mut sim);
    arrive(&mut sim, &mut skipped);
    buy(&mut sim, tools, 10);
    sell(&mut sim, tools, 5);
    let ev = week(&mut sim);
    sell(&mut sim, tools, 5);
    let later = week(&mut sim);
    let trades: Vec<_> = ev
        .iter()
        .chain(&later)
        .filter_map(|e| match e {
            E::GoodsTraded(t) => Some((t.bought, t.price)),
            _ => None,
        })
        .collect();
    let paid = trades[0].1;
    assert!(trades[0].0);
    assert_eq!(trades[1], (false, paid * 3 / 2), "first sale: 1.5x paid");
    assert!(
        trades[2].1 < paid,
        "second sale: the market's offer, {trades:?}"
    );
}

#[test]
fn unprotected_the_raid_takes_half_the_cargo() {
    let mut sim = s1();
    let tools = good(&sim, "g6_0");
    to_the_contract(&mut sim);
    let (_, encounter) = raided_trip(&mut sim, false, false);
    let E::TripEncounter(x) = encounter else {
        unreachable!()
    };
    assert_eq!(x.loss, LOSS_CARGO);
    assert!(x.loss_value > 0 && x.refund == 0, "{x:?}");
    assert_eq!(held(&sim, tools), 9);
    week(&mut sim);
    assert!(
        !sim.scenario_contracts()[0].delivered,
        "nine crates are not the order"
    );
    assert_honest(&statement(&sim));
    assert_eq!(statement(&sim).costs.losses, x.loss_value);
}

#[test]
fn insured_the_raid_is_refunded_eighty_percent() {
    let mut sim = s1();
    to_the_contract(&mut sim);
    let (_, encounter) = raided_trip(&mut sim, false, true);
    let E::TripEncounter(x) = encounter else {
        unreachable!()
    };
    assert_eq!(x.loss, LOSS_CARGO);
    assert_eq!(x.refund, x.loss_value * 8 / 10, "{x:?}");
    let s = statement(&sim);
    assert_honest(&s);
    assert_eq!(s.costs.losses, x.loss_value - x.refund);
}
