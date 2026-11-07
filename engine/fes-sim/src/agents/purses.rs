//! Traders' floats (C# `Purses.cs`, section `agents.purses`, version 2): one purse per nation, then
//! one per continent for nodes outside any nation. Turn-0 state; the spending systems come later.
use super::columns::{read_col, write_col};
use crate::world::{Component, NodeBanks, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::WorldLevel;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PurseState {
    pub nation_count: usize,
    pub continent_count: usize,
    /// Per purse (nations first, then continents): the traders' coins.
    pub float: Vec<i64>,
    /// Per node: the purse its trade goes through.
    pub node_float: Vec<i32>,
    /// Per nation: its nodes, in node order.
    pub nation_nodes: Vec<Vec<i32>>,
    continent_of_float: Vec<i32>,
}

impl PurseState {
    pub fn new(nodes: &NodeTable, banks: &NodeBanks) -> Self {
        let nation_count = banks.nation_count(nodes) as usize;
        let continent_count = nodes.continent_count() as usize;
        let mut continent_of_float = vec![0i32; nation_count + continent_count];
        for (n, slot) in continent_of_float.iter_mut().enumerate().take(nation_count) {
            *slot = nodes.continent[banks.nation_node(nodes, n as i32) as usize];
        }
        for c in 0..continent_count {
            continent_of_float[nation_count + c] = c as i32;
        }
        let mut node_float = vec![0i32; nodes.count()];
        let mut nation_nodes = vec![Vec::new(); nation_count];
        for (node, slot) in node_float.iter_mut().enumerate() {
            let nation_node = nodes.ancestor_at(node as i32, WorldLevel::Nation);
            *slot = if nation_node >= 0 {
                banks.nation_index(nodes, nation_node)
            } else {
                nation_count as i32 + nodes.continent[node]
            };
            if (*slot as usize) < nation_count {
                nation_nodes[*slot as usize].push(node as i32);
            }
        }
        Self {
            nation_count,
            continent_count,
            float: vec![0; nation_count + continent_count],
            node_float,
            nation_nodes,
            continent_of_float,
        }
    }

    /// Number of purses.
    pub fn count(&self) -> usize {
        self.float.len()
    }

    /// The continent a purse's coins belong to.
    pub fn continent_of(&self, purse: usize) -> i32 {
        self.continent_of_float[purse]
    }
}

impl Component for PurseState {
    fn key(&self) -> &'static str {
        "agents.purses"
    }

    fn version(&self) -> i32 {
        2
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.float);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.float)
    }
}
