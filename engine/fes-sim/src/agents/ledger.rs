//! The starting split of each continent's coin supply (C# `MoneyLedger.Seed`, DEC-061).
use super::banks::BankState;
use super::purses::PurseState;
use crate::economy::{CurrencyState, HouseholdCash};
use crate::world::NodeTable;
use fes_data::SimulationConfig;

/// What `seed` reads besides the tables it fills.
pub struct SeedInputs<'a> {
    pub nodes: &'a NodeTable,
    pub config: &'a SimulationConfig,
    pub currency: &'a CurrencyState,
    /// Per node: the households' population.
    pub population: &'a [i64],
    /// Per continent: the coins of the player, facilities, merchant groups, interest groups, ventures
    /// and noble houses on it (the parts of C# `MoneyLedger.ContinentMoney` outside the tables above).
    pub other_money: &'a [i64],
}

/// Gives each continent's international bank its starting share of the coin supply, and the rest of
/// the supply nobody else holds to its traders' floats (a share, split by the population of their
/// nations) and its households (by population), so that the continent's money equals its coin supply.
pub fn seed(
    inputs: &SeedInputs,
    purses: &mut PurseState,
    bank: &mut BankState,
    households: &mut HouseholdCash,
) {
    let nodes = inputs.nodes;
    let float_population = float_population(inputs, purses);
    let purse_continent: Vec<i32> = (0..purses.count())
        .map(|p| purses.continent_of(p))
        .collect();
    for c in 0..inputs.currency.continent_count() {
        let supply = inputs.currency.coin_supply[c];
        bank.international_reserve[c] += inputs
            .config
            .international_bank_reserve_share
            .mul_int(supply);
        let rest = supply - continent_money(inputs, purses, bank, households, c);
        let floats = inputs.config.trader_float_share.mul_int(rest);
        let continent = c as i32;
        split(floats, &mut purses.float, |p| {
            if purse_continent[p] == continent {
                float_population[p]
            } else {
                0
            }
        });
        split(rest - floats, &mut households.cash, |node| {
            if nodes.continent[node] == continent {
                inputs.population[node]
            } else {
                0
            }
        });
    }
    households.start_year();
}

/// Traders work in nations; a continent's own float starts empty unless the continent has no nations.
fn float_population(inputs: &SeedInputs, purses: &PurseState) -> Vec<i64> {
    let mut population = vec![0i64; purses.count()];
    for node in 0..inputs.nodes.count() {
        population[purses.node_float[node] as usize] += inputs.population[node];
    }
    let nation_continents: Vec<i32> = (0..purses.nation_count)
        .map(|n| purses.continent_of(n))
        .collect();
    for (p, slot) in population.iter_mut().enumerate().skip(purses.nation_count) {
        if nation_continents.contains(&purses.continent_of(p)) {
            *slot = 0;
        }
    }
    population
}

/// C# `MoneyLedger.ContinentMoney`, with the parts held outside the filled tables given in
/// `other_money`.
fn continent_money(
    inputs: &SeedInputs,
    purses: &PurseState,
    bank: &BankState,
    households: &HouseholdCash,
    c: usize,
) -> i64 {
    let continent = c as i32;
    let mut money = inputs.other_money[c] + bank.international_reserve[c];
    for n in 0..purses.nation_count {
        if purses.continent_of(n) == continent {
            money += bank.reserve[n];
        }
    }
    for p in 0..purses.count() {
        if purses.continent_of(p) == continent {
            money += purses.float[p];
        }
    }
    for node in 0..inputs.nodes.count() {
        if inputs.nodes.continent[node] == continent {
            money += households.cash[node] + households.spent[node];
        }
    }
    money
}

/// Adds `amount` over the entries with weight, in proportion; the rounding remainder goes to the first.
fn split(amount: i64, into: &mut [i64], weight: impl Fn(usize) -> i64) {
    let weights: Vec<i64> = (0..into.len()).map(weight).collect();
    let total: i64 = weights.iter().filter(|w| **w > 0).sum();
    let Some(first) = weights.iter().position(|w| *w > 0) else {
        return;
    };
    let mut given = 0i64;
    for (slot, w) in into.iter_mut().zip(&weights).filter(|(_, w)| **w > 0) {
        let share = amount.wrapping_mul(*w) / total;
        *slot += share;
        given += share;
    }
    into[first] += amount - given;
}
