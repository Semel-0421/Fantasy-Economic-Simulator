//! Per-level update cadence (C# `TierSchedule`).
use crate::error::SimError;
use fes_core::{TierCadence, WorldLevel};

/// A level is due when `(turn + offset) % period == 0`; higher tiers update less often.
#[derive(Clone, Debug)]
pub struct TierSchedule {
    cadence: [TierCadence; WorldLevel::COUNT],
}

impl TierSchedule {
    /// One cadence per world level; a period below 1 is raised to 1.
    pub fn new(cadence: &[TierCadence]) -> Result<Self, SimError> {
        if cadence.len() != WorldLevel::COUNT {
            return Err(SimError::Invalid(format!(
                "Expected {} cadences.",
                WorldLevel::COUNT
            )));
        }
        let mut out = [TierCadence {
            period: 1,
            offset: 0,
        }; WorldLevel::COUNT];
        for (slot, c) in out.iter_mut().zip(cadence) {
            *slot = TierCadence {
                period: c.period.max(1),
                offset: c.offset,
            };
        }
        Ok(Self { cadence: out })
    }

    pub fn every_turn() -> Self {
        Self {
            cadence: [TierCadence {
                period: 1,
                offset: 0,
            }; WorldLevel::COUNT],
        }
    }

    pub fn cadence(&self, level: WorldLevel) -> TierCadence {
        self.cadence[level as usize]
    }

    pub fn is_due(&self, level: WorldLevel, turn: i32) -> bool {
        let c = self.cadence[level as usize];
        turn.wrapping_add(c.offset).wrapping_rem(c.period) == 0
    }

    /// Turns covered by an update on a due turn (shorter for the first update).
    pub fn elapsed_turns(&self, level: WorldLevel, turn: i32) -> i32 {
        self.cadence[level as usize]
            .period
            .min(turn.wrapping_add(1))
    }
}
