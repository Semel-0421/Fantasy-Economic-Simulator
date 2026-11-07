//! What households and groups offer for bonds and shares this week (C# `BondDesk.OffersOf`,
//! `SumByNation`, `TakeFromHouseholds`). The offers are an unsaved per-turn cache: the first asker
//! of a turn rebuilds them, later askers draw down what is left (see `plan4-audit.md`).
use super::credit::BondOffers;
use super::groups::GroupTable;
use super::purses::PurseState;
use crate::economy::HouseholdCash;
use crate::world::WorldState;
use fes_data::SimulationConfig;

/// C# `OffersOf`: this turn's offers, rebuilt on the first ask of a turn.
pub fn offers_of(
    offers: &mut BondOffers,
    world: &WorldState,
    config: &SimulationConfig,
    turn: i32,
) {
    if offers.turn == turn {
        return;
    }
    offers.turn = turn;
    let cash = world.get::<HouseholdCash>();
    let groups = world.get::<GroupTable>();
    if offers.houses.len() != cash.cash.len() {
        offers.houses = vec![0; cash.cash.len()];
    }
    if offers.groups.len() < groups.count() {
        offers.groups = vec![0; groups.count() + 64];
    }
    offers.groups[groups.count()..].fill(0);
    for n in 0..offers.houses.len() {
        let spare = cash.cash[n] - i64::from(config.bond_keep_weeks) * cash.desired[n];
        offers.houses[n] = config.bond_buy_share.mul_int(spare.max(0));
    }
    for g in 0..groups.count() {
        let keep = i64::from(config.bond_group_keep_times)
            * config.group_start_cash[groups.kind[g] as usize];
        offers.groups[g] = if groups.is_player[g] != 0 {
            0
        } else {
            config
                .bond_buy_share
                .mul_int((groups.cash[g] - keep).max(0))
        };
    }
    sum_by_nation(offers, world.get::<PurseState>(), groups);
}

fn sum_by_nation(offers: &mut BondOffers, purses: &PurseState, groups: &GroupTable) {
    if offers.nations.len() != purses.nation_count {
        offers.nations = vec![0; purses.nation_count];
    }
    for n in 0..offers.nations.len() {
        offers.nations[n] = purses.nation_nodes[n]
            .iter()
            .map(|&node| offers.houses[node as usize])
            .sum();
    }
    for g in 0..groups.count() {
        let nation = purses.node_float[groups.node[g] as usize] as usize;
        if nation < offers.nations.len() {
            offers.nations[nation] += offers.groups[g];
        }
    }
}

/// C# `TakeFromHouseholds`: up to `amount` from what a nation's households offer this week, in
/// proportion. Returns the coins taken, which the caller pays on.
pub fn take_from_households(
    world: &mut WorldState,
    offers: &mut BondOffers,
    config: &SimulationConfig,
    turn: i32,
    nation: i32,
    amount: i64,
) -> i64 {
    offers_of(offers, world, config, turn);
    let (purses, cash) = world.get2_mut::<PurseState, HouseholdCash>();
    let nodes = &purses.nation_nodes[nation as usize];
    let offered: i64 = nodes.iter().map(|&n| offers.houses[n as usize]).sum();
    let want = amount.min(offered);
    if want <= 0 {
        return 0;
    }
    let mut taken = 0;
    for &node in nodes {
        let n = node as usize;
        let part = want / offered * offers.houses[n] + want % offered * offers.houses[n] / offered;
        if part <= 0 {
            continue;
        }
        offers.houses[n] -= part;
        offers.nations[nation as usize] -= part;
        cash.cash[n] -= part;
        cash.saved_year[n] += part;
        taken += part;
    }
    taken
}
