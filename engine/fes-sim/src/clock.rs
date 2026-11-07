//! Turn counter and master seed (C# `SimClock`). One turn is one week.
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

pub const WEEKS_PER_YEAR: i32 = 52;
pub const WEEKS_PER_QUARTER: i32 = 13;

/// Turn counter and master seed. Section `core.clock`, version 1: `turn` (i32), `seed` (u64).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct SimClock {
    pub seed: u64,
    pub turn: i32,
}

impl SimClock {
    pub fn new(seed: u64) -> Self {
        Self { seed, turn: 0 }
    }

    pub fn year(&self) -> i32 {
        self.turn / WEEKS_PER_YEAR
    }

    pub fn week(&self) -> i32 {
        self.turn % WEEKS_PER_YEAR
    }

    pub fn is_new_year(&self) -> bool {
        self.week() == 0
    }

    /// The last week of a quarter, when the quarter's financial record is taken.
    pub fn is_quarter_end(&self) -> bool {
        self.turn % WEEKS_PER_QUARTER == WEEKS_PER_QUARTER - 1
    }

    pub fn advance(&mut self) {
        self.turn += 1;
    }
}

impl Component for SimClock {
    fn key(&self) -> &'static str {
        "core.clock"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.turn);
        let _ = w.write_u64(self.seed);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        self.turn = r.read_i32()?;
        self.seed = r.read_u64()?;
        Ok(())
    }
}
