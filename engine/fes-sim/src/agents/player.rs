//! The peddler the player controls (C# `PlayerState` in `AgentTables.cs`, `agents.player`, version 3).
use super::columns::{read_items, write_col};
use crate::error::SimError;
use crate::world::{Component, NodeTable};
use fes_core::det::det_hash;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{DetRandom, WorldLevel};
use fes_data::SimulationConfig;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PlayerState {
    node_count: i32,
    pub node: i32,
    /// Coins carried, per continent currency.
    pub coins: Vec<i64>,
    /// Node the player is travelling to, or -1.
    pub travel_destination: i32,
    /// Turn on which the player reaches the destination.
    pub arrival_turn: i32,
}

impl PlayerState {
    pub fn new(nodes: &NodeTable) -> Self {
        Self {
            node_count: nodes.count() as i32,
            node: 0,
            coins: vec![0; nodes.continent_count() as usize],
            travel_destination: -1,
            arrival_turn: -1,
        }
    }

    pub fn is_travelling(&self) -> bool {
        self.travel_destination >= 0
    }

    /// The player at its start node, with the start cash in the local coin.
    pub fn at_start(
        nodes: &NodeTable,
        config: &SimulationConfig,
        seed: u64,
    ) -> Result<Self, SimError> {
        let mut player = Self::new(nodes);
        player.node = resolve_start_node(config, nodes, seed)?;
        let continent = nodes.continent[player.node as usize] as usize;
        player.coins[continent] = config.player_start_cash;
        Ok(player)
    }
}

/// A random village from the world seed, unless config names a start node (C# `ResolveStartNode`).
pub fn resolve_start_node(
    config: &SimulationConfig,
    nodes: &NodeTable,
    seed: u64,
) -> Result<i32, SimError> {
    if !config.player_start_node.is_empty() {
        return nodes
            .try_find(&config.player_start_node)
            .map(|n| n.0)
            .ok_or_else(|| {
                SimError::Invalid(format!(
                    "player.start_node '{}' is not on the map.",
                    config.player_start_node
                ))
            });
    }
    let villages = nodes.level_count(WorldLevel::Village);
    if villages == 0 {
        return Ok(0);
    }
    let stream = det_hash::of("agents.player.start");
    let draw = DetRandom::for_key(seed, 0, stream, 0, 0).next_int(villages);
    Ok(nodes.level_start(WorldLevel::Village) + draw)
}

impl Component for PlayerState {
    fn key(&self) -> &'static str {
        "agents.player"
    }

    fn version(&self) -> i32 {
        3
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.node);
        let _ = w.write_i32(self.travel_destination);
        let _ = w.write_i32(self.arrival_turn);
        write_col(w, &self.coins);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        let node = r.read_i32()?;
        let destination = r.read_i32()?;
        if node < 0 || node >= self.node_count || destination < -1 || destination >= self.node_count
        {
            return Err(IoError::InvalidLength);
        }
        self.node = node;
        self.travel_destination = destination;
        self.arrival_turn = r.read_i32()?;
        let count = r.read_i32()?;
        if count < 0 || count as usize != self.coins.len() {
            return Err(IoError::InvalidLength);
        }
        read_items(r, &mut self.coins)
    }
}
