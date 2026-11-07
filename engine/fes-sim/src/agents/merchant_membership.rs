//! `agents.merchant_membership` (C# `MerchantMembershipSystem`, DEC-086): each year a group not in
//! an association may join the richest guild of its home's city, else the richest company of its
//! nation. Members pay a yearly fee on cash above their starting cash, and the association tops up
//! a member running short from a share of its own cash.
use super::facilities::OWNER_NPC;
use super::finance::IncomeTaxState;
use super::groups::{city_of, GroupTable, KIND_COMPANY, KIND_GUILD};
use super::merchants::{capital_start, AgentTable};
use super::orders::MerchantTripStats;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeTable, WorldState};
use fes_core::det::det_hash;
use fes_core::{DetRandom, WorldLevel};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.MerchantMembership`.
pub const ORDER: i32 = sim_order::LATE + 25;

/// `agents.merchant_membership`. `open_at` (per node, the groups a merchant could join there, in
/// table order) is rebuilt each new year before use.
#[derive(Default)]
pub struct MerchantMembershipSystem {
    open_at: Vec<Vec<i32>>,
}

impl System for MerchantMembershipSystem {
    fn id(&self) -> &'static str {
        "agents.merchant_membership"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let config = &ctx.services.database.config;
        if config.merchant_join_share.raw() <= 0 {
            return Ok(());
        }
        let (seed, turn) = (ctx.seed(), ctx.turn());
        let nodes = ctx.services.get::<NodeTable>();
        let year = ctx.clock().is_new_year();
        let world = &mut ctx.world;
        if year {
            self.list_open_groups(world.get::<GroupTable>(), nodes.count());
        }
        let stream = det_hash::of("agents.merchant_join");
        let mut fees = 0i64;
        for a in 0..world.get::<AgentTable>().count() {
            let agents = world.get::<AgentTable>();
            if agents.owner[a] != OWNER_NPC {
                continue;
            }
            let association = agents.association[a];
            if association >= 0 {
                fees += deal(world, config, seed, a, association as usize, year);
            } else if year
                && DetRandom::for_key(seed, turn, stream, a as i32, 0)
                    .next_fx01()
                    .raw()
                    < config.merchant_join_share.raw()
            {
                let found = self.find(world.get::<GroupTable>(), nodes, agents.home[a]);
                world.get_mut::<AgentTable>().association[a] = found;
            }
        }
        ctx.services
            .get_mut::<MerchantTripStats>()
            .spent_fees_to_associations += fees;
        Ok(())
    }
}

impl MerchantMembershipSystem {
    fn find(&self, groups: &GroupTable, nodes: &NodeTable, home: i32) -> i32 {
        let city = city_of(nodes, home);
        let kind = if city >= 0 { KIND_GUILD } else { KIND_COMPANY };
        let at = if city >= 0 {
            city
        } else {
            nodes.ancestor_at(home, WorldLevel::Nation)
        };
        let mut best = -1i32;
        for &g in &self.open_at[at as usize] {
            let gu = g as usize;
            if groups.kind[gu] == kind && (best < 0 || groups.cash[gu] > groups.cash[best as usize])
            {
                best = g;
            }
        }
        best
    }

    fn list_open_groups(&mut self, groups: &GroupTable, node_count: usize) {
        self.open_at.resize_with(node_count, Vec::new);
        for list in &mut self.open_at {
            list.clear();
        }
        for g in 0..groups.count() {
            let kind = groups.kind[g];
            if groups.is_player[g] == 0 && (kind == KIND_GUILD || kind == KIND_COMPANY) {
                self.open_at[groups.node[g] as usize].push(g as i32);
            }
        }
    }
}

/// C# `DealWithAssociation`; returns the fee paid.
fn deal(
    world: &mut WorldState,
    config: &SimulationConfig,
    seed: u64,
    a: usize,
    association: usize,
    year: bool,
) -> i64 {
    let start = capital_start(config, seed, a as i32);
    let (agents, groups) = world.get2_mut::<AgentTable, GroupTable>();
    let mut fee = 0;
    if year && agents.cash[a] > start {
        fee = config.merchant_member_fee.mul_int(agents.cash[a] - start);
        agents.cash[a] -= fee;
        groups.cash[association] += fee;
    }
    let floor = config.merchant_support_floor.mul_int(start);
    if agents.cash[a] >= floor {
        return fee;
    }
    let grant = (floor - agents.cash[a]).min(
        config
            .merchant_support_share
            .mul_int(groups.cash[association].max(0)),
    );
    if grant <= 0 {
        return fee;
    }
    groups.cash[association] -= grant;
    agents.cash[a] += grant;
    world
        .get_mut::<IncomeTaxState>()
        .move_merchant_capital(a, grant);
    fee
}
