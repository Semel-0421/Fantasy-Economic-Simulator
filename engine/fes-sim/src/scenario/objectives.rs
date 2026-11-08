//! The scenario's targets: checked at the end of each week, done once (never undone).
use super::defs::{Goal, ObjectiveDef};
use super::script::{Keys, Script};
use crate::agents::cargo::{CargoTable, PLAYER_CART};
use crate::context::SimContext;
use crate::event_record::SimEventRecord;

/// One objective as the player sees it.
#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct ObjectiveView {
    pub id: String,
    pub text: String,
    pub done: bool,
    /// The week it was done, or -1.
    pub done_week: i32,
}

#[derive(Clone, Debug)]
pub struct Objectives {
    defs: Vec<ObjectiveDef>,
    done_week: Vec<i32>,
}

impl Objectives {
    pub fn new(defs: &[ObjectiveDef]) -> Self {
        Self {
            defs: defs.to_vec(),
            done_week: vec![-1; defs.len()],
        }
    }

    /// Marks the objectives that hold now; publishes `ObjectiveDone` for each newly done one.
    pub fn check(
        &mut self,
        ctx: &SimContext,
        script: &Script,
        net_profit: i64,
        week: i32,
        out: &mut Vec<SimEventRecord>,
    ) {
        for (i, def) in self.defs.iter().enumerate() {
            if self.done_week[i] >= 0 || !holds(ctx, script, &def.goal, net_profit) {
                continue;
            }
            self.done_week[i] = week;
            out.push(SimEventRecord::ObjectiveDone {
                objective: def.id.clone(),
                turn: ctx.turn(),
            });
        }
    }

    pub fn done_count(&self) -> i32 {
        self.done_week.iter().filter(|&&w| w >= 0).count() as i32
    }

    pub fn views(&self) -> Vec<ObjectiveView> {
        self.defs
            .iter()
            .zip(&self.done_week)
            .map(|(d, &w)| ObjectiveView {
                id: d.id.clone(),
                text: d.text.clone(),
                done: w >= 0,
                done_week: w,
            })
            .collect()
    }
}

fn holds(ctx: &SimContext, script: &Script, goal: &Goal, net_profit: i64) -> bool {
    match goal {
        Goal::NetProfit(target) => net_profit >= *target,
        Goal::ContractDone(id) => script.delivered(id),
        Goal::Holds { good, quantity } => Keys(ctx)
            .good(good)
            .is_ok_and(|g| ctx.world.get::<CargoTable>().quantity_of(PLAYER_CART, g) >= *quantity),
    }
}
