//! `agents.state_spending` (C# `StateSpendingSystem`, `Agents/Purses.cs`): each nation spends a
//! share of its reserve above target on wages, and another share on public wages to its towns.
use super::banks::BankState;
use super::money_ledger::{pay_nation_town_wages, pay_nation_wages, state_excess};
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::HouseholdState;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.StateSpending`.
pub const ORDER: i32 = sim_order::LATE + 5;

/// `agents.state_spending`.
pub struct StateSpendingSystem;

impl System for StateSpendingSystem {
    fn id(&self) -> &'static str {
        "agents.state_spending"
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
        for n in 0..banks.nation_count(nodes) {
            let excess = state_excess(&ctx.world, nodes, banks, config, n).max(0);
            let spending = config.state_spending_share.mul_int(excess);
            if spending > 0 {
                pay_wages(&mut ctx.world, nodes, config, n, spending);
            }
            let excess = state_excess(&ctx.world, nodes, banks, config, n).max(0);
            let public_wages = config.state_public_wage_share.mul_int(excess);
            if public_wages <= 0 {
                continue;
            }
            ctx.world.get_mut::<BankState>().reserve[n as usize] -= public_wages;
            pay_nation_town_wages(&mut ctx.world, nodes, config, n, public_wages);
        }
        Ok(())
    }
}

fn pay_wages(
    world: &mut WorldState,
    nodes: &NodeTable,
    config: &SimulationConfig,
    nation: i32,
    amount: i64,
) {
    let people: i64 = {
        let population = &world.get::<HouseholdState>().population;
        world.get::<PurseState>().nation_nodes[nation as usize]
            .iter()
            .map(|&n| population[n as usize])
            .sum()
    };
    if people <= 0 {
        return;
    }
    world.get_mut::<BankState>().reserve[nation as usize] -= amount;
    pay_nation_wages(world, nodes, config, nation, amount);
}
