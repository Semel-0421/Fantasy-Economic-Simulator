//! Player commands (C# `Commands.cs`): validated and applied at the start of a turn, in order.
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

/// A player action: the only way presentation code mutates simulation state. `apply` validates
/// first and must not change anything when it returns `Error`.
pub trait Command: Debug {
    fn apply(&self, ctx: &mut SimContext) -> CommandResult;
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

/// Built-in Commands-phase system that applies queued player commands.
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
            match command.apply(ctx) {
                CommandResult::Error(reason) => ctx.events.publish(CommandRejected {
                    command,
                    reason,
                    turn,
                }),
                CommandResult::Ok => {
                    ctx.commands.mark_applied(Arc::clone(&command));
                    ctx.events.publish(CommandApplied { command, turn });
                }
            }
        }
        Ok(())
    }
}
