//! A group that spent too many weeks below zero is replaced by a new one at its home (C#
//! `ArbitrageMerchantBehavior.GoBankrupt`, DEC-011).
use super::{capital_start, AgentTable, NodeMemory, VEHICLE_PACK};
use crate::agents::money_ledger::fund_new_operator;
use crate::agents::{
    BillTable, CargoTable, IncomeTaxState, MerchantGenetics, MerchantTripStats, NpcLoanTable,
};
use crate::context::SimContext;
use crate::world::NodeTable;
use fes_core::det::det_hash;
use fes_core::DetRandom;
use std::sync::Arc;

/// C# `CargoHolders.MerchantBase`.
pub const MERCHANT_HOLDER_BASE: i32 = 2 << 28;
/// C# `NpcBorrowers.Merchant` base.
const MERCHANT_BORROWER_BASE: i32 = 1 << 28;
/// C# `BillHolders.Households`.
const BILL_HOLDER_HOUSEHOLDS: i32 = -2;

/// A merchant group went bankrupt and was replaced (C# `MerchantBankrupt`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MerchantBankrupt {
    pub merchant: i32,
    pub turn: i32,
}

pub fn go_bankrupt(ctx: &mut SimContext, a: usize) {
    let db = Arc::clone(&ctx.services.database);
    let config = &db.config;
    let (seed, turn) = (ctx.seed(), ctx.turn());
    let w = &mut ctx.world;
    w.get_mut::<NpcLoanTable>()
        .write_off(MERCHANT_BORROWER_BASE + a as i32);
    w.get_mut::<CargoTable>()
        .clear(MERCHANT_HOLDER_BASE + a as i32);
    let bills = w.get_mut::<BillTable>();
    for h in bills.holder.iter_mut().filter(|h| **h == a as i32) {
        *h = BILL_HOLDER_HOUSEHOLDS;
    }
    let agents = w.get_mut::<AgentTable>();
    let lost = agents.load_cost[a];
    agents.load_cost[a] = 0;
    agents.hedge_floor[a] = 0;
    agents.node[a] = agents.home[a];
    agents.destination[a] = -1;
    agents.arrival_turn[a] = -1;
    let (group, home, old_cash) = (agents.group[a], agents.home[a], agents.cash[a]);
    ctx.services.get_mut::<MerchantTripStats>().lost_with_groups += lost;
    let nodes = ctx.services.get::<NodeTable>();
    let start = capital_start(config, seed, a as i32);
    let cash = fund_new_operator(&mut ctx.world, nodes, group, home, old_cash, start);
    ctx.world
        .get_mut::<IncomeTaxState>()
        .restart_merchant(a, cash);
    let continent = nodes.continent[home as usize];
    let (agents, genetics) = ctx.world.get2_mut::<AgentTable, MerchantGenetics>();
    agents.cash[a] = cash;
    agents.negative_cash_weeks[a] = 0;
    agents.memory[a] = NodeMemory::EMPTY;
    if config.merchant_traits_enabled {
        // The replacement is a new generation: its trait follows what has been working.
        genetics.record_bankruptcy(continent, agents.trait_id[a]);
        let stream = det_hash::of("agents.merchant_bankrupt");
        let mut rng = DetRandom::for_key(seed, turn, stream, a as i32, 0);
        agents.trait_id[a] = genetics.draw(&mut rng, continent, config.merchant_mutation_share);
    }
    agents.vehicle[a] = VEHICLE_PACK; // sold off with the old group
    ctx.events.publish(MerchantBankrupt {
        merchant: a as i32,
        turn,
    });
}
