//! Weekly upkeep and dividends of every facility (C# `FacilityUpkeepJob`), the holders' part of the
//! dividends (C# `ShareBook.Snapshot`/`PayHolders`), wages leaving villages and the groups passing
//! dividends on (C# `ProductionSystem.PayWagesAway`, `PassOnDividends`).
use crate::agents::facilities::{OWNER_BANK, OWNER_NPC};
use crate::agents::groups::KIND_STATE_ENTERPRISE;
use crate::agents::money_ledger::{
    pay_households, pay_nation_households, pay_nation_wages, to_state,
};
use crate::agents::{BankState, FacilityTable, GroupTable, IncomeTaxState, PurseState, ShareTable};
use crate::economy::HouseholdCash;
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::Fx;
use fes_data::database::GameDatabase;

/// C# `ShareTable.PlayerHolder`.
const PLAYER_HOLDER: i32 = i32::MIN;

/// Per-turn upkeep inputs and outputs.
pub struct Upkeep<'a> {
    pub db: &'a GameDatabase,
    /// Per node, raw Fx: the share of wages paid there that leaves for the nation's towns.
    pub node_wage_away: &'a [i64],
    pub input_cost: &'a [i64],
    pub output_wage: &'a [i64],
    /// Per node: this week's wages leaving it.
    pub node_wages_away: &'a mut [i64],
    /// Per group: dividends received this week.
    pub group_dividend: &'a mut [i64],
    pub bankrupt: &'a mut Vec<usize>,
}

impl Upkeep<'_> {
    pub fn run(&mut self, world: &mut WorldState) {
        let config = &self.db.config;
        let mut facilities = std::mem::take(world.get_mut::<FacilityTable>());
        let mut dividends_year =
            std::mem::take(&mut world.get_mut::<IncomeTaxState>().facility_dividends);
        let households = &mut world.get_mut::<HouseholdCash>().cash;
        for (f, year) in dividends_year
            .iter_mut()
            .enumerate()
            .take(facilities.count())
        {
            let tier = self.db.facilities[facilities.def_index[f] as usize].tier as usize;
            let node = facilities.node[f] as usize;
            let upkeep =
                config.facility_upkeep[tier] * i64::from(facilities.level[f]) + self.output_wage[f];
            facilities.cash[f] -= upkeep;
            let away = Fx::mul_int_raw(upkeep, self.node_wage_away[node]);
            households[node] += upkeep - away;
            self.node_wages_away[node] += away;
            let buffer = (upkeep + self.input_cost[f]) * i64::from(config.dividend_buffer_weeks);
            if facilities.owner[f] == OWNER_NPC && facilities.cash[f] > buffer {
                let dividend =
                    Fx::mul_int_raw(facilities.cash[f] - buffer, config.dividend_share.raw());
                facilities.cash[f] -= dividend;
                *year += dividend;
                match facilities.group[f] {
                    g if g >= 0 => self.group_dividend[g as usize] += dividend,
                    _ => households[node] += dividend,
                }
            }
            // A facility held for auction has no operator to fail; its sale settles the arrears.
            if facilities.cash[f] >= 0 || facilities.owner[f] == OWNER_BANK {
                facilities.negative_cash_weeks[f] = 0;
                continue;
            }
            facilities.negative_cash_weeks[f] += 1;
            if facilities.negative_cash_weeks[f] >= config.facility_bankrupt_weeks {
                self.bankrupt.push(f);
            }
        }
        *world.get_mut::<FacilityTable>() = facilities;
        world.get_mut::<IncomeTaxState>().facility_dividends = dividends_year;
    }
}

/// Before the week's dividends: what each facility with outside holders had paid this year, in
/// order of first appearance (the C# dictionary's insertion order).
pub fn snapshot_dividends(world: &WorldState) -> Vec<(usize, i64)> {
    let shares = world.get::<ShareTable>();
    let year = &world.get::<IncomeTaxState>().facility_dividends;
    let mut out: Vec<(usize, i64)> = Vec::new();
    for &f in &shares.facility {
        let f = f as usize;
        if !out.iter().any(|&(g, _)| g == f) {
            out.push((f, year[f]));
        }
    }
    out
}

/// After them: the holders' part of each facility's week of dividends, taken from its owner.
pub fn pay_holders(
    world: &mut WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    db: &GameDatabase,
    before: &[(usize, i64)],
) {
    for &(f, was) in before {
        let week = world.get::<IncomeTaxState>().facility_dividends[f] - was;
        if week <= 0 {
            continue;
        }
        let rows: Vec<(i32, i32)> = {
            let shares = world.get::<ShareTable>();
            (0..shares.count())
                .filter(|&r| shares.facility[r] as usize == f)
                .map(|r| (shares.holder[r], shares.count[r]))
                .collect()
        };
        let mut paid = 0;
        for (holder, count) in rows {
            let part = week * i64::from(count) / i64::from(db.config.facility_shares);
            pay_holder(world, nodes, banks, f, holder, part);
            paid += part;
        }
        let (group, node) = {
            let facilities = world.get::<FacilityTable>();
            (facilities.group[f], facilities.node[f])
        };
        if group >= 0 {
            world.get_mut::<GroupTable>().cash[group as usize] -= paid;
        } else {
            pay_households(world, nodes, node, -paid);
        }
    }
}

fn pay_holder(
    world: &mut WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    f: usize,
    holder: i32,
    amount: i64,
) {
    if amount <= 0 {
        return;
    }
    let node = world.get::<FacilityTable>().node[f];
    if holder == PLAYER_HOLDER {
        let nation = banks.nation_of(nodes, node) as usize;
        let bank = world.get_mut::<BankState>();
        bank.player_deposit[nation] += amount;
        bank.reserve[nation] += amount;
    } else if holder < 0 {
        pay_nation_households(world, -1 - holder, amount);
    } else {
        world.get_mut::<GroupTable>().cash[holder as usize] += amount;
    }
}

/// Wages leaving villages go to their nation's households (C# `PayWagesAway`).
pub fn pay_wages_away(
    world: &mut WorldState,
    nodes: &NodeTable,
    db: &GameDatabase,
    node_wages_away: &mut [i64],
) {
    for (n, away) in node_wages_away.iter_mut().enumerate() {
        if *away == 0 {
            continue;
        }
        let nation = world.get::<PurseState>().node_float[n];
        pay_nation_wages(world, nodes, &db.config, nation, *away);
        *away = 0;
    }
}

/// A group keeps part of its dividends and passes the rest on; a state enterprise's go to the state
/// (C# `PassOnDividends`).
pub fn pass_on_dividends(
    world: &mut WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    db: &GameDatabase,
    group_dividend: &[i64],
) {
    for (g, &dividend) in group_dividend.iter().enumerate() {
        if dividend == 0 {
            continue;
        }
        let (kind, node) = {
            let groups = world.get::<GroupTable>();
            (groups.kind[g], groups.node[g])
        };
        let state = kind == KIND_STATE_ENTERPRISE;
        let share = if state {
            db.config.state_facility_levy
        } else {
            db.config.group_payout_share
        };
        let passed = share.mul_int(dividend);
        if state {
            to_state(world, nodes, banks, node, passed);
        } else {
            pay_households(world, nodes, node, passed);
        }
        world.get_mut::<GroupTable>().cash[g] += dividend - passed;
    }
}
