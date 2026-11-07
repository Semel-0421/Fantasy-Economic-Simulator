//! `agents.labor` (C# `LaborMarketSystem`, `Agents/LaborMarket.cs`): merchant groups pay wages
//! from their cash above working capital; each quarter the employment rate follows real spending.
use super::facilities::OWNER_NPC;
use super::labor::LaborState;
use super::merchants::{capital_start, AgentTable};
use super::money_ledger::{merchant_settles_at, pay_households, pay_nation_wages};
use super::orders::MerchantTripStats;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::{CurrencyState, HouseholdState};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_core::Fx;

/// C# `FinanceOrder.Labor`.
pub const ORDER: i32 = sim_order::LATE + 6;

/// `agents.labor`. `wages` and `people` are scratch, cleared after use.
#[derive(Default)]
pub struct LaborMarketSystem {
    wages: Vec<i64>,
    people: Vec<i64>,
}

impl System for LaborMarketSystem {
    fn id(&self) -> &'static str {
        "agents.labor"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let purses = ctx.world.get::<PurseState>();
        self.wages.resize(purses.nation_count, 0);
        self.people.resize(purses.count(), 0);
        self.pay_wages(ctx);
        if ctx.clock().is_quarter_end() {
            self.update_employment(ctx);
        }
        Ok(())
    }
}

impl LaborMarketSystem {
    fn pay_wages(&mut self, ctx: &mut SimContext) {
        let config = &ctx.services.database.config;
        let seed = ctx.seed();
        let nodes = ctx.services.get::<NodeTable>();
        let count = ctx.world.get::<AgentTable>().count();
        let mut spent = 0i64;
        for a in 0..count {
            let (owner, cash, node, home) = {
                let agents = ctx.world.get::<AgentTable>();
                (
                    agents.owner[a],
                    agents.cash[a],
                    agents.node[a],
                    agents.home[a],
                )
            };
            if owner != OWNER_NPC {
                continue;
            }
            let buffer = config
                .labor_merchant_buffer
                .mul_int(capital_start(config, seed, a as i32));
            let excess = cash - buffer;
            if excess <= 0 {
                continue;
            }
            let node = merchant_settles_at(nodes, node, home);
            let (purse, nation_count) = {
                let purses = ctx.world.get::<PurseState>();
                (
                    purses.node_float[node as usize] as usize,
                    purses.nation_count,
                )
            };
            let rate = ctx.world.get::<LaborState>().rate(purse);
            let wage = rate.mul_int(config.labor_merchant_wage.mul_int(excess));
            ctx.world.get_mut::<AgentTable>().cash[a] -= wage;
            spent += wage;
            if purse < nation_count {
                self.wages[purse] += wage;
            } else {
                pay_households(&mut ctx.world, nodes, node, wage);
            }
        }
        ctx.services.get_mut::<MerchantTripStats>().spent_wages += spent;
        let config = &ctx.services.database.config;
        let nodes = ctx.services.get::<NodeTable>();
        for n in 0..self.wages.len() {
            if self.wages[n] == 0 {
                continue;
            }
            pay_nation_wages(&mut ctx.world, nodes, config, n as i32, self.wages[n]);
            self.wages[n] = 0;
        }
    }

    fn update_employment(&mut self, ctx: &mut SimContext) {
        let config = &ctx.services.database.config;
        self.people.fill(0);
        let purses = ctx.world.get::<PurseState>();
        let population = &ctx.world.get::<HouseholdState>().population;
        for (node, &p) in population.iter().enumerate() {
            self.people[purses.node_float[node] as usize] += p;
        }
        for p in 0..self.people.len() {
            let continent = ctx.world.get::<PurseState>().continent_of(p) as usize;
            let price_index = ctx.world.get::<CurrencyState>().price_index[continent];
            let labor = ctx.world.get_mut::<LaborState>();
            let spent = labor.quarter_spending[p];
            labor.quarter_spending[p] = 0;
            if self.people[p] <= 0 {
                continue;
            }
            let activity = Fx::from_ratio(spent, self.people[p]) / price_index;
            if labor.long_run_activity[p] <= 0 {
                labor.long_run_activity[p] = activity.raw();
                continue;
            }
            let long_run = Fx::from_raw(labor.long_run_activity[p]);
            let target = Fx::clamp(
                config.labor_base_employment * (activity / long_run),
                config.labor_min_employment,
                config.labor_max_employment,
            );
            let rate = labor.rate(p);
            labor.employment[p] = (rate + config.labor_response * (target - rate)).raw();
            labor.long_run_activity[p] =
                (long_run + config.labor_long_run_response * (activity - long_run)).raw();
        }
    }
}
