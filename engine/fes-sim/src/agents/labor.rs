//! Employment per purse (C# `LaborMarket.cs`, section `agents.labor`, version 1). Turn-0 state only.
use super::columns::read_len;
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LaborState {
    /// Per purse: the employment rate, raw Fx.
    pub employment: Vec<i64>,
    pub long_run_activity: Vec<i64>,
    pub quarter_spending: Vec<i64>,
}

impl LaborState {
    pub fn new(floats: usize, base_employment: Fx) -> Self {
        Self {
            employment: vec![base_employment.raw(); floats],
            long_run_activity: vec![0; floats],
            quarter_spending: vec![0; floats],
        }
    }

    /// The employment rate of a purse.
    pub fn rate(&self, purse: usize) -> Fx {
        Fx::from_raw(self.employment[purse])
    }
}

impl Component for LaborState {
    fn key(&self) -> &'static str {
        "agents.labor"
    }

    fn version(&self) -> i32 {
        1
    }

    /// The three columns are interleaved per purse.
    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.employment.len() as i32);
        for p in 0..self.employment.len() {
            let _ = w.write_i64(self.employment[p]);
            let _ = w.write_i64(self.long_run_activity[p]);
            let _ = w.write_i64(self.quarter_spending[p]);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_len(r, self.employment.len())?;
        for p in 0..self.employment.len() {
            self.employment[p] = r.read_i64()?;
            self.long_run_activity[p] = r.read_i64()?;
            self.quarter_spending[p] = r.read_i64()?;
        }
        Ok(())
    }
}
