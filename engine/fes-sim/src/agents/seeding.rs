//! The world-level call of C# `MoneyLedger.Seed`: collects the coins held outside the tables the
//! ledger fills (C# `ContinentMoney`'s player, business and noble parts) and runs `ledger::seed`.
use super::banks::BankState;
use super::facilities::FacilityTable;
use super::groups::GroupTable;
use super::ledger::{seed, SeedInputs};
use super::merchants::AgentTable;
use super::nobles::NobleState;
use super::player::PlayerState;
use super::purses::PurseState;
use super::securities::VentureTable;
use crate::economy::{CurrencyState, HouseholdCash, HouseholdState};
use crate::world::{NodeTable, WorldState};
use fes_data::SimulationConfig;

/// Per continent: the player's coins, the cash of facilities, merchant groups (by home), interest
/// groups and ventures on it, and its noble houses' cash.
pub fn other_money(world: &WorldState, nodes: &NodeTable) -> Vec<i64> {
    let continents = nodes.continent_count() as usize;
    let continent_of = |node: i32| nodes.continent[node as usize] as usize;
    let mut money = world.get::<PlayerState>().coins.clone();
    money.resize(continents, 0);
    let facilities = world.get::<FacilityTable>();
    for f in 0..facilities.count() {
        money[continent_of(facilities.node[f])] += facilities.cash[f];
    }
    let agents = world.get::<AgentTable>();
    for a in 0..agents.count() {
        money[continent_of(agents.home[a])] += agents.cash[a];
    }
    let groups = world.get::<GroupTable>();
    for g in 0..groups.count() {
        money[continent_of(groups.node[g])] += groups.cash[g];
    }
    let ventures = world.get::<VentureTable>();
    for v in 0..ventures.count() {
        money[ventures.continent[v] as usize] += ventures.cash[v];
    }
    let houses = world.get::<NobleState>();
    for node in 0..nodes.count() {
        money[nodes.continent[node] as usize] += houses.cash[node];
    }
    money
}

/// C# `MoneyLedger.Seed(world, config)`.
pub fn seed_money(world: &mut WorldState, nodes: &NodeTable, config: &SimulationConfig) {
    let other = other_money(world, nodes);
    let mut purses = world.get::<PurseState>().clone();
    let mut bank = world.get::<BankState>().clone();
    let mut households = world.get::<HouseholdCash>().clone();
    let inputs = SeedInputs {
        nodes,
        config,
        currency: world.get::<CurrencyState>(),
        population: &world.get::<HouseholdState>().population,
        other_money: &other,
    };
    seed(&inputs, &mut purses, &mut bank, &mut households);
    *world.get_mut::<PurseState>() = purses;
    *world.get_mut::<BankState>() = bank;
    *world.get_mut::<HouseholdCash>() = households;
}
