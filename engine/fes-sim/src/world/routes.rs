//! Directed trade links between markets (C# `RouteTable` and `WorldBuilder.BuildRoutes`).
use super::nodes::{build_csr, NodeTable};
use crate::error::SimError;
use fes_data::{MapDef, SimulationConfig};

/// Directed routes with travel time and per-unit transport cost. Static after build.
#[derive(Clone, Debug)]
pub struct RouteTable {
    pub from: Vec<i32>,
    pub to: Vec<i32>,
    pub travel_turns: Vec<i32>,
    pub cost_per_unit: Vec<i32>,
    pub base_fee: Vec<i32>,
    pub terrain: Vec<u8>,
    /// CSR outgoing routes: routes of node n are `route_index[out_start[n]..out_start[n] + out_count[n]]`.
    pub out_start: Vec<i32>,
    pub out_count: Vec<i32>,
    pub route_index: Vec<i32>,
}

impl RouteTable {
    pub fn count(&self) -> usize {
        self.from.len()
    }
}

/// Hard terrain adds its travel turns and scales the route's fees (DEC-025). Everything is resolved
/// and validated before the table is made, so a bad route builds nothing.
pub fn build_routes(
    map: &MapDef,
    nodes: &NodeTable,
    config: &SimulationConfig,
) -> Result<RouteTable, SimError> {
    let count = map.routes.len();
    let mut from = Vec::with_capacity(count);
    let mut to = Vec::with_capacity(count);
    for r in &map.routes {
        let f = nodes.try_find(&r.from_key).ok_or_else(|| {
            SimError::Invalid(format!("Route from unknown node '{}'.", r.from_key))
        })?;
        let t = nodes
            .try_find(&r.to_key)
            .ok_or_else(|| SimError::Invalid(format!("Route to unknown node '{}'.", r.to_key)))?;
        if r.travel_turns < 1 {
            return Err(SimError::Invalid(format!(
                "Route {}->{}: travel_turns must be >= 1.",
                r.from_key, r.to_key
            )));
        }
        if r.cost_per_unit < 0 {
            return Err(SimError::Invalid(format!(
                "Route {}->{}: cost_per_unit must be >= 0.",
                r.from_key, r.to_key
            )));
        }
        from.push(f.0);
        to.push(t.0);
    }

    let mut table = RouteTable {
        from: from.clone(),
        to,
        travel_turns: Vec::with_capacity(count),
        cost_per_unit: Vec::with_capacity(count),
        base_fee: Vec::with_capacity(count),
        terrain: Vec::with_capacity(count),
        out_start: Vec::new(),
        out_count: Vec::new(),
        route_index: Vec::new(),
    };
    for r in &map.routes {
        let terrain = r.terrain as usize;
        table.terrain.push(r.terrain as u8);
        table.travel_turns.push(
            r.travel_turns
                .wrapping_add(config.terrain_extra_turns[terrain]),
        );
        let cost = config.terrain_cost_multiplier[terrain];
        table
            .cost_per_unit
            .push(cost.mul_int(i64::from(r.cost_per_unit)) as i32);
        table
            .base_fee
            .push(cost.mul_int(i64::from(r.base_fee)) as i32);
    }
    let (start, counts, items) = build_csr(&from, nodes.count(), count);
    table.out_start = start;
    table.out_count = counts;
    table.route_index = items;
    Ok(table)
}
