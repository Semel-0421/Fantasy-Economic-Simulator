//! `agents.defense` (C# `DefenseSystem` and `DefensePurchaseJob`, `Agents/DefenseSystem.cs`): a
//! nation's defense costs a share of its yearly budget plus maintenance, bought every few weeks from
//! the stock of its markets; the army makes its routes safer. As in C#, the new-year branch has no
//! turn guard, so turn 0 sets the budget and security too.
use super::banks::BankState;
use super::development::DevelopmentState;
use super::purses::PurseState;
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::MarketStore;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::RouteTable;
use fes_core::Fx;

/// C# `FinanceOrder.Defense`.
pub const ORDER: i32 = sim_order::LATE + 7;

/// `agents.defense`. Per nation: `spend` is recomputed every run; `paid`, `value` and `carry` are
/// job scratch reset by each purchase.
#[derive(Default)]
pub struct DefenseSystem {
    spend: Vec<i64>,
    paid: Vec<i64>,
    value: Vec<i64>,
    carry: Vec<i64>,
    nation_routes: Vec<i64>,
}

impl System for DefenseSystem {
    fn id(&self) -> &'static str {
        "agents.defense"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn initialize(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        self.prepare(ctx);
        Ok(())
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if self.spend.is_empty() {
            self.prepare(ctx);
        }
        let config = &ctx.services.database.config;
        let new_year = ctx.clock().is_new_year();
        let turn = ctx.turn();
        let (bank, development) = ctx.world.get2_mut::<BankState, DevelopmentState>();
        for n in 0..self.spend.len() {
            if new_year {
                development.year_budget[n] = bank.spendable(n);
                development.defense_spent[n] = 0;
            }
            let mut yearly = config.defense_share.mul_int(development.year_budget[n]);
            yearly += config.defense_maintenance.mul_int(yearly);
            if new_year {
                let cost = self.nation_routes[n] * i64::from(config.defense_safety_cost_per_route);
                development.security[n] = Fx::min(
                    config.defense_max_safety,
                    Fx::from_ratio(yearly, cost.max(1)),
                )
                .raw();
            }
            let interval = i64::from(config.defense_interval_weeks);
            self.spend[n] = (yearly * interval / i64::from(WEEKS_PER_YEAR)).min(bank.spendable(n));
        }
        if turn % config.defense_interval_weeks != 0 {
            return Ok(());
        }
        let (purses, markets) = ctx.world.get2_mut::<PurseState, MarketStore>();
        self.purchase(&purses.node_float, markets);
        let (bank, purses) = ctx.world.get2_mut::<BankState, PurseState>();
        for n in 0..self.paid.len() {
            bank.reserve[n] -= self.paid[n];
            purses.float[n] += self.paid[n];
        }
        let development = ctx.world.get_mut::<DevelopmentState>();
        for n in 0..self.paid.len() {
            development.defense_spent[n] += self.paid[n];
        }
        Ok(())
    }
}

impl DefenseSystem {
    /// C# `Initialize`: the scratch per nation and each nation's routes (by their start node).
    fn prepare(&mut self, ctx: &SimContext) {
        let nations = ctx.world.get::<DevelopmentState>().year_budget.len();
        self.spend = vec![0; nations];
        self.paid = vec![0; nations];
        self.value = vec![0; nations];
        self.carry = vec![0; nations];
        self.nation_routes = vec![0; nations];
        let purses = ctx.world.get::<PurseState>();
        let routes = ctx.services.get::<RouteTable>();
        for r in 0..routes.count() {
            let float = purses.node_float[routes.from[r] as usize] as usize;
            if float < nations {
                self.nation_routes[float] += 1;
            }
        }
    }

    /// C# `DefensePurchaseJob.Execute`.
    fn purchase(&mut self, node_float: &[i32], m: &mut MarketStore) {
        self.value.fill(0);
        self.paid.fill(0);
        self.carry.fill(0);
        let nations = self.value.len();
        for (node, &float) in node_float.iter().enumerate() {
            let nation = float as usize;
            if nation >= nations || self.spend[nation] <= 0 {
                continue;
            }
            for c in cells(m, node) {
                self.value[nation] = self.value[nation]
                    .wrapping_add(i64::from(m.stock[c]).wrapping_mul(i64::from(m.price[c])));
            }
        }
        for (node, &float) in node_float.iter().enumerate() {
            let nation = float as usize;
            if nation < nations && self.spend[nation] > 0 && self.value[nation] > 0 {
                self.buy(m, node, nation);
            }
        }
    }

    fn buy(&mut self, m: &mut MarketStore, node: usize, nation: usize) {
        for c in cells(m, node) {
            let units = self.units_at(m, c, nation);
            if units <= 0 {
                continue;
            }
            // Through this turn's supply into its demand, like every purchase.
            m.stock[c] = m.stock[c].wrapping_sub(units as i32);
            m.supply[c] = MarketStore::clamp_to_non_negative_int(i64::from(m.supply[c]) + units);
            m.demand[c] = MarketStore::clamp_to_non_negative_int(i64::from(m.demand[c]) + units);
            self.paid[nation] += units * i64::from(m.price[c]);
        }
    }

    /// A cell's share of the nation's purchase; the part short of a unit carries to the next cell.
    fn units_at(&mut self, m: &MarketStore, c: usize, nation: usize) -> i64 {
        let (spend, value) = (self.spend[nation], self.value[nation]);
        if value <= spend {
            return i64::from(m.stock[c]);
        }
        let share = i64::from(m.stock[c]).wrapping_mul(spend);
        let mut units = share / value;
        self.carry[nation] += share % value;
        if self.carry[nation] >= value {
            self.carry[nation] -= value;
            units += 1;
        }
        let price = if m.price[c] > 0 { m.price[c] } else { 1 };
        let left = (spend - self.paid[nation]) / i64::from(price);
        units.min(left)
    }
}

fn cells(m: &MarketStore, node: usize) -> std::ops::Range<usize> {
    let start = m.market_cell_start[node] as usize;
    start..start + m.market_cell_count[node] as usize
}
