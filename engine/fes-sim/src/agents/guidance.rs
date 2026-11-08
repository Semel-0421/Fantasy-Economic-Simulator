//! The implicit expansion direction of the player's actions (C# `Guidance.cs`, `agents.guidance`,
//! version 1).
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

/// Number of guidance axes (C# `GuidanceAxes.Count`).
pub const GUIDANCE_AXES: usize = 5;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GuidanceState {
    /// Raw Fx weight per axis.
    pub weights: [i64; GUIDANCE_AXES],
    /// Node of the most recent guiding action, -1 if none yet.
    pub focus_node: i32,
}

impl Default for GuidanceState {
    fn default() -> Self {
        Self {
            weights: [0; GUIDANCE_AXES],
            focus_node: -1,
        }
    }
}

impl Component for GuidanceState {
    fn key(&self) -> &'static str {
        "agents.guidance"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        for v in self.weights {
            let _ = w.write_i64(v);
        }
        let _ = w.write_i32(self.focus_node);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        for v in &mut self.weights {
            *v = r.read_i64()?;
        }
        self.focus_node = r.read_i32()?;
        Ok(())
    }
}

/// One guiding signal (C# `GuidanceSignal`): push toward `axis`, centered on `focus_node`
/// (-1 for none).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GuidanceSignal {
    pub axis: fes_core::GuidanceAxis,
    /// Raw Fx weight.
    pub weight: i64,
    pub focus_node: i32,
}

/// `agents.guidance` (C# `GuidanceSystem`): decays the weights, then adds the signals of the
/// commands applied this turn.
pub struct GuidanceSystem;

impl System for GuidanceSystem {
    fn id(&self) -> &'static str {
        "agents.guidance"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Guidance
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let keep = Fx::ONE - ctx.services.database.config.guidance_decay_per_turn;
        let signals: Vec<GuidanceSignal> = ctx
            .commands
            .applied_this_turn()
            .iter()
            .filter_map(|c| c.guidance(ctx))
            .collect();
        let state = ctx.world.get_mut::<GuidanceState>();
        for w in &mut state.weights {
            *w = (Fx::from_raw(*w) * keep).raw();
        }
        for s in signals {
            state.weights[s.axis as usize] += s.weight;
            if s.focus_node >= 0 {
                state.focus_node = s.focus_node;
            }
        }
        Ok(())
    }
}
