//! What C# `ArbitrageMerchantBehavior.Initialize` derives once from the map and config: per-node
//! specialty categories, ports for crossers, the way toward each continent, licensing rows, and
//! the per-level and per-vehicle limits.
use super::job::LevelLimits;
use crate::agents::PolicyState;
use crate::context::SimContext;
use crate::economy::MarketFlows;
use crate::world::{NodeBanks, NodeSpecialties, NodeTable, RouteTable};
use fes_core::QualityGrade;
use std::collections::VecDeque;

#[derive(Default)]
pub struct Statics {
    pub category_count: usize,
    /// Per (node, category): 1 when one of the node's specialty goods is in it.
    pub category_specialty: Vec<u8>,
    /// Per node: a large enough group based here may sail whatever its personality.
    pub node_port: Vec<u8>,
    /// Per (node, continent): the first route of the fewest hops there, or -1.
    pub route_toward: Vec<i32>,
    /// Per node: its row in `PolicyState::licensed`.
    pub license_row: Vec<i32>,
    pub levels: Vec<LevelLimits>,
    pub vehicle_capacity: Vec<i64>,
    pub vehicle_cost: Vec<i64>,
    pub vehicle_danger: Vec<i64>,
    pub vehicle_premium: Vec<i64>,
}

impl Statics {
    pub fn new(ctx: &SimContext) -> Self {
        let db = &ctx.services.database;
        let c = &db.config;
        let nodes = ctx.services.get::<NodeTable>();
        let routes = ctx.services.get::<RouteTable>();
        let banks = ctx.services.get::<NodeBanks>();
        let categories = ctx.services.get::<MarketFlows>().category_count;
        let specialties = ctx.services.get::<NodeSpecialties>();
        let mut category_specialty = vec![0u8; nodes.count() * categories];
        for n in 0..nodes.count() {
            let start = specialties.start[n] as usize;
            for k in start..start + specialties.count[n] as usize {
                let low = specialties.base_good[k] as usize * QualityGrade::COUNT;
                let category = db.goods[low].category_index as usize;
                category_specialty[n * categories + category] = 1;
            }
        }
        let mut node_port = vec![0u8; nodes.count()];
        for r in 0..routes.count() {
            let (from, to) = (routes.from[r] as usize, routes.to[r] as usize);
            if c.merchant_port_crossers && nodes.continent[from] != nodes.continent[to] {
                node_port[from] = 1;
            }
        }
        let policy = ctx.world.get::<PolicyState>();
        let license_row = (0..nodes.count() as i32)
            .map(|n| match banks.nation_of(nodes, n) {
                nation if nation < 0 => policy.nation_count as i32,
                nation => nation,
            })
            .collect();
        let levels = (0..c.merchant_max_weight.len())
            .map(|l| LevelLimits {
                max_weight: c.merchant_max_weight[l],
                max_size: c.merchant_max_size[l],
                upkeep: c.merchant_upkeep[l],
            })
            .collect();
        Self {
            category_count: categories,
            category_specialty,
            node_port,
            route_toward: ways_toward(nodes, routes),
            license_row,
            levels,
            vehicle_capacity: c
                .merchant_vehicle_capacity
                .iter()
                .map(|f| f.raw())
                .collect(),
            vehicle_cost: c.merchant_vehicle_cost.iter().map(|f| f.raw()).collect(),
            vehicle_danger: c.merchant_vehicle_danger.iter().map(|f| f.raw()).collect(),
            vehicle_premium: c.merchant_vehicle_premium.iter().map(|f| f.raw()).collect(),
        }
    }
}

/// C# `PrepareWaysHome`: a breadth-first search back from each continent's nodes.
fn ways_toward(nodes: &NodeTable, routes: &RouteTable) -> Vec<i32> {
    let continents = nodes.continent_count() as usize;
    let mut toward = vec![-1i32; nodes.count() * continents];
    for c in 0..continents {
        let mut seen: Vec<bool> = nodes.continent.iter().map(|&k| k == c as i32).collect();
        let mut frontier: VecDeque<i32> = (0..nodes.count() as i32)
            .filter(|&n| seen[n as usize])
            .collect();
        while let Some(at) = frontier.pop_front() {
            let mut way = Way {
                seen: &mut seen,
                toward: &mut toward[..],
                frontier: &mut frontier,
            };
            way.step_back(routes, at, c, continents);
        }
    }
    toward
}

/// One continent's search state in `ways_toward`.
struct Way<'a> {
    seen: &'a mut [bool],
    toward: &'a mut [i32],
    frontier: &'a mut VecDeque<i32>,
}

impl Way<'_> {
    /// Every unseen node with a route into `at` goes that way toward continent `c`.
    fn step_back(&mut self, routes: &RouteTable, at: i32, c: usize, continents: usize) {
        for r in 0..routes.count() {
            let from = routes.from[r] as usize;
            if routes.to[r] != at || self.seen[from] {
                continue;
            }
            self.seen[from] = true;
            self.toward[from * continents + c] = r as i32;
            self.frontier.push_back(from as i32);
        }
    }
}
