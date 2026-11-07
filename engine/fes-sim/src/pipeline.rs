//! Phases, systems and the turn pipeline (C# `Core/Pipeline.cs`).
use crate::context::SimContext;
use crate::error::SimError;

/// Fixed turn phases. Systems in a phase run in (order, id) order; events dispatch after each phase.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum SimPhase {
    Commands = 0,
    Lod = 1,
    Environment = 2,
    Production = 3,
    Agents = 4,
    Markets = 5,
    Finance = 6,
    Guidance = 7,
    Cleanup = 8,
}

/// Named order bands inside a phase.
pub mod sim_order {
    pub const EARLY: i32 = -100;
    pub const DEFAULT: i32 = 0;
    pub const LATE: i32 = 100;
}

/// A unit of simulation behavior, registered from a module.
pub trait System {
    /// Stable id, e.g. "economy.pricing"; also keys the system's RNG stream.
    fn id(&self) -> &'static str;
    fn phase(&self) -> SimPhase;
    fn order(&self) -> i32 {
        0
    }
    /// Called once after every module has registered.
    fn initialize(&mut self, _ctx: &mut SimContext) -> Result<(), SimError> {
        Ok(())
    }
    /// Rebuilds derived (non-saved) state after a save is loaded.
    fn on_loaded(&mut self, _ctx: &mut SimContext) -> Result<(), SimError> {
        Ok(())
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError>;
}

pub struct TurnPipeline {
    systems: Vec<Box<dyn System>>,
}

impl TurnPipeline {
    /// Sorted by (phase, order, id ordinal); the sort is stable.
    pub fn new(mut systems: Vec<Box<dyn System>>) -> Self {
        systems.sort_by(|a, b| {
            (a.phase(), a.order())
                .cmp(&(b.phase(), b.order()))
                .then_with(|| a.id().as_bytes().cmp(b.id().as_bytes()))
        });
        Self { systems }
    }

    pub fn system_ids(&self) -> Vec<&'static str> {
        self.systems.iter().map(|s| s.id()).collect()
    }

    pub fn initialize(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        for s in &mut self.systems {
            s.initialize(ctx)?;
        }
        Ok(())
    }

    /// Clears per-turn transient state and lets systems rebuild derived state after a load.
    pub fn notify_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        ctx.commands.clear();
        ctx.events.reset();
        for s in &mut self.systems {
            s.on_loaded(ctx)?;
        }
        Ok(())
    }

    pub fn advance(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        ctx.events.begin_turn();
        let mut i = 0;
        while i < self.systems.len() {
            let phase = self.systems[i].phase();
            while i < self.systems.len() && self.systems[i].phase() == phase {
                self.systems[i].execute(ctx)?;
                i += 1;
            }
            ctx.events.dispatch()?;
        }
        ctx.clock_mut().advance();
        Ok(())
    }
}
