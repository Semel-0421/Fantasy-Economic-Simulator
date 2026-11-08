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
pub trait System: Send {
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

/// C# `string.CompareOrdinal`: compares UTF-16 code units, which differs from byte order for
/// characters above U+FFFF versus U+E000..U+FFFF.
pub fn ordinal_cmp(a: &str, b: &str) -> std::cmp::Ordering {
    a.encode_utf16().cmp(b.encode_utf16())
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
                .then_with(|| ordinal_cmp(a.id(), b.id()))
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

    /// Runs the system with this id once, outside a turn (used by the step-replay harness).
    pub fn run_system(&mut self, id: &str, ctx: &mut SimContext) -> Result<(), SimError> {
        match self.systems.iter_mut().find(|s| s.id() == id) {
            Some(s) => s.execute(ctx),
            None => Err(SimError::Invalid(format!(
                "no system '{id}' in the pipeline"
            ))),
        }
    }

    /// Like `advance`, but returns each system's run time and the event dispatch time of each phase
    /// (labelled `dispatch`), for profiling. Native only: `std::time::Instant` panics on
    /// `wasm32-unknown-unknown`.
    #[cfg(not(target_arch = "wasm32"))]
    pub fn advance_timed(
        &mut self,
        ctx: &mut SimContext,
    ) -> Result<Vec<(&'static str, std::time::Duration)>, SimError> {
        let origin = std::time::Instant::now();
        let mut now = || origin.elapsed().as_secs_f64() * 1000.0;
        let times = self.advance_clocked(ctx, &mut now)?;
        Ok(times
            .into_iter()
            .map(|(id, ms)| (id, std::time::Duration::from_secs_f64(ms / 1000.0)))
            .collect())
    }

    /// Like `advance_timed`, with the clock (milliseconds) supplied by the caller, so a `wasm32`
    /// host can pass `performance.now`. Returns milliseconds per system and per dispatch.
    pub fn advance_clocked(
        &mut self,
        ctx: &mut SimContext,
        now: &mut dyn FnMut() -> f64,
    ) -> Result<Vec<(&'static str, f64)>, SimError> {
        let mut times = Vec::with_capacity(self.systems.len() + 9);
        ctx.events.begin_turn();
        let mut i = 0;
        while i < self.systems.len() {
            let phase = self.systems[i].phase();
            while i < self.systems.len() && self.systems[i].phase() == phase {
                let start = now();
                self.systems[i].execute(ctx)?;
                times.push((self.systems[i].id(), now() - start));
                i += 1;
            }
            let start = now();
            ctx.dispatch_events()?;
            times.push(("dispatch", now() - start));
        }
        ctx.clock_mut().advance();
        Ok(times)
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
            ctx.dispatch_events()?;
        }
        ctx.clock_mut().advance();
        Ok(())
    }
}
