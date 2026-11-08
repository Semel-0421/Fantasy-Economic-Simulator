//! `agents.trader_loans` (C# `TraderLoanSystem`, DEC-092, DEC-098): each week a nation's traders'
//! float pays the interest and a part of each loan, as far as a share of it allows; while it keeps
//! up, households lend part of their spare cash and the bank part of its excess. A float beyond
//! the weeks of trade it needs pays out to the nation's households.
use super::banks::BankState;
use super::loans::TraderLoans;
use super::money_ledger::{pay_nation_households, state_excess};
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::HouseholdCash;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.TraderLoans`.
pub const ORDER: i32 = sim_order::LATE + 29;
const WEEKS_PER_YEAR: i64 = 52;

/// `agents.trader_loans`.
pub struct TraderLoanSystem;

impl System for TraderLoanSystem {
    fn id(&self) -> &'static str {
        "agents.trader_loans"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let config = &ctx.services.database.config;
        let nodes = ctx.services.get::<NodeTable>();
        let banks = ctx.services.get::<NodeBanks>();
        for nation in 0..banks.nation_count(nodes) {
            if repay(&mut ctx.world, config, nation as usize) {
                lend(&mut ctx.world, config, nodes, banks, nation);
            }
            pay_out_excess(&mut ctx.world, config, nation);
        }
        Ok(())
    }
}

/// C# `PayOutExcess`: what traders hold beyond the weeks of trade they need is their income.
fn pay_out_excess(world: &mut WorldState, config: &SimulationConfig, nation: i32) {
    if config.trader_float_keep_weeks <= 0 {
        return;
    }
    let n = nation as usize;
    let purses = world.get::<PurseState>();
    let cash = world.get::<HouseholdCash>();
    let wishes: i64 = purses.nation_nodes[n]
        .iter()
        .map(|&node| cash.desired[node as usize])
        .sum();
    let excess = purses.float[n] - i64::from(config.trader_float_keep_weeks) * wishes;
    if excess <= 0 {
        return;
    }
    let paid = config.trader_float_payout.mul_int(excess);
    world.get_mut::<PurseState>().float[n] -= paid;
    pay_nation_households(world, nation, paid);
}

fn due(config: &SimulationConfig, owed: i64, interest: i64) -> i64 {
    interest
        + if owed <= 0 {
            0
        } else {
            (owed / i64::from(config.trader_loan_weeks)).max(1)
        }
}

fn accrue(config: &SimulationConfig, owed: i64, interest: &mut i64) -> i64 {
    *interest += config.trader_loan_interest.mul_int(owed) / WEEKS_PER_YEAR;
    due(config, owed, *interest)
}

/// C# `Pay`: its share of what the float can pay, interest first.
fn pay(
    config: &SimulationConfig,
    owed: &mut i64,
    interest: &mut i64,
    total: i64,
    free: i64,
) -> i64 {
    let paid = due(config, *owed, *interest).wrapping_mul(free) / total;
    let to_interest = paid.min(*interest);
    *interest -= to_interest;
    *owed -= paid - to_interest;
    paid
}

/// C# `Repay`: whether the float paid all that fell due.
fn repay(world: &mut WorldState, config: &SimulationConfig, nation: usize) -> bool {
    let nodes = world.get::<PurseState>().nation_nodes[nation].clone();
    let float = world.get::<PurseState>().float[nation];
    let loans = world.get_mut::<TraderLoans>();
    let mut total = 0i64;
    for &node in &nodes {
        let n = node as usize;
        total += accrue(
            config,
            loans.owed_households[n],
            &mut loans.interest_households[n],
        );
    }
    total += accrue(
        config,
        loans.owed_bank[nation],
        &mut loans.interest_bank[nation],
    );
    if total <= 0 {
        return true;
    }
    let free = total.min(config.trader_loan_repay_share.mul_int(float.max(0)));
    let mut to_float = 0i64;
    for &node in &nodes {
        let n = node as usize;
        let loans = world.get_mut::<TraderLoans>();
        let before = loans.owed_households[n];
        let paid = pay(
            config,
            &mut loans.owed_households[n],
            &mut loans.interest_households[n],
            total,
            free,
        );
        let returned = before - loans.owed_households[n];
        let cash = world.get_mut::<HouseholdCash>();
        cash.cash[n] += paid;
        cash.saved_year[n] -= returned; // interest is income, the rest was theirs
        to_float += paid;
    }
    let loans = world.get_mut::<TraderLoans>();
    let to_bank = pay(
        config,
        &mut loans.owed_bank[nation],
        &mut loans.interest_bank[nation],
        total,
        free,
    );
    world.get_mut::<BankState>().reserve[nation] += to_bank;
    world.get_mut::<PurseState>().float[nation] -= to_float + to_bank;
    free >= total
}

/// C# `Lend`.
fn lend(
    world: &mut WorldState,
    config: &SimulationConfig,
    nodes: &NodeTable,
    banks: &NodeBanks,
    nation: i32,
) {
    let n = nation as usize;
    if config.trader_loan_share.raw() > 0 {
        let members = world.get::<PurseState>().nation_nodes[n].clone();
        let mut lent_all = 0i64;
        for node in members {
            let i = node as usize;
            let cash = world.get_mut::<HouseholdCash>();
            let spare = cash.cash[i] - i64::from(config.trader_loan_keep_weeks) * cash.desired[i];
            let lent = config.trader_loan_share.mul_int(spare.max(0));
            cash.cash[i] -= lent;
            cash.saved_year[i] += lent;
            world.get_mut::<TraderLoans>().owed_households[i] += lent;
            lent_all += lent;
        }
        world.get_mut::<PurseState>().float[n] += lent_all;
    }
    if config.trader_bank_loan_share.raw() > 0 {
        let excess = state_excess(world, nodes, banks, config, nation).max(0);
        let lent = config.trader_bank_loan_share.mul_int(excess);
        world.get_mut::<BankState>().reserve[n] -= lent;
        world.get_mut::<TraderLoans>().owed_bank[n] += lent;
        world.get_mut::<PurseState>().float[n] += lent;
    }
}
