//! Loans of NPC facilities and merchant groups (C# `BankSystem.ServiceNpc`, `LendToNpcs`,
//! `NpcLending.Lend`, `NpcBorrowers`; DEC-037, DEC-038, DEC-044, DEC-046, DEC-061).
use super::asset_prices::{facility_upkeep, facility_value};
use super::balance_sheet::cargo_value;
use super::bank_seizure::trader_offer;
use super::bank_system::{interest, Bank};
use super::banks::BankState;
use super::cargo::{merchant_holder, CargoTable};
use super::facilities::{FacilityTable, OWNER_NPC};
use super::finance::{FinanceState, IncomeTaxState};
use super::loans::NpcLoanTable;
use super::merchants::AgentTable;
use super::money_ledger::merchant_settles_at;
use super::production::bankruptcy::{seize_facility_in, NpcLoanDefaulted};
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use crate::economy::MarketStore;
use crate::world::WorldState;
use fes_core::Fx;
use fes_data::SimulationConfig;

/// C# `FinancialEventKind.CreditCrunch`.
const CREDIT_CRUNCH: u8 = 2;
const MERCHANT_BASE: i32 = 1 << 28;

/// C# `NpcBorrowers.Merchant`.
pub fn merchant_borrower(agent: i32) -> i32 {
    MERCHANT_BASE + agent
}

impl IncomeTaxState {
    /// C# `MoveFacilityCapital`.
    pub fn move_facility_capital(&mut self, f: usize, amount: i64) {
        if let Some(start) = self.facility_start.get_mut(f) {
            *start += amount;
        }
    }

    /// C# `MoveMerchantCapital`.
    pub fn move_merchant_capital(&mut self, a: usize, amount: i64) {
        if let Some(start) = self.merchant_start.get_mut(a) {
            *start += amount;
        }
    }
}

impl NpcLoanTable {
    /// C# `Add`.
    pub fn add(&mut self, borrower: i32, nation: i32, balance: i64, weekly: i64) {
        self.borrower.push(borrower);
        self.nation.push(nation);
        self.balance.push(balance);
        self.weekly_payment.push(weekly);
        self.due.push(0);
        self.overdue_weeks.push(0);
    }
}

/// C# `NpcLending.Lend`: up to `amount` from the nation's bank, unless a credit crunch stops new
/// loans, and no more than the config share of the reserve. Returns what was lent.
pub fn lend(
    world: &mut WorldState,
    config: &SimulationConfig,
    borrower: i32,
    nation: i32,
    amount: i64,
) -> i64 {
    let n = nation as usize;
    let finance = world.get::<FinanceState>();
    if finance.event[n] == CREDIT_CRUNCH {
        return 0;
    }
    let rate = Fx::from_raw(finance.loan_rate[n]);
    let bank = world.get_mut::<BankState>();
    let amount = amount.min(
        config
            .npc_loan_max_reserve_share
            .mul_int(bank.reserve[n].max(0)),
    );
    if amount <= 0 {
        return 0;
    }
    let term = config.npc_loan_term_weeks;
    let balance = amount + interest(rate, amount, term);
    let weekly = (balance + i64::from(term) - 1) / i64::from(term);
    bank.reserve[n] -= amount;
    world
        .get_mut::<NpcLoanTable>()
        .add(borrower, nation, balance, weekly);
    amount
}

impl Bank<'_> {
    /// Instalments come out of the borrower's cash; after the grace period the bank takes the
    /// facility, or a merchant group's cargo, and writes off what that does not cover.
    pub(crate) fn service_npc(&mut self, l: usize) {
        let loans = self.world.get::<NpcLoanTable>();
        let (borrower, nation) = (loans.borrower[l], loans.nation[l]);
        let merchant = borrower >= MERCHANT_BASE;
        let a = (borrower.wrapping_sub(MERCHANT_BASE)) as usize;
        let cash = if merchant {
            self.world.get::<AgentTable>().cash[a]
        } else {
            self.world.get::<FacilityTable>().cash[borrower as usize]
        };
        let loans = self.world.get_mut::<NpcLoanTable>();
        loans.due[l] = loans.balance[l].min(loans.due[l] + loans.weekly_payment[l]);
        let paid = loans.due[l].min(cash.max(0));
        if merchant {
            self.world.get_mut::<AgentTable>().cash[a] -= paid;
            self.world
                .get_mut::<IncomeTaxState>()
                .move_merchant_capital(a, -paid);
            self.spent_loans += paid;
        } else {
            let f = borrower as usize;
            self.world.get_mut::<FacilityTable>().cash[f] -= paid;
            self.world
                .get_mut::<IncomeTaxState>()
                .move_facility_capital(f, -paid);
        }
        let loans = self.world.get_mut::<NpcLoanTable>();
        loans.balance[l] -= paid;
        loans.due[l] -= paid;
        self.world.get_mut::<BankState>().reserve[nation as usize] += paid;
        let loans = self.world.get_mut::<NpcLoanTable>();
        if loans.balance[l] == 0 {
            loans.remove_row_swap_back(l);
            return;
        }
        loans.overdue_weeks[l] = if loans.due[l] > 0 {
            loans.overdue_weeks[l] + 1
        } else {
            0
        };
        if loans.overdue_weeks[l] <= self.r.db.config.loan_grace_weeks {
            return;
        }
        if merchant {
            self.seize_merchant_cargo(l, a, nation);
        } else {
            seize_facility_in(
                self.world,
                self.events,
                self.r,
                self.turn,
                borrower as usize,
            );
        }
    }

    /// Only what the traders there can pay for is sold, and only at home; proceeds beyond the
    /// debt go to the bank too (DEC-039, DEC-061).
    fn seize_merchant_cargo(&mut self, l: usize, a: usize, nation: i32) {
        let holder = merchant_holder(a as i32);
        let spread = self.r.db.config.trader_spread;
        let (node, home) = {
            let agents = self.world.get::<AgentTable>();
            (agents.node[a], agents.home[a])
        };
        let continent = &self.r.nodes.continent;
        for (good, quantity) in self.world.get::<CargoTable>().collect(holder) {
            if continent[node as usize] != continent[home as usize] {
                break; // out of the bank's reach abroad
            }
            let markets = self.world.get::<MarketStore>();
            let Some((_, cell)) = find_nearest_market(self.r.nodes, markets, node, good) else {
                continue;
            };
            let price = i64::from(trader_offer(spread, markets.price[cell]));
            let units = self
                .world
                .get::<PurseState>()
                .sellable(node, quantity, price);
            if units <= 0 {
                continue;
            }
            let worth = i64::from(units) * price;
            self.world
                .get_mut::<CargoTable>()
                .change(holder, good, -units);
            let supply = &mut self.world.get_mut::<MarketStore>().supply[cell];
            *supply = supply.wrapping_add(units);
            self.world.get_mut::<BankState>().reserve[nation as usize] += worth;
            self.world
                .get_mut::<PurseState>()
                .change_float(node, -worth);
        }
        self.world.get_mut::<AgentTable>().load_cost[a] = 0;
        self.world.get_mut::<NpcLoanTable>().remove_row_swap_back(l);
        let nation_node = self.r.banks.nation_node(self.r.nodes, nation);
        self.events.publish(NpcLoanDefaulted {
            facility: -1,
            merchant: a as i32,
            nation_node,
            turn: self.turn,
        });
    }

    /// NPCs whose cash ran out borrow the shortfall plus a few weeks of upkeep, up to the
    /// loan-to-value share of their collateral: a facility's value, or a group's cargo.
    pub(crate) fn lend_to_npcs(&mut self) {
        let config = &self.r.db.config;
        if config.npc_loan_max_reserve_share.raw() <= 0 {
            return;
        }
        for f in 0..self.world.get::<FacilityTable>().count() {
            let facilities = self.world.get::<FacilityTable>();
            if facilities.cash[f] >= 0 || facilities.owner[f] != OWNER_NPC {
                continue;
            }
            let nation = self.r.banks.nation_of(self.r.nodes, facilities.node[f]);
            if nation < 0 || self.world.get::<NpcLoanTable>().find(f as i32).is_some() {
                continue;
            }
            let upkeep = facility_upkeep(self.r.db, facilities, f);
            let need = -facilities.cash[f] + i64::from(config.npc_loan_buffer_weeks) * upkeep;
            let limit = config
                .loan_to_value
                .mul_int(facility_value(self.r.db, facilities, f));
            let lent = lend(self.world, config, f as i32, nation, need.min(limit));
            self.world.get_mut::<FacilityTable>().cash[f] += lent;
            self.world
                .get_mut::<IncomeTaxState>()
                .move_facility_capital(f, lent);
        }
        for a in 0..self.world.get::<AgentTable>().count() {
            self.lend_to_merchant(a);
        }
    }

    fn lend_to_merchant(&mut self, a: usize) {
        let config = &self.r.db.config;
        let agents = self.world.get::<AgentTable>();
        if agents.cash[a] >= 0 || agents.owner[a] != OWNER_NPC {
            return;
        }
        let node = merchant_settles_at(self.r.nodes, agents.node[a], agents.home[a]);
        let nation = self.r.banks.nation_of(self.r.nodes, node);
        let borrower = merchant_borrower(a as i32);
        if nation < 0 || self.world.get::<NpcLoanTable>().find(borrower).is_some() {
            return;
        }
        let upkeep = config.merchant_upkeep[usize::from(agents.scale[a])];
        let need = -agents.cash[a] + i64::from(config.npc_loan_buffer_weeks) * upkeep;
        let collateral = cargo_value(self.world, self.r, merchant_holder(a as i32), node);
        let limit = config.loan_to_value.mul_int(collateral);
        let lent = lend(self.world, config, borrower, nation, need.min(limit));
        self.world.get_mut::<AgentTable>().cash[a] += lent;
        self.world
            .get_mut::<IncomeTaxState>()
            .move_merchant_capital(a, lent);
    }
}
