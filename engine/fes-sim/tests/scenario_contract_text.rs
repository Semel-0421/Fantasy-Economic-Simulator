//! Contract dialog values come from the contract and the actual payment, never a literal promise.
#![cfg(feature = "scenario")]
mod common;

use fes_data::FileDataSource;
use fes_sim::agents::banks::BankState;
use fes_sim::economy::HouseholdCash;
use fes_sim::scenario::{Effect, ScenarioDef, Script};
use fes_sim::{SimEventRecord as E, Simulation, SimulationBuilder};

const START: &str = "aur_n1_r1_c1_v1";

fn scenario(offer: &str, delivery: &str, reward: i64) -> String {
    format!(
        r#"(id: "text", title: "Text", seed: 3, start_node: "{START}", start_coins: 0,
        weeks: 8, objectives: [], events: [
          (id: "offer", trigger: Week(1), effects: [OfferContract(id: "c1", good: "wheat",
            quantity: 0, node: "{START}", reward: {reward}, deadline_week: 5, text: {offer:?})]),
          (id: "done", trigger: ContractDone("c1"), fallback_week: 7, effects: [Dialog({delivery:?})])
        ])"#
    )
}

fn setup(text: &str) -> (Simulation, Script) {
    let def = ScenarioDef::parse(text).expect("parse");
    let source = FileDataSource::new(common::data_dir());
    let mut sim = SimulationBuilder::from_data(&source, 3)
        .expect("builder")
        .build()
        .expect("world");
    let node = sim
        .context()
        .services
        .get::<fes_sim::world::NodeTable>()
        .try_find(START)
        .unwrap()
        .0;
    sim.context_mut()
        .world
        .get_mut::<fes_sim::agents::player::PlayerState>()
        .node = node;
    let script = Script::new(&def, sim.context(), 0).expect("script");
    (sim, script)
}

fn dialog<'a>(events: &'a [E], id: &str) -> (&'a str, i64) {
    events
        .iter()
        .find_map(|e| match e {
            E::ScenarioDialog {
                event, text, coins, ..
            } if event == id => Some((text.as_str(), *coins)),
            _ => None,
        })
        .expect("dialog")
}

#[test]
fn contract_placeholders_fill_offer_view_and_delivery() {
    let offer = "{qty} {good} to {dest} by {deadline}: {reward}, again {reward}.";
    let delivery = "Promised {reward}, paid {paid}; {qty} {good} to {dest} by {deadline}.";
    let (mut sim, mut script) = setup(&scenario(offer, delivery, 1379));
    let mut events = Vec::new();
    script.tick(sim.context_mut(), 1, &mut events);
    let expected = format!("0 wheat to {START} by 5: 1379, again 1379.");
    assert_eq!(dialog(&events, "offer"), (expected.as_str(), 0));
    assert_eq!(script.contracts[0].text, expected);
    script.accept("c1").unwrap();
    events.clear();
    script.tick(sim.context_mut(), 2, &mut events);
    assert_eq!(dialog(&events, "c1").1, 1379);
    let expected = format!("Promised 1379, paid 1379; 0 wheat to {START} by 5.");
    assert_eq!(dialog(&events, "done"), (expected.as_str(), 0));
}

#[test]
fn unknown_malformed_and_out_of_context_placeholders_fail_validation() {
    for bad in [
        "{rewrad}",
        "{reward",
        "reward}",
        "{}",
        "{{reward}}",
        "{paid}",
    ] {
        assert!(
            ScenarioDef::parse(&scenario(bad, "done", 900)).is_err(),
            "offer {bad}"
        );
    }
    assert!(ScenarioDef::parse(&scenario("offer", "{typo}", 900)).is_err());
    let outside = scenario("offer", "{reward}", 900).replace("ContractDone(\"c1\")", "Week(3)");
    assert!(ScenarioDef::parse(&outside).is_err(), "no contract context");
    let missing = scenario("offer", "{reward}", 900)
        .replace("ContractDone(\"c1\")", "ContractDone(\"missing\")");
    assert!(ScenarioDef::parse(&missing).is_err(), "unknown contract");

    // Public callers can construct/edit a definition without going through parse.
    let mut def = ScenarioDef::parse(&scenario("offer", "done", 900)).unwrap();
    def.events[1].effects[0] = Effect::Dialog("{unknown}".into());
    let (sim, _) = setup(&scenario("offer", "done", 900));
    assert!(Script::new(&def, sim.context(), 0).is_err());
}

#[test]
fn delivery_text_reports_partial_and_zero_payments() {
    let reward = 1_000_000_000_000_000;
    for empty in [false, true] {
        let (mut sim, mut script) = setup(&scenario(
            "Offer {reward}.",
            "Promised {reward}, paid {paid}.",
            reward,
        ));
        let mut events = Vec::new();
        script.tick(sim.context_mut(), 1, &mut events);
        script.accept("c1").unwrap();
        if empty {
            let world = &mut sim.context_mut().world;
            world.get_mut::<HouseholdCash>().cash.fill(0);
            world.get_mut::<BankState>().reserve.fill(0);
        }
        events.clear();
        script.tick(sim.context_mut(), 2, &mut events);
        let paid = dialog(&events, "c1").1;
        assert!(paid < reward);
        assert_eq!(paid == 0, empty);
        let expected = format!("Promised {reward}, paid {paid}.");
        assert_eq!(dialog(&events, "done"), (expected.as_str(), 0));
        assert_eq!(
            script.contracts[0].reward, reward,
            "promise stays unchanged"
        );
    }
}

#[test]
fn each_delivery_uses_its_own_contract_and_payment() {
    let text = scenario("{reward}", "{good}: {reward}/{paid}", 17).replace(
        "(id: \"done\"",
        &format!(r#"(id: "offer2", trigger: Week(1), effects: [OfferContract(id: "c2", good: "barley", quantity: 0, node: "{START}", reward: 29, deadline_week: 6, text: "{{reward}}")]),
        (id: "done2", trigger: ContractDone("c2"), fallback_week: 7, effects: [Dialog("{{good}}: {{reward}}/{{paid}}")]),
        (id: "done""#),
    );
    let (mut sim, mut script) = setup(&text);
    script.tick(sim.context_mut(), 1, &mut Vec::new());
    script.accept("c1").unwrap();
    script.accept("c2").unwrap();
    let mut events = Vec::new();
    script.tick(sim.context_mut(), 2, &mut events);
    assert_eq!(dialog(&events, "done"), ("wheat: 17/17", 0));
    assert_eq!(dialog(&events, "done2"), ("barley: 29/29", 0));
}

#[test]
fn early_fallback_uses_declared_contract_and_no_payment() {
    let text = scenario("offer", "Promised {reward}, paid {paid}.", 1379)
        .replace("Week(1)", "Week(3)")
        .replace("fallback_week: 7", "fallback_week: 1");
    let (mut sim, mut script) = setup(&text);
    let mut events = Vec::new();
    script.tick(sim.context_mut(), 1, &mut events);
    assert!(script.contracts.is_empty(), "not offered yet");
    assert_eq!(dialog(&events, "done"), ("Promised 1379, paid 0.", 0));
}
