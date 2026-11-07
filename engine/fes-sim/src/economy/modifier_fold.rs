//! Refolds the modifiers after world events changed them, before any flow of the turn (C#
//! `ModifierFoldSystem` and `MarketFlows.Rebuild`).
use super::markets::{MarketFlows, MODIFIER_TARGETS};
use super::modifiers::{MarketModifier, MarketModifierTable};
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_core::Fx;

/// Folds `modifiers` in list order into per-(node, category, target) multipliers: every node of a
/// modifier's subtree, and every category when the modifier's category is -1.
pub fn fold_multipliers(
    category_count: usize,
    nodes: &NodeTable,
    modifiers: &[MarketModifier],
) -> Vec<i64> {
    let mut out = vec![Fx::ONE_RAW; nodes.count() * category_count * MODIFIER_TARGETS];
    let mut subtree = Vec::new();
    for m in modifiers {
        let (first, last) = if m.category < 0 {
            (0, category_count as i32 - 1)
        } else {
            (m.category, m.category)
        };
        subtree.clear();
        nodes.collect_subtree(m.node, &mut subtree);
        for &n in &subtree {
            for c in first..=last {
                let index = (n as usize * category_count + c as usize) * MODIFIER_TARGETS
                    + m.target as usize;
                out[index] = Fx::round_shift(out[index].wrapping_mul(m.multiplier.raw()));
            }
        }
    }
    out
}

/// Recomputes `MarketFlows::multipliers` from the active modifiers.
pub fn rebuild_flows(ctx: &mut SimContext) {
    let category_count = ctx.services.get::<MarketFlows>().category_count;
    let folded = fold_multipliers(
        category_count,
        ctx.services.get::<NodeTable>(),
        ctx.world.get::<MarketModifierTable>().active(),
    );
    ctx.services.get_mut::<MarketFlows>().multipliers = folded;
}

pub struct ModifierFoldSystem;

impl System for ModifierFoldSystem {
    fn id(&self) -> &'static str {
        "economy.modifier_fold"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Environment
    }

    fn order(&self) -> i32 {
        sim_order::LATE
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        rebuild_flows(ctx);
        Ok(())
    }

    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        rebuild_flows(ctx);
        Ok(())
    }
}
