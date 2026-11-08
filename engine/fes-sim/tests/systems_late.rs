//! The last three systems (`agents.guidance`, `agents.groups`, `agents.licenses`) against the
//! recorded C# turns, and the C# cleanup tests ported (`GroupTests` membership expiry,
//! `LicenseTests` expiry). The command-driven guidance tests wait for the commands (Task 8).
mod common;

use common::build_sample;
use fes_core::testing::fixtures_dir;
use fes_core::{Fx, GuidanceAxis};
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::agents::groups::{GuildMembershipExpired, GuildMembershipTable};
use fes_sim::agents::guidance::{GuidanceSignal, GuidanceState};
use fes_sim::agents::licenses::{LicenseExpired, LicenseTable};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::{Command, CommandResult, SimContext, SimEvent, Simulation};
use std::sync::Arc;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const NEW: &[&str] = &["agents.guidance", "agents.groups", "agents.licenses"];

#[test]
fn late_systems_match_on_every_sampled_turn() {
    let source = FileDataSource::new(common::data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, NEW).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        for id in NEW {
            let label = format!("system:{id}");
            assert!(
                report
                    .steps
                    .iter()
                    .any(|s| matches!(s, StepResult::Equal { label: l } if *l == label)),
                "turn {t}: {id} did not run in Rust"
            );
        }
    }
}

fn of<E: Clone + 'static>(events: &[Arc<dyn SimEvent>]) -> Vec<E> {
    events
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<E>().cloned())
        .collect()
}

fn step(sim: &mut Simulation) -> Vec<Arc<dyn SimEvent>> {
    sim.step().expect("step");
    sim.context().events.turn_log().to_vec()
}

#[derive(Debug)]
struct Signal(GuidanceSignal);

impl Command for Signal {
    fn validate(&self, _ctx: &SimContext) -> CommandResult {
        CommandResult::Ok
    }
    fn apply(&self, _ctx: &mut SimContext) {}
    fn guidance(&self, _ctx: &SimContext) -> Option<GuidanceSignal> {
        Some(self.0)
    }
}

#[test]
fn guidance_decays_and_applied_commands_add_their_signal() {
    let mut sim = build_sample(&[]);
    let one = Fx::ONE.raw();
    sim.context_mut().commands.enqueue(Signal(GuidanceSignal {
        axis: GuidanceAxis::Industry,
        weight: one,
        focus_node: 7,
    }));
    step(&mut sim);
    let state = sim.context().world.get::<GuidanceState>().clone();
    assert_eq!(state.weights[GuidanceAxis::Industry as usize], one);
    assert_eq!(state.focus_node, 7);
    step(&mut sim);
    let keep = Fx::ONE
        - sim
            .context()
            .services
            .database
            .config
            .guidance_decay_per_turn;
    let state = sim.context().world.get::<GuidanceState>();
    assert_eq!(
        state.weights[GuidanceAxis::Industry as usize],
        (Fx::from_raw(one) * keep).raw()
    );
    assert_eq!(state.focus_node, 7, "focus stays without a new signal");
}

#[test]
fn guild_membership_is_dropped_the_turn_before_it_lapses() {
    let mut sim = build_sample(&[]);
    let turn = sim.context().turn();
    let m = sim.context_mut().world.get_mut::<GuildMembershipTable>();
    m.guild.push(3);
    m.expires_turn.push(turn + 2);
    let events = step(&mut sim);
    assert!(of::<GuildMembershipExpired>(&events).is_empty());
    let events = step(&mut sim);
    assert_eq!(of::<GuildMembershipExpired>(&events).len(), 1);
    assert_eq!(sim.context().world.get::<GuildMembershipTable>().count(), 0);
}

#[test]
fn license_expires_and_is_announced() {
    let mut sim = build_sample(&[]);
    let turn = sim.context().turn();
    let l = sim.context_mut().world.get_mut::<LicenseTable>();
    l.base_good.push(2);
    l.nation.push(5);
    l.expires_turn.push(turn + 2);
    assert!(of::<LicenseExpired>(&step(&mut sim)).is_empty());
    let events = of::<LicenseExpired>(&step(&mut sim));
    assert_eq!(events.len(), 1);
    assert_eq!((events[0].base_good, events[0].nation), (2, 5));
    assert_eq!(sim.context().world.get::<LicenseTable>().count(), 0);
}
