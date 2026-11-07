//! Simulation core: components, the world container, the clock, the tier schedule and the turn pipeline.
pub mod clock;
pub mod commands;
pub mod context;
pub mod economy;
pub mod error;
pub mod events;
pub mod module;
pub mod pipeline;
pub mod schedule;
#[cfg(feature = "testing")]
pub mod testing;
pub mod world;

pub use clock::SimClock;
pub use commands::{
    Command, CommandApplied, CommandQueue, CommandRejected, CommandResult, CommandSystem,
};
pub use context::{Services, SimContext};
pub use error::SimError;
pub use events::{EventBus, SimEvent};
pub use module::{module_order, Module, SimBuilder};
pub use pipeline::{sim_order, SimPhase, System, TurnPipeline};
pub use schedule::TierSchedule;
pub use world::{Component, WorldState};
