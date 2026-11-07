//! The level-of-detail policy and its system (C# `ILodPolicy`, `DefaultLodPolicy`, `LodSystem` in
//! `Lod.cs`). The policy is the single authority on which nodes run per entity.
use super::facilities::{FacilityTable, OWNER_PLAYER};
use super::lod::LodState;
use super::merchants::AgentTable;
use super::player::PlayerState;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_core::SimDetail;

const AGGREGATE: u8 = SimDetail::Aggregate as u8;
const DETAILED: u8 = SimDetail::Detailed as u8;

/// Decides which nodes are simulated per entity. Replace the service to change the rule.
pub trait LodPolicy: Send + Sync {
    /// Fills `detail` (one `SimDetail` byte per node) from the world in `ctx`.
    fn evaluate(&self, ctx: &SimContext, detail: &mut [u8]);
}

/// The registered `ILodPolicy` service.
pub struct LodPolicyService(pub Box<dyn LodPolicy>);

/// Detailed: the subtree of the player's ancestor at the configured focus level (default Region),
/// plus every node hosting a player-owned facility or merchant. Everything else is aggregate.
/// `lod.mode` 1 makes every node detailed and 2 every node aggregate, whatever the player does.
#[derive(Clone, Copy, Debug, Default)]
pub struct DefaultLodPolicy;

impl LodPolicy for DefaultLodPolicy {
    fn evaluate(&self, ctx: &SimContext, detail: &mut [u8]) {
        let config = &ctx.services.database.config;
        if config.lod_mode != 0 {
            let value = if config.lod_mode == 1 {
                DETAILED
            } else {
                AGGREGATE
            };
            detail.fill(value);
            return;
        }
        let nodes = ctx.services.get::<NodeTable>();
        let player = ctx.world.get::<PlayerState>();
        let facilities = ctx.world.get::<FacilityTable>();
        let agents = ctx.world.get::<AgentTable>();
        detail.fill(AGGREGATE);
        let focus = nodes.ancestor_at(player.node, config.lod_focus_level);
        let mut subtree = Vec::new();
        nodes.collect_subtree(if focus >= 0 { focus } else { player.node }, &mut subtree);
        for n in subtree {
            detail[n as usize] = DETAILED;
        }
        for i in 0..facilities.count() {
            if facilities.owner[i] == OWNER_PLAYER {
                detail[facilities.node[i] as usize] = DETAILED;
            }
        }
        for i in 0..agents.count() {
            if agents.owner[i] == OWNER_PLAYER {
                detail[agents.node[i] as usize] = DETAILED;
            }
        }
    }
}

/// Recomputes `LodState` through the policy service.
pub fn evaluate_lod(ctx: &mut SimContext) {
    let mut detail = std::mem::take(&mut ctx.services.get_mut::<LodState>().detail);
    ctx.services
        .get::<LodPolicyService>()
        .0
        .evaluate(ctx, &mut detail);
    ctx.services.get_mut::<LodState>().detail = detail;
}

/// `agents.lod`: evaluates the policy every turn and after a load.
pub struct LodSystem;

impl System for LodSystem {
    fn id(&self) -> &'static str {
        "agents.lod"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Lod
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        evaluate_lod(ctx);
        Ok(())
    }

    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        evaluate_lod(ctx);
        Ok(())
    }
}
