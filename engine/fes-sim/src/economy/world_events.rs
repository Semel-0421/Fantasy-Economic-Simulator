//! Expires old modifiers and rolls the data-driven world events (C# `WorldEventSystem`).
use super::modifiers::{MarketModifier, MarketModifierTable};
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_data::defs::WorldEventDef;
use std::sync::Arc;

const SYSTEM_ID: &str = "economy.world_events";

/// Published when a world event starts on a node (C# `WorldEventStarted`; no subscriber).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct WorldEventStarted {
    /// Index of the event definition in `GameDatabase::events`.
    pub definition: i32,
    pub node: i32,
    pub turn: i32,
}

pub struct WorldEventSystem;

impl System for WorldEventSystem {
    fn id(&self) -> &'static str {
        SYSTEM_ID
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Environment
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        ctx.world
            .get_mut::<MarketModifierTable>()
            .remove_expired(turn);
        let db = Arc::clone(&ctx.services.database);
        for def in &db.events {
            let nodes = ctx.services.get::<NodeTable>();
            let start = nodes.level_start(def.scope_level);
            let end = start + nodes.level_count(def.scope_level);
            for n in start..end {
                roll(ctx, def, n);
            }
        }
        Ok(())
    }
}

/// One node's roll for one event definition; a hit adds the modifier and publishes the event.
fn roll(ctx: &mut SimContext, def: &WorldEventDef, n: i32) {
    let turn = ctx.turn();
    let mut rng = ctx.random_for(SYSTEM_ID, n, def.index as u32);
    if !rng.chance_permille(def.chance_permille) {
        return;
    }
    ctx.world
        .get_mut::<MarketModifierTable>()
        .add(MarketModifier {
            node: n,
            category: def.category_index,
            target: def.target,
            multiplier: def.multiplier,
            expires_turn: turn.wrapping_add(def.duration_turns),
            source_event: def.index,
        });
    ctx.events.publish(WorldEventStarted {
        definition: def.index,
        node: n,
        turn,
    });
}
