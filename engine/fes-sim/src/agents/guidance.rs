//! The implicit expansion direction of the player's actions (C# `Guidance.cs`, `agents.guidance`,
//! version 1).
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

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
