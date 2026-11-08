//! Simulation core: components, the world container, the clock, the tier schedule and the turn pipeline.
pub mod agents;
pub mod builder;
pub mod clock;
pub mod commands;
pub mod context;
pub mod economy;
pub mod error;
pub mod event_record;
pub mod events;
#[cfg(feature = "testing")]
pub mod full_run;
pub mod module;
pub mod pipeline;
pub mod preview;
#[cfg(feature = "testing")]
pub mod replay;
#[cfg(feature = "testing")]
pub mod replay_services;
pub mod scenario;
pub mod schedule;
pub mod simulation;
#[cfg(feature = "testing")]
pub mod testing;
pub mod views;
pub mod world;

pub use builder::SimulationBuilder;
pub use clock::SimClock;
pub use commands::player_command::PlayerCommand;
pub use commands::{
    Command, CommandApplied, CommandQueue, CommandRejected, CommandResult, CommandSystem,
};
pub use context::{Services, SimContext};
pub use error::SimError;
pub use event_record::SimEventRecord;
pub use events::{EventBus, SimEvent};
pub use module::{module_order, Module, SimBuilder};
pub use pipeline::{ordinal_cmp, sim_order, SimPhase, System, TurnPipeline};
pub use schedule::TierSchedule;
pub use simulation::{Simulation, TurnReport};
pub use world::{Component, WorldState};

use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_save::RawSection;

/// Every save section of the turn-0 world, by key (the default modules' components).
pub const TURN0_KEYS: &[&str] = &[
    "agents.asset_markets",
    "agents.auctions",
    "agents.banks",
    "agents.bill_room",
    "agents.bills",
    "agents.bonds",
    "agents.cargo",
    "agents.contracts",
    "agents.demand_forecast",
    "agents.development",
    "agents.facilities",
    "agents.finance",
    "agents.groups",
    "agents.guidance",
    "agents.guild_memberships",
    "agents.income_tax",
    "agents.labor",
    "agents.land",
    "agents.licenses",
    "agents.loans",
    "agents.margin",
    "agents.merchant_genetics",
    "agents.merchants",
    "agents.migration",
    "agents.nobles",
    "agents.npc_loans",
    "agents.orders",
    "agents.player",
    "agents.policy",
    "agents.purses",
    "agents.shares",
    "agents.ship",
    "agents.short_licences",
    "agents.shorts",
    "agents.trader_loans",
    "agents.ventures",
    "core.clock",
    "economy.currency",
    "economy.household_cash",
    "economy.households",
    "economy.markets",
    "economy.modifiers",
    "world.roads",
];

/// Builds the turn-0 world with the default modules and returns its sections in key order (the
/// parity runner's `BuildWorld`).
pub fn build_world(db: &GameDatabase, map: &MapDef, seed: u64) -> Result<Vec<RawSection>, String> {
    let sim = SimulationBuilder::new(db.clone(), map.clone(), seed)
        .with_default_modules()
        .and_then(SimulationBuilder::build)
        .map_err(|e| e.to_string())?;
    Ok(sim.world().sections())
}
