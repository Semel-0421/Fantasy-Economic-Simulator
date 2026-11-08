//! Where money goes when it changes hands outside a market (C# `MoneyLedger`, the parts the
//! production system uses, and `PurseState.PayHouseholds`).
use super::banks::BankState;
use super::groups::{private_enterprise_at, GroupTable};
use super::purses::PurseState;
use crate::economy::{CurrencyState, HouseholdCash, HouseholdState};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;

/// Money paid to the households of `node` (C# `PurseState.PayHouseholds`): at its core when classes
/// are absorbed.
pub fn pay_households(world: &mut WorldState, nodes: &NodeTable, node: i32, amount: i64) {
    let at = if nodes.absorbed() {
        nodes.core[node as usize]
    } else {
        node
    };
    world.get_mut::<HouseholdCash>().cash[at as usize] += amount;
}

/// Takes up to `want` coins from the households of `node`, never leaving them below zero;
/// returns what was taken.
pub fn take_households(world: &mut WorldState, nodes: &NodeTable, node: i32, want: i64) -> i64 {
    let at = if nodes.absorbed() {
        nodes.core[node as usize]
    } else {
        node
    };
    let cash = &mut world.get_mut::<HouseholdCash>().cash[at as usize];
    let taken = want.min((*cash).max(0)).max(0);
    *cash -= taken;
    taken
}

/// A nation's reserve above its target and the player's deposits (C# `StateExcess`).
pub fn state_excess(
    world: &WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    config: &SimulationConfig,
    nation: i32,
) -> i64 {
    let continent = nodes.continent[banks.nation_node(nodes, nation) as usize];
    let nations = (0..banks.nation_count(nodes))
        .filter(|&n| nodes.continent[banks.nation_node(nodes, n) as usize] == continent)
        .count() as i64;
    let bank = world.get::<BankState>();
    let supply = world.get::<CurrencyState>().coin_supply[continent as usize];
    let share = config.bank_initial_reserve_share.mul_int(supply) / nations.max(1);
    bank.reserve[nation as usize]
        - bank.player_deposit[nation as usize]
        - config.state_reserve_target.mul_int(share)
}

/// Money for the households around `node`: its nation's by population, or the node's own outside
/// any nation (C# `PayLocalHouseholds`).
pub fn pay_local_households(world: &mut WorldState, nodes: &NodeTable, node: i32, amount: i64) {
    let purses = world.get::<PurseState>();
    let nation = purses.node_float[node as usize];
    if (nation as usize) < purses.nation_count {
        pay_nation_households(world, nation, amount);
    } else {
        pay_households(world, nodes, node, amount);
    }
}

/// Money for a nation's households, split by population (C# `PayNationHouseholds`, not borrowed).
pub fn pay_nation_households(world: &mut WorldState, nation: i32, amount: i64) {
    pay_nation_households_as(world, nation, amount, false);
}

/// C# `PayNationHouseholds(..., borrowed: true)`: a loan, not income (DEC-131), so it also comes
/// off what the households saved this year.
pub fn lend_nation_households(world: &mut WorldState, nation: i32, amount: i64) {
    pay_nation_households_as(world, nation, amount, true);
}

fn pay_nation_households_as(world: &mut WorldState, nation: i32, amount: i64, borrowed: bool) {
    let nodes = world.get::<PurseState>().nation_nodes[nation as usize].clone();
    let population = world.get::<HouseholdState>().population.clone();
    let people: i64 = nodes
        .iter()
        .map(|&n| population[n as usize])
        .filter(|&p| p > 0)
        .sum();
    let Some(&first) = nodes.iter().find(|&&n| population[n as usize] > 0) else {
        return;
    };
    let households = world.get_mut::<HouseholdCash>();
    let mut given = 0i64;
    for &node in &nodes {
        let p = population[node as usize];
        if p <= 0 {
            continue;
        }
        let share = amount * p / people;
        households.cash[node as usize] += share;
        if borrowed {
            households.saved_year[node as usize] -= share;
        }
        given += share;
    }
    households.cash[first as usize] += amount - given;
    if borrowed {
        households.saved_year[first as usize] -= amount - given;
    }
}

fn wage_weight(nodes: &NodeTable, config: &SimulationConfig, node: i32) -> Fx {
    config.level_wage_weight[nodes.level[node as usize] as usize]
        * (Fx::ONE
            + Fx::from_int(i64::from(nodes.core_strength[node as usize]))
                * config.core_wage_per_point)
}

/// Wages for a nation's households, by population weighted per level (C# `PayNationWages`).
pub fn pay_nation_wages(
    world: &mut WorldState,
    levels: &NodeTable,
    config: &SimulationConfig,
    nation: i32,
    amount: i64,
) {
    let nodes = world.get::<PurseState>().nation_nodes[nation as usize].clone();
    let population = world.get::<HouseholdState>().population.clone();
    let weight = |node: i32| wage_weight(levels, config, node).mul_int(population[node as usize]);
    let total: i64 = nodes
        .iter()
        .filter(|&&n| population[n as usize] > 0)
        .map(|&n| weight(n))
        .sum();
    let Some(&first) = nodes.iter().find(|&&n| population[n as usize] > 0) else {
        return;
    };
    let cash = &mut world.get_mut::<HouseholdCash>().cash;
    let mut given = 0i64;
    for &node in &nodes {
        if population[node as usize] <= 0 {
            continue;
        }
        let w = weight(node);
        let share = amount / total * w + amount % total * w / total;
        cash[node as usize] += share;
        given += share;
    }
    cash[first as usize] += amount - given;
}

/// Toll, license and levy money: the nation's bank reserve, or the continent's international bank
/// outside any nation (C# `ToState`).
pub fn to_state(
    world: &mut WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    node: i32,
    amount: i64,
) {
    let nation_node = nodes.ancestor_at(node, WorldLevel::Nation);
    let bank = world.get_mut::<BankState>();
    if nation_node >= 0 {
        bank.reserve[banks.nation_index(nodes, nation_node) as usize] += amount;
    } else {
        bank.international_reserve[nodes.continent[node as usize] as usize] += amount;
    }
}

/// A group pays `amount`; what it lacks comes from the households around `node` (C# `PayFromGroup`).
pub fn pay_from_group(
    world: &mut WorldState,
    nodes: &NodeTable,
    group: i32,
    node: i32,
    amount: i64,
) {
    let mut from_group = 0;
    if group >= 0 {
        let cash = &mut world.get_mut::<GroupTable>().cash[group as usize];
        from_group = amount.min((*cash).max(0));
        *cash -= from_group;
    }
    if amount > from_group {
        pay_local_households(world, nodes, node, -(amount - from_group));
    }
}

/// A new operator takes over an asset with `cash` (C# `FundNewOperator`).
pub fn fund_new_operator(
    world: &mut WorldState,
    nodes: &NodeTable,
    group: i32,
    node: i32,
    old_cash: i64,
    cash: i64,
) -> i64 {
    pay_local_households(world, nodes, node, old_cash);
    pay_from_group(world, nodes, group, node, cash);
    cash
}

/// The family at a node; one set up now starts with the coins of the households around (C# `FamilyAt`).
pub fn family_at(
    world: &mut WorldState,
    nodes: &NodeTable,
    config: &SimulationConfig,
    node: i32,
) -> i32 {
    let groups = world.get_mut::<GroupTable>();
    let before = groups.count();
    let family = private_enterprise_at(groups, node, config);
    if groups.count() > before {
        let cash = groups.cash[family as usize];
        pay_local_households(world, nodes, node, -cash);
    }
    family
}

/// Where a merchant group's dues are settled: where it is, or its home while abroad (C#
/// `MerchantSettlesAt`).
pub fn merchant_settles_at(nodes: &NodeTable, node: i32, home: i32) -> i32 {
    if nodes.continent[node as usize] == nodes.continent[home as usize] {
        node
    } else {
        home
    }
}

/// How far a town's weekly budget falls short of the necessities it would buy (C# `TownShortfall`).
pub fn town_shortfall(
    levels: &NodeTable,
    households: &HouseholdCash,
    config: &SimulationConfig,
    node: i32,
) -> i64 {
    if levels.level_of(node) == WorldLevel::Village {
        return 0;
    }
    let n = node as usize;
    households.desired_necessity[n]
        - households.cash[n].max(0) / i64::from(config.household_budget_weeks.max(1))
}

/// Public wages to a nation's towns by their shortfall; by the usual wage weights if none falls
/// short (C# `PayNationTownWages`).
pub fn pay_nation_town_wages(
    world: &mut WorldState,
    levels: &NodeTable,
    config: &SimulationConfig,
    nation: i32,
    amount: i64,
) {
    let (total, first) = {
        let purses = world.get::<PurseState>();
        let households = world.get::<HouseholdCash>();
        let mut total = 0i64;
        let mut first = -1;
        for &node in &purses.nation_nodes[nation as usize] {
            let gap = town_shortfall(levels, households, config, node);
            if gap <= 0 {
                continue;
            }
            total += gap;
            if first < 0 {
                first = node;
            }
        }
        (total, first)
    };
    if first < 0 {
        pay_nation_wages(world, levels, config, nation, amount);
        return;
    }
    let (purses, households) = world.get2_mut::<PurseState, HouseholdCash>();
    let nodes = &purses.nation_nodes[nation as usize];
    let mut given = 0i64;
    for &node in nodes {
        let gap = town_shortfall(levels, households, config, node);
        if gap <= 0 {
            continue;
        }
        let share = amount / total * gap + amount % total * gap / total;
        households.cash[node as usize] += share;
        given += share;
    }
    households.cash[first as usize] += amount - given;
}

/// A payment in one coin settles a debt in another through the international banks (C#
/// `AcrossContinents`).
pub fn across_continents(bank: &mut BankState, from: i32, paid: i64, to: i32, received: i64) {
    if from == to {
        return;
    }
    bank.international_reserve[from as usize] += paid;
    bank.international_reserve[to as usize] -= received;
}
