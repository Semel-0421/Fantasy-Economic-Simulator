//! The route charges and dangers of C# `TravelCosts` that merchants use every turn
//! (SYS-TRAVEL-COSTS, SYS-ROUTE-HAZARDS). A pure function of the roads, the development state and
//! the purses; nothing is stored.
use super::development::DevelopmentState;
use super::purses::PurseState;
use crate::world::{RoadState, RouteHazards, RouteTable};
use fes_core::{Fx, RouteTerrain};
use fes_data::SimulationConfig;

/// What `TravelCosts` reads.
pub struct TravelCosts<'a> {
    pub config: &'a SimulationConfig,
    pub routes: &'a RouteTable,
    pub hazards: &'a RouteHazards,
    pub roads: &'a RoadState,
    pub development: &'a DevelopmentState,
    pub purses: &'a PurseState,
}

impl TravelCosts<'_> {
    fn is_water(&self, route: usize) -> bool {
        RouteTerrain::from_u8(self.routes.terrain[route]).is_some_and(|t| t.is_water())
    }

    /// The route's charge per cargo unit, cut by paving and by the states' highways and harbors
    /// (C# `PerUnitCharge`, DEC-054, DEC-072).
    pub fn per_unit_charge(&self, route: usize) -> Fx {
        let paving = Fx::ONE - self.roads.effect(route, self.config.road_cost_reduction);
        Fx::from_int(i64::from(self.routes.cost_per_unit[route]))
            * paving
            * self.development_factor(route)
    }

    fn development_factor(&self, route: usize) -> Fx {
        if self.development.route_grade[route] > 0 {
            return Fx::ONE - self.config.state_highway_bonus;
        }
        let harbor = self.development.harbor_level[self.routes.from[route] as usize];
        if harbor > 0 && self.is_water(route) {
            Fx::ONE - self.config.state_harbor_bonus * Fx::from_int(i64::from(harbor))
        } else {
            Fx::ONE
        }
    }

    /// The terrain and region danger of a route, cut by paving and the nation's army (C#
    /// `RouteDanger`, DEC-054, DEC-074).
    pub fn route_danger(&self, route: usize) -> Fx {
        let paving = Fx::ONE - self.roads.effect(route, self.config.road_danger_reduction);
        Fx::from_raw(self.hazards.base_danger[route]) * paving * self.army_factor(route)
    }

    fn army_factor(&self, route: usize) -> Fx {
        let nation = self.purses.node_float[self.routes.from[route] as usize];
        match self.development.security.get(nation as usize) {
            Some(&security) => Fx::ONE - Fx::from_raw(security),
            None => Fx::ONE,
        }
    }
}
