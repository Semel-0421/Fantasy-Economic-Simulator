//! The merchants of aggregate nodes (C# `ArbitrageMerchantBehavior.RunAggregate` and
//! `PoolIdleGroups`): idle automated groups pool their capacity and their traits average out, then
//! `MerchantFlowJob` moves goods along the routes. With `lod.mode = 1` every node is detailed and
//! nothing happens here.
use super::flow_job::FlowJob;
use super::statics::Statics;
use super::traits::MerchantTraitTable;
use super::AgentTable;
use crate::agents::facilities::MODE_AUTOMATED;
use crate::agents::{BankState, DevelopmentState, LodState, PolicyState, PurseState};
use crate::context::SimContext;
use crate::economy::{HouseholdCash, MarketStore};
use crate::world::{NodeTable, NodeTolls, RouteTable};
use fes_core::{Fx, SimDetail};
use std::mem::take;

/// Per node: the pooled capacity of its idle groups and their averaged traits; per route: the
/// weight still to move this week.
#[derive(Default)]
pub struct Pool {
    pub capacity: Vec<i64>,
    pub spend: Vec<i64>,
    pub min_margin: Vec<i64>,
    pub vehicles: Vec<i64>,
    pub groups: Vec<i64>,
    pub vehicle_cost: Vec<i64>,
    pub vehicle_danger: Vec<i64>,
    pub route_budget: Vec<i64>,
}

impl Pool {
    /// C# `PoolIdleGroups`.
    pub fn pool_idle_groups(
        &mut self,
        ctx: &SimContext,
        traits: &MerchantTraitTable,
        statics: &Statics,
    ) {
        let nodes = ctx.services.get::<NodeTable>().count();
        for v in [
            &mut self.capacity,
            &mut self.spend,
            &mut self.min_margin,
            &mut self.vehicles,
            &mut self.groups,
        ] {
            v.clear();
            v.resize(nodes, 0);
        }
        for v in [&mut self.vehicle_cost, &mut self.vehicle_danger] {
            v.clear();
            v.resize(nodes, Fx::ONE_RAW);
        }
        let agents = ctx.world.get::<AgentTable>();
        let detail = &ctx.services.get::<LodState>().detail;
        let detailed = |n: i32| detail[n as usize] == SimDetail::Detailed as u8;
        let max_weight = &ctx.services.database.config.merchant_max_weight;
        for a in 0..agents.count() {
            let n = agents.node[a];
            if detailed(n)
                || detailed(agents.home[a])
                || agents.destination[a] >= 0
                || agents.mode[a] != MODE_AUTOMATED
            {
                continue;
            }
            let (n, t, v) = (
                n as usize,
                usize::from(agents.trait_id[a]),
                usize::from(agents.vehicle[a]),
            );
            let base = Fx::mul_int_raw(
                max_weight[usize::from(agents.scale[a])],
                traits.capacity_raw[t],
            );
            let capacity = Fx::mul_int_raw(base, statics.vehicle_capacity[v]);
            self.vehicles[n] += statics.vehicle_capacity[v];
            if self.groups[n] == 0 {
                self.vehicle_cost[n] = 0;
                self.vehicle_danger[n] = 0;
            }
            self.vehicle_cost[n] += statics.vehicle_cost[v];
            self.vehicle_danger[n] += statics.vehicle_danger[v];
            self.groups[n] += 1;
            self.capacity[n] += capacity;
            self.spend[n] += capacity * traits.spend_raw[t];
            self.min_margin[n] += capacity * traits.min_margin_raw[t];
        }
        for n in 0..nodes {
            if self.capacity[n] <= 0 {
                continue;
            }
            // Better vehicles dare move a larger share of the stock (DEC-084).
            let spend = Fx::from_raw(self.spend[n] / self.capacity[n]);
            self.spend[n] = (spend * Fx::from_raw(self.vehicles[n] / self.groups[n])).raw();
            self.vehicle_cost[n] /= self.groups[n];
            self.vehicle_danger[n] /= self.groups[n];
            self.min_margin[n] /= self.capacity[n];
        }
    }
}

/// The per-route costs and the coin rates the flow reads.
pub struct FlowInputs<'a> {
    pub statics: &'a Statics,
    pub coin_rate: &'a [i64],
    pub route_cost: &'a [i64],
    pub route_expected_loss: &'a [i64],
}

/// C# `RunAggregate` after `PoolIdleGroups`: the flow job, with the international reserves.
pub fn run_flow(ctx: &mut SimContext, pool: &mut Pool, inputs: &FlowInputs) {
    let config = &ctx.services.database.config;
    let w = &mut ctx.world;
    let mut international = take(&mut w.get_mut::<BankState>().international_reserve);
    let mut household_cash = take(&mut w.get_mut::<HouseholdCash>().cash);
    let mut float = take(&mut w.get_mut::<PurseState>().float);
    let mut imports = take(&mut w.get_mut::<DevelopmentState>().imports);
    let markets = w.get_mut::<MarketStore>();
    let (mut stock, mut supply, mut demand) = (
        take(&mut markets.stock),
        take(&mut markets.supply),
        take(&mut markets.demand),
    );
    let markets = ctx.world.get::<MarketStore>();
    let nodes = ctx.services.get::<NodeTable>();
    let routes = ctx.services.get::<RouteTable>();
    let tolls = ctx.services.get::<NodeTolls>();
    let policy = ctx.world.get::<PolicyState>();
    pool.route_budget.resize(routes.count(), 0);
    FlowJob {
        category_count: inputs.statics.category_count as i32,
        flow_share_raw: config.merchant_flow_share.raw(),
        licensed_share_raw: Fx::from_permille(i64::from(config.licensed_merchant_permille)).raw(),
        base_good_count: policy.base_good_count as i32,
        cross_flow_raw: config.merchant_cross_flow.raw(),
        continent_count: nodes.continent_count(),
        coin_rate: inputs.coin_rate,
        exchange_fee_raw: config.exchange_fee.raw(),
        international: &mut international,
        wage_raw: config.merchant_flow_wage.raw(),
        household_cash: &mut household_cash,
        detail: &ctx.services.get::<LodState>().detail,
        continent: &nodes.continent,
        capacity: &pool.capacity,
        spend_raw: &pool.spend,
        min_margin_raw: &pool.min_margin,
        vehicle_cost_raw: &pool.vehicle_cost,
        vehicle_danger_raw: &pool.vehicle_danger,
        category_specialty: &inputs.statics.category_specialty,
        tariff: &tolls.tariff,
        surcharge: &tolls.specialty_surcharge,
        out_start: &routes.out_start,
        out_count: &routes.out_count,
        route_index: &routes.route_index,
        route_to: &routes.to,
        route_turns: &routes.travel_turns,
        route_cost_raw: inputs.route_cost,
        route_budget: &mut pool.route_budget,
        route_expected_loss: inputs.route_expected_loss,
        cell_start: &markets.market_cell_start,
        cell_count: &markets.market_cell_count,
        cell_good: &markets.cell_good,
        good_category: &markets.good_category,
        good_weight: &markets.good_weight,
        license_row: &inputs.statics.license_row,
        licensed: &policy.licensed,
        price: &markets.price,
        node_float: &ctx.world.get::<PurseState>().node_float,
        float: &mut float,
        imports: &mut imports,
        stock: &mut stock,
        supply: &mut supply,
        demand: &mut demand,
    }
    .execute();
    let w = &mut ctx.world;
    w.get_mut::<BankState>().international_reserve = international;
    w.get_mut::<HouseholdCash>().cash = household_cash;
    w.get_mut::<PurseState>().float = float;
    w.get_mut::<DevelopmentState>().imports = imports;
    let markets = w.get_mut::<MarketStore>();
    markets.stock = stock;
    markets.supply = supply;
    markets.demand = demand;
}
