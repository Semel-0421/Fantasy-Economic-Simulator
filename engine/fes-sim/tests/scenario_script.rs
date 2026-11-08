//! Scripted scenario events and objectives: each trigger (and its deadline fallback), each effect,
//! the end of a scenario and a deterministic replay.
#![cfg(feature = "scenario")]
mod common;

use common::data_dir;
use fes_data::FileDataSource;
use fes_sim::agents::asset_prices::PLAYER_HOLDER;
use fes_sim::agents::cargo::{CargoTable, PLAYER_CART};
use fes_sim::agents::facilities::FacilityTable;
use fes_sim::agents::licenses::LicenseTable;
use fes_sim::agents::player::PlayerState;
use fes_sim::agents::securities::ShareTable;
use fes_sim::commands::trade::BuyGoods;
use fes_sim::commands::travel::{Travel, LOSS_CARGO, LOSS_NONE};
use fes_sim::economy::MarketModifierTable;
use fes_sim::scenario::ScenarioDef;
use fes_sim::world::NodeTable;
use fes_sim::{CommandResult, SimEventRecord as E, Simulation, SimulationBuilder};

const START: &str = "aur_n1_r1_c1_v1";
const CITY: &str = "aur_n1_r1_c1";
const FAR: &str = "aur_n1_r1_c2";

fn scenario(objectives: &str, events: &str) -> String {
    format!(
        r#"(id: "t", title: "Test", seed: 3, start_node: "{START}", start_coins: 5000, weeks: 8,
        objectives: [{objectives}], events: [{events}])"#
    )
}

fn build(text: &str) -> Simulation {
    let def = ScenarioDef::parse(text).expect("parse");
    let source = FileDataSource::new(data_dir());
    SimulationBuilder::from_data(&source, 99)
        .expect("builder")
        .scenario(def)
        .build()
        .expect("build")
}

fn node(sim: &Simulation, key: &str) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    nodes.try_find(key).expect(key).0
}

/// The scenario events of the next step.
fn step(sim: &mut Simulation) -> Vec<E> {
    sim.step()
        .expect("step")
        .events
        .into_iter()
        .filter(|e| {
            matches!(
                e,
                E::ScenarioDialog { .. }
                    | E::ObjectiveDone { .. }
                    | E::ScenarioEnded { .. }
                    | E::TripEncounter(_)
            )
        })
        .collect()
}

fn dialog_ids(events: &[E]) -> Vec<String> {
    events
        .iter()
        .filter_map(|e| match e {
            E::ScenarioDialog { event, .. } => Some(event.clone()),
            _ => None,
        })
        .collect()
}

/// Weeks (1-based) at which each event fired over `weeks` steps.
fn fire_weeks(sim: &mut Simulation, weeks: i32) -> Vec<(i32, String)> {
    (1..=weeks)
        .flat_map(|w| {
            dialog_ids(&step(sim))
                .into_iter()
                .map(move |id| (w, id))
                .collect::<Vec<_>>()
        })
        .collect()
}

fn good_key_at_start(sim: &mut Simulation) -> (i32, String) {
    let db = std::sync::Arc::clone(&sim.context().services.database);
    for g in &db.goods {
        if g.id.grade() != fes_core::QualityGrade::Mid {
            continue;
        }
        let buy = BuyGoods {
            good: g.id.0,
            quantity: 2,
            pay_from_deposit: false,
        };
        if sim.preview(&buy) == CommandResult::Ok {
            return (g.id.0, g.base_key.clone());
        }
    }
    panic!("no good to buy at the start");
}

#[test]
fn a_trigger_without_a_fallback_is_rejected() {
    let text = scenario("", r#"(id: "a", trigger: PlayerAt("x"), effects: [])"#);
    assert!(ScenarioDef::parse(&text).is_err());
    let bad_key = scenario(
        "",
        r#"(id: "a", trigger: Week(1), effects: [PriceShock(node: "nowhere", good: "wheat", percent: 50, weeks: 2, text: "")])"#,
    );
    let def = ScenarioDef::parse(&bad_key).expect("parse");
    let source = FileDataSource::new(data_dir());
    let built = SimulationBuilder::from_data(&source, 1)
        .expect("builder")
        .scenario(def)
        .build();
    assert!(built.is_err());
}

#[test]
fn start_places_the_player_with_the_start_coins() {
    let sim = build(&scenario("", ""));
    let p = sim.world().get::<PlayerState>();
    assert_eq!(p.node, node(&sim, START));
    assert_eq!(p.coins.iter().sum::<i64>(), 5000);
    assert_eq!(sim.context().seed(), 3);
}

#[test]
fn week_and_player_at_triggers_and_their_fallbacks() {
    let events = format!(
        r#"(id: "w2", trigger: Week(2), effects: [Dialog("two")]),
        (id: "here", trigger: PlayerAt("{START}"), fallback_week: 6, effects: [Dialog("at")]),
        (id: "far", trigger: PlayerAt("{FAR}"), fallback_week: 3, effects: [Dialog("late")]),
        (id: "never", trigger: Never, fallback_week: 4, effects: [Dialog("n")])"#
    );
    let mut sim = build(&scenario("", &events));
    let fired = fire_weeks(&mut sim, 6);
    let expect = [(1, "here"), (2, "w2"), (3, "far"), (4, "never")];
    let expect: Vec<(i32, String)> = expect.iter().map(|(w, s)| (*w, s.to_string())).collect();
    assert_eq!(fired, expect);
}

#[test]
fn holds_trigger_fires_when_bought_else_at_fallback() {
    let mut sim = build(&scenario("", ""));
    sim.step().expect("step");
    let (good, key) = good_key_at_start(&mut sim);
    let events = format!(
        r#"(id: "h", trigger: Holds(good: "{key}", quantity: 2), fallback_week: 5, effects: [Dialog("held")])"#
    );
    let mut bought = build(&scenario("", &events));
    bought.step().expect("step");
    bought.queue(Box::new(BuyGoods {
        good,
        quantity: 2,
        pay_from_deposit: false,
    }));
    assert_eq!(fire_weeks(&mut bought, 3), vec![(1, "h".to_string())]);
    let mut idle = build(&scenario("", &events));
    assert_eq!(fire_weeks(&mut idle, 5), vec![(5, "h".to_string())]);
}

#[test]
fn contract_delivery_pays_and_fires_contract_done_and_objectives() {
    let mut probe = build(&scenario("", ""));
    probe.step().expect("step");
    let (good, key) = good_key_at_start(&mut probe);
    let objectives = r#"(id: "deliver", text: "Deliver", goal: ContractDone("c1"))"#;
    let events = format!(
        r#"(id: "offer", trigger: Week(1), effects: [OfferContract(id: "c1", good: "{key}", quantity: 2, node: "{START}", reward: 900, deadline_week: 5, text: "Bring two")]),
        (id: "thanks", trigger: ContractDone("c1"), fallback_week: 7, effects: [Dialog("thanks")])"#
    );
    let mut sim = build(&scenario(objectives, &events));
    assert!(sim.accept_contract("c1").is_err());
    assert_eq!(dialog_ids(&step(&mut sim)), vec!["offer"]);
    assert_eq!(sim.scenario_contracts().len(), 1);
    sim.accept_contract("c1").expect("accept");
    sim.queue(Box::new(BuyGoods {
        good,
        quantity: 2,
        pay_from_deposit: false,
    }));
    let week2 = step(&mut sim);
    assert!(week2.contains(&E::ObjectiveDone {
        objective: "deliver".into(),
        turn: sim.turn()
    }));
    let paid = week2
        .iter()
        .any(|e| matches!(e, E::ScenarioDialog { event, coins: 900, .. } if event == "c1"));
    assert!(paid);
    // Delivery runs before the triggers, so the event waiting on it fires the same week.
    assert_eq!(dialog_ids(&week2), vec!["c1", "thanks"]);
    assert!(sim.objectives()[0].done);
    let s = sim.statement().expect("statement");
    assert_eq!(s.revenue - s.costs.total(), s.net_profit);
    assert!(s.revenue >= 900);
}

#[test]
fn an_accepted_contract_expires_after_its_deadline() {
    let events = format!(
        r#"(id: "offer", trigger: Week(1), effects: [OfferContract(id: "c1", good: "wheat", quantity: 2, node: "{FAR}", reward: 900, deadline_week: 2, text: "")])"#
    );
    let mut sim = build(&scenario("", &events));
    sim.step().expect("step");
    sim.accept_contract("c1").expect("accept");
    for _ in 0..3 {
        sim.step().expect("step");
    }
    let c = &sim.scenario_contracts()[0];
    assert!(c.expired && !c.delivered);
    assert!(sim.accept_contract("c1").is_err());
}

fn raid_run(guards: bool) -> (Vec<E>, i32, i32) {
    let events = format!(
        r#"(id: "raid", trigger: Week(1), effects: [Raid(from: "{START}", to: "{CITY}", loss_permille: 500)])"#
    );
    let mut sim = build(&scenario("", &events));
    sim.step().expect("step");
    let (good, _) = good_key_at_start(&mut sim);
    sim.queue(Box::new(BuyGoods {
        good,
        quantity: 2,
        pay_from_deposit: false,
    }));
    step(&mut sim);
    let before = sim
        .world()
        .get::<CargoTable>()
        .quantity_of(PLAYER_CART, good);
    let destination = node(&sim, CITY);
    sim.queue(Box::new(Travel {
        destination,
        guards,
        insurance: false,
    }));
    let events = step(&mut sim);
    let after = sim
        .world()
        .get::<CargoTable>()
        .quantity_of(PLAYER_CART, good);
    (events, before, after)
}

#[test]
fn a_scripted_raid_takes_cargo_unless_guards_ride_along() {
    let (events, before, after) = raid_run(false);
    assert!(events
        .iter()
        .any(|e| matches!(e, E::TripEncounter(x) if x.loss == LOSS_CARGO)));
    assert_eq!(after, before - before / 2);
    let (events, before, after) = raid_run(true);
    assert!(events
        .iter()
        .any(|e| matches!(e, E::TripEncounter(x) if x.loss == LOSS_NONE && x.loss_value == 0)));
    assert_eq!(after, before);
}

#[test]
fn market_company_bond_and_licence_effects() {
    let events = format!(
        r#"(id: "shock", trigger: Week(1), effects: [PriceShock(node: "{CITY}", good: "wheat", percent: 150, weeks: 3, text: "boom")]),
        (id: "news", trigger: Week(1), effects: [CompanyNews(facility: 0, percent: 50, text: "loss")]),
        (id: "div", trigger: Week(2), effects: [Dividend(facility: 1, per_share: 3, text: "paid")]),
        (id: "bond", trigger: Week(2), effects: [BondIssue(issuer: -1, text: "bonds")]),
        (id: "lic", trigger: Week(2), effects: [LicenceOffer(good: "wheat", nation: "aur_n1", text: "licence")])"#
    );
    let mut sim = build(&scenario("", &events));
    let modifiers = sim.world().get::<MarketModifierTable>().active().len();
    sim.context_mut()
        .world
        .get_mut::<ShareTable>()
        .add(1, PLAYER_HOLDER, 10);
    assert_eq!(dialog_ids(&step(&mut sim)), vec!["shock", "news"]);
    assert!(sim.world().get::<MarketModifierTable>().active().len() > modifiers);
    sim.context_mut().world.get_mut::<FacilityTable>().cash[1] = 1_000_000;
    let week2 = step(&mut sim);
    assert_eq!(dialog_ids(&week2), vec!["div", "bond", "lic"]);
    assert!(week2
        .iter()
        .any(|e| matches!(e, E::ScenarioDialog { event, coins: 30, .. } if event == "div")));
    let nation = node(&sim, "aur_n1");
    let licences = sim.world().get::<LicenseTable>();
    assert!(licences.is_valid(0, nation, sim.turn()));
}

#[test]
fn the_scenario_ends_at_its_last_week() {
    let mut sim = build(&scenario(
        r#"(id: "rich", text: "Profit", goal: NetProfit(1000000))"#,
        "",
    ));
    for _ in 0..7 {
        assert!(!step(&mut sim)
            .iter()
            .any(|e| matches!(e, E::ScenarioEnded { .. })));
    }
    let last = step(&mut sim);
    assert!(last.iter().any(|e| matches!(
        e,
        E::ScenarioEnded {
            objectives_done: 0,
            ..
        }
    )));
    assert!(sim.scenario_ended());
    assert!(sim.step().is_err());
    assert_eq!(sim.statement().expect("statement").weeks, 8);
}

#[test]
fn a_script_replays_the_same() {
    let events = format!(
        r#"(id: "shock", trigger: Week(2), effects: [PriceShock(node: "{CITY}", good: "wheat", percent: 60, weeks: 3, text: "")]),
        (id: "far", trigger: PlayerAt("{FAR}"), fallback_week: 4, effects: [Dialog("x")])"#
    );
    let run = || {
        let mut sim = build(&scenario("", &events));
        let reports: Vec<_> = (0..6).map(|_| sim.step().expect("step")).collect();
        (reports, sim.world_hash())
    };
    assert_eq!(run(), run());
}

#[test]
fn the_readme_example_parses_and_builds() {
    let readme = include_str!("../../../data/scenarios/README.md");
    let start = readme.find("```ron").expect("example") + "```ron".len();
    let end = start + readme[start..].find("```").expect("end");
    let def = ScenarioDef::parse(&readme[start..end]).expect("parse");
    let source = FileDataSource::new(data_dir());
    SimulationBuilder::from_data(&source, 1)
        .expect("builder")
        .scenario(def)
        .build()
        .expect("build");
}

#[test]
fn company_orders_and_a_coupon_move_money_without_making_any() {
    let events = r#"(id: "orders", trigger: Week(1), effects: [CompanyOrders(facility: 1, coins: 4000, text: "orders")]),
        (id: "coupon", trigger: Week(1), effects: [Coupon(issuer: -1, percent: 8, text: "coupon")])"#;
    let mut sim = build(&scenario("", events));
    let cash = sim.world().get::<FacilityTable>().cash[1];
    let week1 = step(&mut sim);
    assert_eq!(dialog_ids(&week1), vec!["orders", "coupon"]);
    assert!(sim.world().get::<FacilityTable>().cash[1] >= cash + 4000 - 1000);
    assert!(week1
        .iter()
        .any(|e| matches!(e, E::ScenarioDialog { event, coins: 0, .. } if event == "coupon")));
    common::assert_conserved(&sim, "orders and coupon");
    let unknown = scenario(
        "",
        r#"(id: "c", trigger: Week(1), effects: [Coupon(issuer: -99, percent: 8, text: "")])"#,
    );
    let source = FileDataSource::new(data_dir());
    let built = SimulationBuilder::from_data(&source, 1)
        .expect("builder")
        .scenario(ScenarioDef::parse(&unknown).expect("parse"))
        .build();
    assert!(built.is_err(), "unknown issuer");
}
