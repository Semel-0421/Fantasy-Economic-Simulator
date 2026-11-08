//! Scenario effects never make or destroy money: what a script pays comes from someone who has
//! it, and what it takes goes to someone. Also the protected opening's cap and the shipped files.
#![cfg(feature = "scenario")]
mod common;

use common::{assert_conserved, data_dir};
use fes_data::FileDataSource;
use fes_sim::agents::facilities::FacilityTable;
use fes_sim::commands::trade::{BuyGoods, SellGoods};
use fes_sim::economy::HouseholdCash;
use fes_sim::scenario::{ScenarioDef, Trigger};
use fes_sim::world::NodeTable;
use fes_sim::{CommandResult, SimEventRecord as E, Simulation, SimulationBuilder};

const START: &str = "aur_n1_r1_c1_v1";

fn scenario_with(coins: i64, extra: &str, events: &str) -> String {
    format!(
        r#"(id: "t", title: "Test", seed: 3, start_node: "{START}", start_coins: {coins}, weeks: 8,
        {extra} objectives: [], events: [{events}])"#
    )
}

fn try_build(text: &str) -> Result<Simulation, String> {
    let def = ScenarioDef::parse(text)?;
    let source = FileDataSource::new(data_dir());
    SimulationBuilder::from_data(&source, 99)
        .map_err(|e| format!("{e:?}"))?
        .scenario(def)
        .build()
        .map_err(|e| format!("{e:?}"))
}

fn build(text: &str) -> Simulation {
    try_build(text).expect("build")
}

/// The households' cash where `node` keeps it (its core when classes are absorbed).
fn household_slot(sim: &Simulation, node: i32) -> usize {
    let nodes = sim.context().services.get::<NodeTable>();
    (if nodes.absorbed() {
        nodes.core[node as usize]
    } else {
        node
    }) as usize
}

fn min_household_cash(sim: &Simulation) -> i64 {
    *sim.world()
        .get::<HouseholdCash>()
        .cash
        .iter()
        .min()
        .expect("nodes")
}

#[test]
fn company_news_hands_the_lost_cash_to_the_households() {
    let events = r#"(id: "news", trigger: Week(1), effects: [CompanyNews(facility: 0, percent: 50, text: "loss")])"#;
    let mut sim = build(&scenario_with(5000, "", events));
    let node = sim.world().get::<FacilityTable>().node[0];
    let slot = household_slot(&sim, node);
    let world = &mut sim.context_mut().world;
    world.get_mut::<HouseholdCash>().cash[slot] -= 1_000_000;
    world.get_mut::<FacilityTable>().cash[0] += 1_000_000;
    sim.step().expect("step");
    assert_conserved(&sim, "company news");
    let over = r#"(id: "news", trigger: Week(1), effects: [CompanyNews(facility: 0, percent: 150, text: "")])"#;
    assert!(
        try_build(&scenario_with(5000, "", over)).is_err(),
        "percent > 100"
    );
}

#[test]
fn company_orders_take_only_what_the_households_have() {
    let events = r#"(id: "orders", trigger: Week(1), effects: [CompanyOrders(facility: 1, coins: 1000000000000, text: "")])"#;
    let mut sim = build(&scenario_with(5000, "", events));
    let node = sim.world().get::<FacilityTable>().node[1];
    let slot = household_slot(&sim, node);
    sim.step().expect("step");
    assert!(sim.world().get::<HouseholdCash>().cash[slot] >= 0);
    assert_conserved(&sim, "orders");
}

#[test]
fn the_start_coins_come_from_households_that_have_them() {
    let sim = build(&scenario_with(5000, "", ""));
    assert!(min_household_cash(&sim) >= 0);
    assert!(
        try_build(&scenario_with(1_000_000_000_000_000, "", "")).is_err(),
        "no continent holds that much"
    );
}

#[test]
fn a_contract_reward_is_paid_by_someone() {
    let events = format!(
        r#"(id: "offer", trigger: Week(1), effects: [OfferContract(id: "c1", good: "wheat", quantity: 0, node: "{START}", reward: 1000000000000000, deadline_week: 5, text: "")])"#
    );
    let mut sim = build(&scenario_with(5000, "", &events));
    sim.step().expect("step");
    sim.accept_contract("c1").expect("accept");
    let week2 = sim.step().expect("step").events;
    let paid = week2.iter().find_map(|e| match e {
        E::ScenarioDialog { event, coins, .. } if event == "c1" => Some(*coins),
        _ => None,
    });
    let paid = paid.expect("delivered");
    assert!(paid > 0 && paid < 1_000_000_000_000_000, "paid {paid}");
    assert!(min_household_cash(&sim) >= 0);
    assert_conserved(&sim, "reward");
}

fn first_buyable(sim: &mut Simulation) -> i32 {
    let db = std::sync::Arc::clone(&sim.context().services.database);
    db.goods
        .iter()
        .filter(|g| g.id.grade() == fes_core::QualityGrade::Mid)
        .map(|g| g.id.0)
        .find(|&good| {
            let buy = BuyGoods {
                good,
                quantity: 6,
                pay_from_deposit: false,
            };
            sim.preview(&buy) == CommandResult::Ok
        })
        .expect("a good to buy")
}

#[test]
fn the_protected_price_covers_only_the_first_purchase() {
    let mut sim = build(&scenario_with(5000, "protected_opening: true,", ""));
    sim.step().expect("step");
    let good = first_buyable(&mut sim);
    for quantity in [2, 3] {
        sim.queue(Box::new(BuyGoods {
            good,
            quantity,
            pay_from_deposit: false,
        }));
    }
    sim.queue(Box::new(SellGoods { good, quantity: 5 }));
    let events = sim.step().expect("step").events;
    let trades: Vec<_> = events
        .iter()
        .filter_map(|e| match e {
            E::GoodsTraded(t) => Some(*t),
            _ => None,
        })
        .collect();
    let paid = trades[0].price;
    let protected: i32 = trades
        .iter()
        .filter(|t| !t.bought && t.price == paid * 3 / 2)
        .map(|t| t.filled)
        .sum();
    let sold: i32 = trades.iter().filter(|t| !t.bought).map(|t| t.filled).sum();
    assert_eq!(protected, 2, "{trades:?}");
    assert_eq!(sold, 5, "{trades:?}");
}

#[test]
fn every_shipped_event_can_fire() {
    for file in [
        "s1_first_venture",
        "s2_shares_and_bonds",
        "s3_licences_abroad",
    ] {
        let path = format!(
            "{}/../../data/scenarios/{file}.ron",
            env!("CARGO_MANIFEST_DIR")
        );
        let text = std::fs::read_to_string(&path).expect("read");
        let def = ScenarioDef::parse(&text).expect("parse");
        for e in &def.events {
            assert!(
                matches!(e.trigger, Trigger::Week(_)) || e.fallback_week.is_some(),
                "{file}: event '{}' may never fire",
                e.id
            );
        }
    }
}
