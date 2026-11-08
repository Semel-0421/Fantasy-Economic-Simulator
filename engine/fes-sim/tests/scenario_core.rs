//! The scenario switch: free mode untouched, randomness off in a scenario, the statement ties.
mod common;

use common::data_dir;
use fes_data::FileDataSource;
use fes_sim::agents::orders::MerchantTripStats;
use fes_sim::agents::player::PlayerState;
use fes_sim::commands::trade::{BuyGoods, SellGoods};
use fes_sim::commands::travel::Travel;
use fes_sim::scenario::ScenarioRules;
use fes_sim::world::RouteTable;
use fes_sim::{Simulation, SimulationBuilder};

const SEED: u64 = 7;

fn build(scenario: bool) -> Simulation {
    let source = FileDataSource::new(data_dir());
    let mut b = SimulationBuilder::from_data(&source, SEED).expect("builder");
    if scenario {
        b = b.scenario_rules(ScenarioRules::scenario());
    }
    b.build().expect("build")
}

/// (world events + financial events, player encounters, merchant encounters) over `turns`.
fn random_counts(sim: &mut Simulation, turns: i32) -> (usize, usize, i64) {
    let (mut events, mut trips) = (0, 0);
    for _ in 0..turns {
        for e in sim.step().expect("step").events {
            match e.name() {
                "WorldEventStarted" | "FinancialEventStarted" => events += 1,
                "TripEncounter" => trips += 1,
                _ => {}
            }
        }
    }
    let merchants = sim
        .context()
        .services
        .try_get::<MerchantTripStats>()
        .map_or(0, |s| s.encounters);
    (events, trips, merchants)
}

#[test]
fn free_mode_has_no_scenario_and_same_world() {
    let a = build(false);
    let b = build(true);
    assert!(!a.is_scenario());
    assert!(a.statement().is_none());
    assert!(b.is_scenario());
    // The switch adds no state to the world.
    assert_eq!(a.world_hash(), b.world_hash());
}

#[test]
fn scenario_turns_off_random_events_and_encounters() {
    let mut free = build(false);
    let (events, _, merchants) = random_counts(&mut free, 30);
    assert!(events > 0, "the free run should roll some random event");
    assert!(
        merchants > 0,
        "the free run should roll some merchant encounter"
    );
    let mut scenario = build(true);
    assert_eq!(random_counts(&mut scenario, 30), (0, 0, 0));
}

fn first_buyable(sim: &mut Simulation) -> i32 {
    let goods = sim.context().services.database.goods.len() as i32;
    (0..goods)
        .find(|&good| {
            sim.preview(&BuyGoods {
                good,
                quantity: 3,
                pay_from_deposit: false,
            }) == fes_sim::CommandResult::Ok
        })
        .expect("a good to buy")
}

#[test]
fn statement_ties_to_the_coin_change() {
    let mut sim = build(true);
    sim.step().expect("step");
    let good = first_buyable(&mut sim);
    sim.queue(Box::new(BuyGoods {
        good,
        quantity: 3,
        pay_from_deposit: false,
    }));
    let node = sim.context().world.get::<PlayerState>().node;
    let routes = sim.context().services.get::<RouteTable>();
    let route = routes.route_index[routes.out_start[node as usize] as usize] as usize;
    let destination = routes.to[route];
    sim.queue(Box::new(Travel {
        destination,
        guards: true,
        insurance: false,
    }));
    for _ in 0..12 {
        sim.step().expect("step");
        if !sim.context().world.get::<PlayerState>().is_travelling() {
            sim.queue(Box::new(SellGoods { good, quantity: 3 }));
        }
    }
    let s = sim.statement().expect("statement");
    assert!(s.costs.purchases > 0);
    assert!(s.costs.travel > 0);
    assert!(s.costs.protection > 0);
    assert!(s.revenue > 0);
    assert_eq!(s.weeks, 13);
    assert_eq!(s.end_coins - s.start_coins, s.net_profit);
    assert_eq!(s.revenue - s.costs.total(), s.net_profit);
}
