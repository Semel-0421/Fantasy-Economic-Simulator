//! Player commands (C# `Commands.cs`): validated and applied at the start of a turn, in order.
pub mod bank;
pub mod bonds;
pub mod facility;
pub mod licenses;
pub mod places;
pub mod player_command;
pub mod securities;
pub mod support;
pub mod trade;
pub mod travel;

use crate::agents::guidance::GuidanceSignal;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use std::fmt::Debug;
use std::sync::Arc;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum CommandResult {
    Ok,
    Error(String),
}

/// A player action: the only way presentation code mutates simulation state (C# `ICommand`).
/// `validate` must not change anything; `apply` runs only after it returned `Ok`. Commands must
/// not store simulation-derived state.
pub trait Command: std::any::Any + Debug + Send + Sync {
    fn validate(&self, ctx: &SimContext) -> CommandResult;
    fn apply(&self, ctx: &mut SimContext);

    /// Runs right before `validate`. Only for C# validations that fill an unsaved per-turn cache
    /// as a side effect (the first ask of `BondOffers` builds it): the cache must be built at the
    /// same point of the turn even when validation then fails. Changes nothing else.
    ///
    /// UI previews must call `prepare` before `validate` too (`Simulation::preview` does both):
    /// C# `Validate` refreshes `BondOffers` as a side effect, so a preview that skipped `prepare`
    /// could answer from a stale offer cache and differ from what the Commands phase decides.
    fn prepare(&self, _ctx: &mut SimContext) {}

    /// C# `IGuidanceSource.GetGuidance`: the expansion direction this command signals once it
    /// was applied this turn. Reads the world, never changes it; most commands signal none.
    fn guidance(&self, _ctx: &SimContext) -> Option<GuidanceSignal> {
        None
    }
}

#[derive(Clone, Debug)]
pub struct CommandApplied {
    pub command: Arc<dyn Command>,
    pub turn: i32,
}

#[derive(Clone, Debug)]
pub struct CommandRejected {
    pub command: Arc<dyn Command>,
    pub reason: String,
    pub turn: i32,
}

#[derive(Default)]
pub struct CommandQueue {
    queue: Vec<Arc<dyn Command>>,
    applied: Vec<Arc<dyn Command>>,
}

impl CommandQueue {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn len(&self) -> usize {
        self.queue.len()
    }

    pub fn is_empty(&self) -> bool {
        self.queue.is_empty()
    }

    pub fn enqueue(&mut self, command: impl Command + 'static) {
        self.queue.push(Arc::new(command));
    }

    /// Like `enqueue`, for a command already boxed as a trait object.
    pub fn enqueue_boxed(&mut self, command: Box<dyn Command>) {
        self.queue.push(Arc::from(command));
    }

    /// The commands waiting for the next Commands phase, in order.
    pub fn queued(&self) -> &[Arc<dyn Command>] {
        &self.queue
    }

    /// Commands applied in this turn's Commands phase, in order.
    pub fn applied_this_turn(&self) -> &[Arc<dyn Command>] {
        &self.applied
    }

    pub(crate) fn drain(&mut self) -> Vec<Arc<dyn Command>> {
        self.applied.clear();
        std::mem::take(&mut self.queue)
    }

    pub(crate) fn mark_applied(&mut self, command: Arc<dyn Command>) {
        self.applied.push(command);
    }

    pub fn clear(&mut self) {
        self.queue.clear();
        self.applied.clear();
    }
}

/// Built-in Commands-phase system that validates and applies queued player commands.
pub struct CommandSystem;

impl System for CommandSystem {
    fn id(&self) -> &'static str {
        "core.commands"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Commands
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        for command in ctx.commands.drain() {
            let turn = ctx.turn();
            command.prepare(ctx);
            if let CommandResult::Error(reason) = command.validate(ctx) {
                ctx.events.publish(CommandRejected {
                    command,
                    reason,
                    turn,
                });
                continue;
            }
            command.apply(ctx);
            let turn = ctx.turn();
            ctx.commands.mark_applied(Arc::clone(&command));
            ctx.events.publish(CommandApplied { command, turn });
        }
        Ok(())
    }
}
