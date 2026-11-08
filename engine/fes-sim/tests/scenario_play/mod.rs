//! Plays the shipped teaching scenarios (`data/scenarios/*.ron`) on the full-scale world, the way a
//! player does: queue the week's commands, press Next week.
#![allow(dead_code)]
use fes_core::QualityGrade;
use fes_data::FileDataSource;
use fes_sim::agents::cargo::{CargoTable, PLAYER_CART};
use fes_sim::agents::player::PlayerState;
use fes_sim::commands::trade::{BuyGoods, SellGoods};
use fes_sim::commands::travel::Travel;
use fes_sim::commands::{Command, CommandResult};
use fes_sim::scenario::{ScenarioDef, StatementView};
use fes_sim::world::NodeTable;
use fes_sim::{SimEventRecord as E, Simulation, SimulationBuilder};
use std::path::PathBuf;

fn root() -> PathBuf {
    PathBuf::from(concat!(env!("CARGO_MANIFEST_DIR"), "/../.."))
}

/// The scenario file's definition.
pub fn def(name: &str) -> ScenarioDef {
    let path = root().join("data/scenarios").join(format!("{name}.ron"));
    let text = std::fs::read_to_string(&path).expect("scenario file");
    ScenarioDef::parse(&text).expect("parse scenario")
}

/// The scenario running on the full-scale world.
pub fn start(def: ScenarioDef) -> Simulation {
    let source = FileDataSource::new(root().join("engine/fixtures/fullscale"));
    SimulationBuilder::from_data(&source, 1)
        .expect("builder")
        .scenario(def)
        .build()
        .expect("build")
}

pub fn node(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .get::<NodeTable>()
        .try_find(key)
        .expect(key)
        .0
}

/// The base good's middle grade.
pub fn good(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .database
        .try_get_good(key, QualityGrade::Mid)
        .expect(key)
        .0
}

pub fn player(sim: &Simulation) -> &PlayerState {
    sim.context().world.get::<PlayerState>()
}

/// Coins on the player's current continent.
pub fn coins(sim: &Simulation) -> i64 {
    let ctx = sim.context();
    let p = player(sim);
    let c = ctx.services.get::<NodeTable>().continent[p.node as usize];
    p.coins[c as usize]
}

pub fn held(sim: &Simulation, good: i32) -> i32 {
    sim.context()
        .world
        .get::<CargoTable>()
        .quantity_of(PLAYER_CART, good)
}

/// Queues a command after checking the preview (with the week's queued commands) accepts it.
pub fn order(sim: &mut Simulation, cmd: Box<dyn Command>) {
    let preview = sim.preview_queued(cmd.as_ref());
    assert_eq!(preview.error, None, "command refused");
    sim.queue(cmd);
}

/// What the command's preview answers now.
pub fn refusal(sim: &mut Simulation, cmd: &dyn Command) -> CommandResult {
    sim.preview(cmd)
}

pub fn buy(sim: &mut Simulation, good: i32, quantity: i32) {
    order(
        sim,
        Box::new(BuyGoods {
            good,
            quantity,
            pay_from_deposit: false,
        }),
    );
}

pub fn sell(sim: &mut Simulation, good: i32, quantity: i32) {
    order(sim, Box::new(SellGoods { good, quantity }));
}

pub fn go(sim: &mut Simulation, to: &str, guards: bool, insurance: bool) {
    let destination = node(sim, to);
    order(
        sim,
        Box::new(Travel {
            destination,
            guards,
            insurance,
        }),
    );
}

/// Presses Next week; panics on a rejected command.
pub fn week(sim: &mut Simulation) -> Vec<E> {
    let events = sim.step().expect("step").events;
    for e in &events {
        if let E::CommandRejected { reason, .. } = e {
            panic!("command rejected: {reason}");
        }
    }
    events
}

/// Presses Next week until the player arrives (at most `limit` weeks).
pub fn arrive(sim: &mut Simulation, all: &mut Vec<E>) {
    for _ in 0..8 {
        if !player(sim).is_travelling() {
            return;
        }
        all.extend(week(sim));
    }
    panic!("still travelling");
}

/// Presses Next week until the scenario ends; returns the events.
pub fn finish(sim: &mut Simulation) -> Vec<E> {
    let mut all = Vec::new();
    while !sim.scenario_ended() {
        all.extend(week(sim));
    }
    all
}

pub fn statement(sim: &Simulation) -> StatementView {
    sim.statement().expect("scenario statement")
}

/// The statement adds up: revenue - costs = net profit = end - start coins.
pub fn assert_honest(s: &StatementView) {
    let c = &s.costs;
    let costs = c.purchases + c.travel + c.protection + c.losses + c.other;
    assert_eq!(s.revenue - costs, s.net_profit, "{s:?}");
    assert_eq!(s.end_coins - s.start_coins, s.net_profit, "{s:?}");
}

pub fn ended(events: &[E]) -> Option<(i64, i32)> {
    events.iter().find_map(|e| match e {
        E::ScenarioEnded {
            net_profit,
            objectives_done,
            ..
        } => Some((*net_profit, *objectives_done)),
        _ => None,
    })
}

pub fn dialogs(events: &[E]) -> Vec<(String, i64)> {
    events
        .iter()
        .filter_map(|e| match e {
            E::ScenarioDialog { event, coins, .. } => Some((event.clone(), *coins)),
            _ => None,
        })
        .collect()
}
