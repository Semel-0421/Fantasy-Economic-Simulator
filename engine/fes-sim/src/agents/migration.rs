//! Who may leave (C# `MigrationState` in `Migration.cs`, `agents.migration`, version 1).
use super::columns::{read_items, read_len, write_col, write_items};
use crate::world::{Component, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MigrationState {
    /// Raw Fx, per node: the share of free people.
    pub free_share: Vec<i64>,
    /// Per nation: the year emancipation was enacted, or -1.
    pub enacted_year: Vec<i32>,
}

impl MigrationState {
    pub fn new(nodes: &NodeTable, nations: usize, config: &SimulationConfig) -> Self {
        let free_share = (0..nodes.count() as i32)
            .map(|n| {
                if nodes.level_of(n) == WorldLevel::Village {
                    config.migration_start_free_share.raw()
                } else {
                    Fx::ONE_RAW
                }
            })
            .collect();
        Self {
            free_share,
            enacted_year: vec![-1; nations],
        }
    }
}

impl Component for MigrationState {
    fn key(&self) -> &'static str {
        "agents.migration"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.free_share);
        write_items(w, &self.enacted_year);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_len(r, self.free_share.len())?;
        read_items(r, &mut self.free_share)?;
        read_items(r, &mut self.enacted_year)
    }
}
