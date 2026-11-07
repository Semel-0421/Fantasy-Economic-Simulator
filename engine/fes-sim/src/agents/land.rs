//! Land plots per node (C# `Land.cs`, section `agents.land`, version 1). Turn-0 state only.
use super::columns::read_len;
use crate::world::{Component, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_data::SimulationConfig;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LandState {
    /// Per node: plots in all.
    pub plots: Vec<i32>,
    /// Per node: plots with a facility on them.
    pub used: Vec<i32>,
    pub player_plots: Vec<i32>,
}

impl LandState {
    /// `facility_nodes` lists the node of every facility, so each node's plots cover its facilities.
    pub fn new(nodes: &NodeTable, config: &SimulationConfig, facility_nodes: &[i32]) -> Self {
        let mut used = vec![0i32; nodes.count()];
        for node in facility_nodes {
            used[*node as usize] += 1;
        }
        let plots = (0..nodes.count())
            .map(|n| config.land_plots[usize::from(nodes.level[n])].max(used[n]))
            .collect();
        Self {
            plots,
            used,
            player_plots: vec![0; nodes.count()],
        }
    }
}

impl Component for LandState {
    fn key(&self) -> &'static str {
        "agents.land"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.plots.len() as i32);
        for n in 0..self.plots.len() {
            let _ = w.write_i32(self.plots[n]);
            let _ = w.write_i32(self.used[n]);
            let _ = w.write_i32(self.player_plots[n]);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_len(r, self.plots.len())?;
        for n in 0..self.plots.len() {
            self.plots[n] = r.read_i32()?;
            self.used[n] = r.read_i32()?;
            self.player_plots[n] = r.read_i32()?;
        }
        Ok(())
    }
}
