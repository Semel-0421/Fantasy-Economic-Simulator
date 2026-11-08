//! Front-end plain data (commands, events, views) survives a JSON round trip.
#![cfg(feature = "serde")]
mod common;

use common::build_sample;
use fes_sim::agents::travel::PlayerArrived;
use fes_sim::{views, PlayerCommand, SimEventRecord};
use serde::{de::DeserializeOwned, Serialize};
use std::fmt::Debug;

fn round_trip<T: Serialize + DeserializeOwned + PartialEq + Debug>(value: &T) {
    let json = serde_json::to_string(value).expect("serialize");
    let back: T = serde_json::from_str(&json).expect("deserialize");
    assert_eq!(value, &back, "{json}");
}

#[test]
fn player_commands_round_trip() {
    round_trip(&PlayerCommand::Exchange {
        from_continent: 0,
        to_continent: 1,
        amount: 500,
    });
    round_trip(&PlayerCommand::RepurposeFacility {
        facility: 3,
        def_key: "mill".to_string(),
    });
    round_trip(&PlayerCommand::BuyPlot);
}

#[test]
fn event_records_round_trip() {
    round_trip(&SimEventRecord::PlayerArrived(PlayerArrived {
        node: 4,
        turn: 9,
    }));
    round_trip(&SimEventRecord::CommandRejected {
        command: Some(PlayerCommand::BuyShip),
        reason: "no money".to_string(),
        turn: 2,
    });
}

#[test]
fn views_of_a_running_world_round_trip() {
    let mut sim = build_sample(&[]);
    let report = sim.step().expect("step");
    for e in &report.events {
        round_trip(e);
    }
    let ctx = sim.context();
    round_trip(&views::purse(ctx));
    round_trip(&views::cargo(ctx));
    round_trip(&views::contracts(ctx));
    round_trip(&views::shares(ctx));
    round_trip(&views::licences(ctx));
    round_trip(&views::statement(ctx));
}
