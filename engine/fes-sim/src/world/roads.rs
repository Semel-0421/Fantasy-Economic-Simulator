//! Paved roads (C# `RoadState`, DEC-054): each route's condition, who maintains it and any road
//! being built on it. Section `world.roads`, version 1.
use super::extras::NodeBanks;
use super::nodes::NodeTable;
use super::routes::RouteTable;
use super::Component;
use crate::error::SimError;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{Fx, RouteTerrain, WorldLevel};
use fes_data::MapDef;

pub const MAX_CONDITION: i32 = 1000;
/// Owner value for roads the player built; nation-maintained roads hold the nation index.
pub const PLAYER_OWNER: i32 = -2;
pub const NO_OWNER: i32 = -1;

/// Condition in permille (0 = unpaved, 1000 = fully paved), maintainer and build-done turn per route.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct RoadState {
    pub condition: Vec<i32>,
    /// Nation index maintaining the route, `PLAYER_OWNER` or `NO_OWNER`.
    pub owner: Vec<i32>,
    /// Turn the road under construction is finished, or -1.
    pub build_done_turn: Vec<i32>,
}

impl RoadState {
    /// Roads paved in the map start in full condition, maintained by the nation of their start node.
    pub fn new(
        map: &MapDef,
        routes: &RouteTable,
        banks: &NodeBanks,
        nodes: &NodeTable,
    ) -> Result<Self, SimError> {
        let mut state = Self::with_routes(routes.count());
        for (r, def) in map.routes.iter().enumerate() {
            if !def.paved {
                continue;
            }
            if RouteTerrain::from_u8(routes.terrain[r]).is_some_and(RouteTerrain::is_water) {
                return Err(SimError::Invalid(format!(
                    "Route {}->{}: water routes cannot be paved.",
                    def.from_key, def.to_key
                )));
            }
            state.condition[r] = MAX_CONDITION;
            let nation = nodes.ancestor_at(routes.from[r], WorldLevel::Nation);
            state.owner[r] = if nation >= 0 {
                banks.nation_index(nodes, nation)
            } else {
                NO_OWNER
            };
        }
        Ok(state)
    }

    /// An unpaved state for `count` routes, ready to be read into.
    pub fn with_routes(count: usize) -> Self {
        Self {
            condition: vec![0; count],
            owner: vec![NO_OWNER; count],
            build_done_turn: vec![-1; count],
        }
    }

    pub fn is_paved(&self, route: usize) -> bool {
        self.condition[route] > 0
    }

    /// Share of a paving effect that applies: the effect's full size scaled by the condition.
    pub fn effect(&self, route: usize, full_effect: Fx) -> Fx {
        let share = Fx::from_ratio(i64::from(self.condition[route]), i64::from(MAX_CONDITION));
        full_effect * share
    }
}

impl Component for RoadState {
    fn key(&self) -> &'static str {
        "world.roads"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.condition.len() as i32);
        for r in 0..self.condition.len() {
            let _ = w.write_i32(self.condition[r]);
            let _ = w.write_i32(self.owner[r]);
            let _ = w.write_i32(self.build_done_turn[r]);
        }
    }

    /// A saved route count other than this state's is `IoError::InvalidLength` (C#: `InvalidDataException`).
    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        let count = r.read_i32()?;
        if count < 0 || count as usize != self.condition.len() {
            return Err(IoError::InvalidLength);
        }
        for i in 0..self.condition.len() {
            self.condition[i] = r.read_i32()?;
            self.owner[i] = r.read_i32()?;
            self.build_done_turn[i] = r.read_i32()?;
        }
        Ok(())
    }
}
