//! Teaching scenarios (spec 5): the scenario switch, the results statement, and (with the
//! `scenario` data) scripted events and objectives. Nothing here runs in free mode.
pub mod rules;
pub mod statement;

pub use rules::ScenarioRules;
pub use statement::{StatementCosts, StatementLedger, StatementView};

use crate::context::SimContext;
use crate::event_record::SimEventRecord;

/// The scenario state a running simulation keeps beside the world (never saved: scenarios do not
/// save, spec 9.2).
#[derive(Clone, Debug)]
pub struct ScenarioRun {
    pub ledger: StatementLedger,
}

impl ScenarioRun {
    pub fn start(ctx: &SimContext) -> Self {
        Self {
            ledger: StatementLedger::start(ctx),
        }
    }

    /// Called after each turn with the turn's events; may append scenario events.
    pub fn after_turn(&mut self, ctx: &mut SimContext, events: &[SimEventRecord]) {
        self.ledger.record(ctx, events);
    }
}
