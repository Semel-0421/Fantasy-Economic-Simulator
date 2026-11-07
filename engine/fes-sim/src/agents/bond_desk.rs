//! What households and groups offer for bonds and shares this week (C# `BondDesk.OffersOf`,
//! `SumByNation`, `TakeFromHouseholds`). The offers are an unsaved per-turn cache: the first asker
//! of a turn rebuilds them, later askers draw down what is left (see `plan4-audit.md`).
use super::asset_prices::facility_value;
use super::credit::{BondOffers, BondTable};
use super::facilities::{FacilityTable, OWNER_NPC};
use super::groups::GroupTable;
use super::purses::PurseState;
use crate::economy::HouseholdCash;
use crate::world::WorldState;
use fes_data::database::GameDatabase;
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

/// C# `TakeFromNode`: up to `amount` from what one node's households offer this week.
pub fn take_from_node(
    world: &mut WorldState,
    offers: &mut BondOffers,
    config: &SimulationConfig,
    turn: i32,
    households: usize,
    amount: i64,
) -> i64 {
    offers_of(offers, world, config, turn);
    let part = amount.min(offers.houses[households]);
    if part <= 0 {
        return 0;
    }
    let nation = world.get::<PurseState>().node_float[households] as usize;
    offers.houses[households] -= part;
    if nation < offers.nations.len() {
        offers.nations[nation] -= part;
    }
    let cash = world.get_mut::<HouseholdCash>();
    cash.cash[households] -= part;
    cash.saved_year[households] += part;
    part
}

impl BondTable {
    /// C# `BondTable.Add`: adds to the issuer's holding by `holder`, or a new row.
    pub fn add(&mut self, issuer: i32, holder: i32, face: i64) {
        if let Some(r) =
            (0..self.count()).rposition(|r| self.issuer[r] == issuer && self.holder[r] == holder)
        {
            self.face[r] += face;
            return;
        }
        self.issuer.push(issuer);
        self.holder.push(holder);
        self.face.push(face);
        self.interest.push(0);
        self.behind_weeks.push(0);
    }
}

/// C# `BondTable.OfGroup`: a group as a bond holder.
pub fn group_holder(group: i32) -> i32 {
    !group
}

/// C# `BondDesk.Offered`: what a nation's holders would pay for this week's bonds, apart from the
/// issuer itself (-1 for a state issue).
pub fn offered(
    offers: &mut BondOffers,
    world: &WorldState,
    config: &SimulationConfig,
    turn: i32,
    nation: i32,
    issuer: i32,
) -> i64 {
    offers_of(offers, world, config, turn);
    let groups = world.get::<GroupTable>();
    let float = &world.get::<PurseState>().node_float;
    let issuer_buys = issuer >= 0
        && (issuer as usize) < groups.count()
        && float[groups.node[issuer as usize] as usize] == nation;
    offers.nations[nation as usize]
        - if issuer_buys {
            offers.groups[issuer as usize]
        } else {
            0
        }
}

/// C# `BondDesk.Raise`: sells up to `amount` of bonds, in proportion to what holders offer.
/// Returns the coins raised, which the caller receives.
pub fn raise(
    world: &mut WorldState,
    offers: &mut BondOffers,
    config: &SimulationConfig,
    turn: i32,
    issuer: i32,
    nation: i32,
    amount: i64,
) -> i64 {
    let buyer = if issuer < 0 { -1 } else { issuer };
    let offered = offered(offers, world, config, turn, nation, buyer);
    let want = amount.min(offered);
    if want <= 0 {
        return 0;
    }
    let share = |offer: i64| want / offered * offer + want % offered * offer / offered;
    let nodes = world.get::<PurseState>().nation_nodes[nation as usize].clone();
    let mut raised = 0;
    for node in nodes {
        let n = node as usize;
        let part = share(offers.houses[n]);
        if part <= 0 {
            continue;
        }
        offers.houses[n] -= part;
        offers.nations[nation as usize] -= part;
        let cash = world.get_mut::<HouseholdCash>();
        cash.cash[n] -= part;
        cash.saved_year[n] += part;
        world.get_mut::<BondTable>().add(issuer, node, part);
        raised += part;
    }
    for g in 0..world.get::<GroupTable>().count() {
        let group_node = world.get::<GroupTable>().node[g] as usize;
        if g as i32 == issuer || world.get::<PurseState>().node_float[group_node] != nation {
            continue;
        }
        let part = share(offers.groups[g]);
        if part <= 0 {
            continue;
        }
        offers.groups[g] -= part;
        offers.nations[nation as usize] -= part;
        world.get_mut::<GroupTable>().cash[g] -= part;
        world
            .get_mut::<BondTable>()
            .add(issuer, group_holder(g as i32), part);
        raised += part;
    }
    raised
}

/// C# `BondDesk.CompanyRoom`: how far a company may still borrow on its facilities.
pub fn company_room(config: &SimulationConfig, facilities_value: i64, debt: i64) -> i64 {
    (config.company_debt_share.mul_int(facilities_value) - debt).max(0)
}

/// What each company's facilities are worth and what it owes on bonds (C# `CompanyRooms`),
/// refreshed before each use.
#[derive(Clone, Debug, Default)]
pub struct CompanyRooms {
    value: Vec<i64>,
    debt: Vec<i64>,
}

impl CompanyRooms {
    /// C# `Refresh`.
    pub fn refresh(&mut self, world: &WorldState, db: &GameDatabase) {
        let groups = world.get::<GroupTable>().count();
        if self.value.len() < groups {
            self.value = vec![0; groups + 64];
            self.debt = vec![0; groups + 64];
        }
        self.value.fill(0);
        self.debt.fill(0);
        let facilities = world.get::<FacilityTable>();
        for f in 0..facilities.count() {
            if facilities.group[f] >= 0 && facilities.owner[f] == OWNER_NPC {
                self.value[facilities.group[f] as usize] += facility_value(db, facilities, f);
            }
        }
        let bonds = world.get::<BondTable>();
        for r in 0..bonds.count() {
            if bonds.issuer[r] >= 0 {
                self.debt[bonds.issuer[r] as usize] += bonds.face[r];
            }
        }
    }

    /// C# `Room`.
    pub fn room(&mut self, config: &SimulationConfig, group: usize) -> i64 {
        self.reach(group);
        company_room(config, self.value[group], self.debt[group])
    }

    /// C# `AddDebt`.
    pub fn add_debt(&mut self, group: usize, face: i64) {
        self.reach(group);
        self.debt[group] += face;
    }

    /// C# `AddValue`.
    pub fn add_value(&mut self, group: usize, value: i64) {
        self.reach(group);
        self.value[group] += value;
    }

    /// A group founded since the last refresh has no facilities and no debt yet.
    fn reach(&mut self, group: usize) {
        if group >= self.value.len() {
            self.value.resize(group + 64, 0);
            self.debt.resize(group + 64, 0);
        }
    }
}
