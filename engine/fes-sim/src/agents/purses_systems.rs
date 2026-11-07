//! `agents.household_spending` and `agents.home_surplus` (C# `HouseholdSpendingSystem` and
//! `HomeSurplusSystem`, `Agents/Purses.cs`): what households paid for consumption goes to the
//! traders' float of their nation, and what they made at home and did not eat goes to market.
use super::labor::LaborState;
use super::money_ledger::to_state;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::{HouseholdCash, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable};
use fes_core::Fx;

/// C# `HouseholdCash.LuxuryTax`: what a node paid beyond the week's necessities, taxed.
fn luxury_tax(spent: i64, desired_necessity: i64, rate_raw: i64) -> i64 {
    Fx::mul_int_raw((spent - desired_necessity).max(0), rate_raw)
}

/// `agents.household_spending`.
pub struct HouseholdSpendingSystem;

impl System for HouseholdSpendingSystem {
    fn id(&self) -> &'static str {
        "agents.household_spending"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Markets
    }

    fn order(&self) -> i32 {
        sim_order::LATE + 1
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let rate = ctx.services.database.config.luxury_tax.raw();
        let node_count = ctx.world.get::<HouseholdCash>().spent.len();
        for node in 0..node_count {
            let (spent, desired_necessity) = {
                let cash = ctx.world.get::<HouseholdCash>();
                (cash.spent[node], cash.desired_necessity[node])
            };
            if spent == 0 {
                continue;
            }
            let tax = luxury_tax(spent, desired_necessity, rate);
            if tax > 0 {
                let nodes = ctx.services.get::<NodeTable>();
                let banks = ctx.services.get::<NodeBanks>();
                to_state(&mut ctx.world, nodes, banks, node as i32, tax);
            }
            let float = ctx.world.get::<PurseState>().node_float[node] as usize;
            ctx.world.get_mut::<PurseState>().float[float] += spent - tax;
            ctx.world.get_mut::<LaborState>().quarter_spending[float] += spent;
            let cash = ctx.world.get_mut::<HouseholdCash>();
            cash.spent_year[node] += spent;
            cash.spent[node] = 0;
        }
        Ok(())
    }
}

/// `agents.home_surplus`.
pub struct HomeSurplusSystem;

impl System for HomeSurplusSystem {
    fn id(&self) -> &'static str {
        "agents.home_surplus"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Markets
    }

    fn order(&self) -> i32 {
        sim_order::LATE + 2
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let markets = ctx.world.get::<MarketStore>().market_count();
        for node in 0..markets {
            let (start, count) = {
                let m = ctx.world.get::<MarketStore>();
                (
                    m.market_cell_start[node] as usize,
                    m.market_cell_count[node] as usize,
                )
            };
            for cell in start..start + count {
                sell_if_grown(ctx, node, cell);
            }
        }
        Ok(())
    }
}

fn sell(ctx: &mut SimContext, node: usize, cell: usize) {
    let db = &ctx.services.database;
    let (price, good) = {
        let m = ctx.world.get::<MarketStore>();
        (m.price[cell], m.cell_good[cell] as usize)
    };
    // C# `TraderOffer(int)`: the spread is truncated to int.
    let offer = price - db.config.trader_spread.mul_int(i64::from(price)) as i32;
    let preserved = db.goods[good].traits.is_preserved;
    let grown = ctx.world.get::<HouseholdCash>().home_grown[cell];
    let purses = ctx.world.get::<PurseState>();
    let float = purses.float[purses.node_float[node] as usize];
    // C# `Sellable`: no price sells everything; else as many as the float pays for.
    let sold = if offer <= 0 {
        grown
    } else {
        grown.min((float.max(0) / i64::from(offer)).min(i64::from(i32::MAX)) as i32)
    };
    ctx.world.get_mut::<HouseholdCash>().home_grown[cell] =
        if preserved { grown - sold } else { 0 };
    if sold == 0 {
        return;
    }
    let supply = &mut ctx.world.get_mut::<MarketStore>().supply[cell];
    *supply = supply.wrapping_add(sold);
    let value = i64::from(sold) * i64::from(offer);
    let purses = ctx.world.get_mut::<PurseState>();
    let float_index = purses.node_float[node] as usize;
    purses.float[float_index] -= value;
    let nodes = ctx.services.get::<NodeTable>();
    let at = if nodes.absorbed() {
        nodes.core[node] as usize
    } else {
        node
    };
    ctx.world.get_mut::<HouseholdCash>().cash[at] += value;
}

fn sell_if_grown(ctx: &mut SimContext, node: usize, cell: usize) {
    if ctx.world.get::<HouseholdCash>().home_grown[cell] > 0 {
        sell(ctx, node, cell);
    }
}
