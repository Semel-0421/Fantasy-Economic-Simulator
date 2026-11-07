//! `agents.travel` (C# `TravelSystem` in `TravelCommands.cs`): completes the player's trip on the
//! arrival turn. Runs right after commands, so LOD and every later phase see the new node.
use super::player::PlayerState;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};

/// The player reached the destination of a trip (C# `PlayerArrived`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PlayerArrived {
    pub node: i32,
    pub turn: i32,
}

/// `agents.travel`.
pub struct TravelSystem;

impl System for TravelSystem {
    fn id(&self) -> &'static str {
        "agents.travel"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Commands
    }

    fn order(&self) -> i32 {
        sim_order::LATE
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let player = ctx.world.get_mut::<PlayerState>();
        if !player.is_travelling() || turn < player.arrival_turn {
            return Ok(());
        }
        player.node = player.travel_destination;
        player.travel_destination = -1;
        player.arrival_turn = -1;
        let node = player.node;
        ctx.events.publish(PlayerArrived { node, turn });
        Ok(())
    }
}
