//! `agents.households` (C# `HouseholdGrowthSystem`, `Agents/HouseholdGrowth.cs`): population and
//! income change once a year with economic conditions (DEC-050).
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::{HouseholdCash, HouseholdState};
use crate::error::SimError;
use crate::pipeline::{SimPhase, System};
use crate::world::NodeTable;
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;

/// Published after the yearly update; `year` is the year whose conditions set the change.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct HouseholdsUpdated {
    pub year: i32,
}

/// C# `FinanceOrder.Households`.
const ORDER: i32 = 120;

pub struct HouseholdGrowthSystem;

impl System for HouseholdGrowthSystem {
    fn id(&self) -> &'static str {
        "agents.households"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if !ctx.clock().is_new_year() || ctx.turn() == 0 {
            return Ok(());
        }
        let db = std::sync::Arc::clone(&ctx.services.database);
        let config = &db.config;
        let nodes = ctx.services.get::<NodeTable>();
        let cash = ctx.world.get::<HouseholdCash>().clone();
        let h = ctx.world.get_mut::<HouseholdState>();
        for node in 0..h.node_count() {
            h.income_per_head[node] = income(config, h, &cash, node).raw();
            let change = population_change(config, nodes, h, node);
            h.population[node] += change.mul_int(h.population[node]);
            h.necessity_wanted[node] = 0;
            h.necessity_met[node] = 0;
        }
        ctx.world.get_mut::<HouseholdCash>().start_year();
        let year = ctx.clock().year() - 1;
        ctx.events.publish(HouseholdsUpdated { year });
        Ok(())
    }
}

/// Base growth plus a share of how far the year's necessity demand stood above or below the need
/// at anchor prices.
fn population_change(
    config: &SimulationConfig,
    nodes: &NodeTable,
    h: &HouseholdState,
    node: usize,
) -> Fx {
    let wanted = h.necessity_wanted[node];
    let mut change = config.population_base_growth;
    if wanted > 0 {
        change = change
            + config.population_growth_response
                * (Fx::from_ratio(h.necessity_met[node], wanted) - Fx::ONE);
    }
    if nodes.level_of(node as i32) != WorldLevel::Village {
        change = change - config.population_urban_penalty;
    }
    Fx::clamp(
        change,
        -config.population_max_decline,
        config.population_max_growth,
    )
}

/// A share of the way from last year's income per head to the year's measured income per head and
/// week, by at most the cap either way. Nobody's income is below zero.
fn income(config: &SimulationConfig, h: &HouseholdState, cash: &HouseholdCash, node: usize) -> Fx {
    let income = Fx::from_raw(h.income_per_head[node]);
    let people = h.population[node];
    if people <= 0 {
        return income;
    }
    let measured = Fx::from_ratio(
        cash.year_income(node).max(0),
        people.wrapping_mul(i64::from(WEEKS_PER_YEAR)),
    );
    let limit = config.income_max_change * Fx::max(income, measured);
    income + Fx::clamp(config.income_response * (measured - income), -limit, limit)
}
