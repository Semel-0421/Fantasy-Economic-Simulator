//! Where the states and groups may build, and nation-wide price sums (C# `FacilityPlaces`,
//! `NationMarkets`, `DevelopmentState.LandProjectsAt`, `TravelCosts.FindRoute`/`IsWater`).
use super::development::DevelopmentState;
use super::groups::{GroupTable, KIND_STATE_ENTERPRISE};
use crate::economy::MarketStore;
use crate::world::{NodeTable, RouteTable};
use fes_core::{RouteTerrain, WorldLevel};
use fes_data::defs::FacilityDef;

/// C# `StateProjectKind`.
pub mod project_kind {
    pub const FARMLAND: u8 = 0;
    pub const MINE: u8 = 1;
    pub const HARBOR: u8 = 2;
    pub const HIGHWAY: u8 = 3;
    pub const ROAD: u8 = 4;
}

/// Whether anything can be built at a node: a class has no land of its own (DEC-105).
pub fn is_land(nodes: &NodeTable, node: i32) -> bool {
    !nodes.absorbed() || nodes.level[node as usize] >= WorldLevel::City as u8
}

/// C# `FacilityPlaces.Allowed`.
pub fn allowed(def: &FacilityDef, nodes: &NodeTable, node: i32) -> bool {
    if nodes.absorbed()
        && def.core_only
        && nodes.level[node as usize] >= WorldLevel::City as u8
        && !nodes.is_seat(node)
    {
        return false; // DEC-104
    }
    match &def.continent {
        None => true,
        Some(key) => key == nodes.key_of(nodes.ancestor_at(node, WorldLevel::Continent)),
    }
}

/// C# `NationMarkets.AddPrices`: a good's price and anchor at each of the nodes carrying it.
pub fn add_prices(
    markets: &MarketStore,
    nodes: &[i32],
    good: i32,
    price: &mut i64,
    anchor: &mut i64,
) {
    for &node in nodes {
        if let Some(cell) = markets.try_find_cell(node as usize, good) {
            *price += i64::from(markets.price[cell]);
            *anchor += i64::from(markets.good_base_price[good as usize]);
        }
    }
}

impl DevelopmentState {
    /// C# `LandProjectsAt`: farmland and mine projects planned or under way at a node.
    pub fn land_projects_at(&self, node: i32) -> i32 {
        self.projects
            .iter()
            .filter(|p| {
                p.node == node && (p.kind == project_kind::FARMLAND || p.kind == project_kind::MINE)
            })
            .count() as i32
    }
}

/// C# `TravelCosts.FindRoute`.
pub fn find_route(routes: &RouteTable, from: i32, to: i32) -> i32 {
    let start = routes.out_start[from as usize];
    (start..start + routes.out_count[from as usize])
        .map(|k| routes.route_index[k as usize])
        .find(|&r| routes.to[r as usize] == to)
        .unwrap_or(-1)
}

/// C# `TravelCosts.IsWater`.
pub fn is_water(routes: &RouteTable, route: usize) -> bool {
    RouteTerrain::from_u8(routes.terrain[route]).is_some_and(|t| t.is_water())
}

/// C# `GroupDirectory.StateEnterpriseOf`: the last state enterprise row at the nation node, or -1.
pub fn state_enterprise_of(groups: &GroupTable, nation_node: i32) -> i32 {
    (0..groups.count())
        .rposition(|g| groups.kind[g] == KIND_STATE_ENTERPRISE && groups.node[g] == nation_node)
        .map_or(-1, |g| g as i32)
}
