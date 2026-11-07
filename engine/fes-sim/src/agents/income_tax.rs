//! `agents.income_tax` (C# `IncomeTaxSystem`, `Agents/IncomeTax.cs`): once a year households pay
//! on what they measurably received and facilities and merchant groups on their profit; the
//! money is split between the nobles and the state (DEC-076, DEC-107).
use super::facilities::{FacilityTable, OWNER_BANK, OWNER_NPC};
use super::finance::IncomeTaxState;
use super::loans::NpcLoanTable;
use super::merchants::AgentTable;
use super::money_ledger::{merchant_settles_at, pay_local_households};
use super::nobles::collect_tax;
use super::orders::MerchantTripStats;
use crate::context::SimContext;
use crate::economy::HouseholdCash;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_data::database::GameDatabase;
use std::sync::Arc;

/// C# `FinanceOrder.IncomeTax`.
pub const ORDER: i32 = sim_order::LATE + 15;

/// The year's income tax was collected (C# `IncomeTaxCollected`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct IncomeTaxCollected {
    /// The year taxed.
    pub year: i32,
    pub households: i64,
    pub companies: i64,
}

/// A facility closed after years of losses (C# `FacilityClosed`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FacilityClosed {
    pub facility: i32,
    pub turn: i32,
}

impl IncomeTaxState {
    /// C# `EnsureRows`: companies founded since the last call start their books with their cash.
    pub fn ensure_rows(&mut self, facilities: &FacilityTable, agents: &AgentTable) {
        for f in self.facility_start.len()..facilities.count() {
            self.facility_start.push(facilities.cash[f]);
            self.facility_dividends.push(0);
        }
        for a in self.merchant_start.len()..agents.count() {
            self.merchant_start.push(agents.cash[a]);
        }
    }

    /// C# `FacilityProfit`: the change in its cash plus the dividends it paid.
    pub fn facility_profit(&self, f: usize, cash: i64) -> i64 {
        if f < self.facility_start.len() {
            cash - self.facility_start[f] + self.facility_dividends[f]
        } else {
            0
        }
    }

    /// C# `OpenBooks`.
    fn open_books(&mut self, facilities: &FacilityTable, agents: &AgentTable) {
        for f in 0..facilities.count() {
            self.facility_start[f] = facilities.cash[f];
            self.facility_dividends[f] = 0;
        }
        for a in 0..agents.count() {
            self.merchant_start[a] = agents.cash[a];
        }
        self.opened = true;
    }
}

/// Runs `f` on the books beside the facilities and merchants.
fn with_books<R>(
    world: &mut WorldState,
    f: impl FnOnce(&mut IncomeTaxState, &FacilityTable, &AgentTable) -> R,
) -> R {
    let mut books = std::mem::replace(world.get_mut::<IncomeTaxState>(), IncomeTaxState::new(0));
    let result = f(
        &mut books,
        world.get::<FacilityTable>(),
        world.get::<AgentTable>(),
    );
    *world.get_mut::<IncomeTaxState>() = books;
    result
}

/// `agents.income_tax`.
pub struct IncomeTaxSystem;

impl System for IncomeTaxSystem {
    fn id(&self) -> &'static str {
        "agents.income_tax"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let opened = with_books(&mut ctx.world, |books, facilities, agents| {
            books.ensure_rows(facilities, agents);
            let opened = books.opened;
            if !opened {
                books.open_books(facilities, agents);
            }
            opened
        });
        if !opened {
            return Ok(());
        }
        if !ctx.clock().is_new_year() || ctx.turn() == 0 {
            return Ok(());
        }
        let year = ctx.clock().year() - 1;
        let turn = ctx.turn();
        let mut t = Taxer {
            world: &mut ctx.world,
            events: &mut ctx.events,
            db: Arc::clone(&ctx.services.database),
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
            turn,
        };
        let (households, companies, spent) = t.collect();
        ctx.services.get_mut::<MerchantTripStats>().spent_tax += spent;
        ctx.events.publish(IncomeTaxCollected {
            year,
            households,
            companies,
        });
        Ok(())
    }
}

struct Taxer<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    db: Arc<GameDatabase>,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    turn: i32,
}

impl Taxer<'_> {
    /// Returns what households and companies paid, and what merchants paid.
    fn collect(&mut self) -> (i64, i64, i64) {
        let books = self.world.get_mut::<IncomeTaxState>();
        books.last_households.fill(0);
        books.last_companies.fill(0);
        let config = &self.db.config;
        let mut households = 0i64;
        for node in 0..self.nodes.count() {
            let cash = self.world.get::<HouseholdCash>();
            let income = cash.year_income(node).max(0);
            let paid = config
                .tax_household
                .mul_int(income)
                .min(cash.cash[node].max(0));
            if paid <= 0 {
                continue;
            }
            self.world.get_mut::<HouseholdCash>().cash[node] -= paid;
            collect_tax(
                self.world,
                self.nodes,
                self.banks,
                config,
                node as i32,
                paid,
            );
            let continent = self.nodes.continent[node] as usize;
            self.world.get_mut::<IncomeTaxState>().last_households[continent] += paid;
            households += paid;
        }
        let mut companies = 0i64;
        for f in 0..self.world.get::<FacilityTable>().count() {
            let facilities = self.world.get::<FacilityTable>();
            if facilities.owner[f] == OWNER_BANK {
                continue;
            }
            let (node, cash) = (facilities.node[f], facilities.cash[f]);
            let profit = self.world.get::<IncomeTaxState>().facility_profit(f, cash);
            let paid = self.pay(node, profit, cash);
            self.world.get_mut::<FacilityTable>().cash[f] -= paid;
            companies += paid;
            self.close_after_losses(f, profit);
        }
        let mut spent = 0i64;
        for a in 0..self.world.get::<AgentTable>().count() {
            let agents = self.world.get::<AgentTable>();
            let node = merchant_settles_at(self.nodes, agents.node[a], agents.home[a]);
            let cash = agents.cash[a];
            let profit = cash - self.world.get::<IncomeTaxState>().merchant_start[a];
            let paid = self.pay(node, profit, cash);
            self.world.get_mut::<AgentTable>().cash[a] -= paid;
            spent += paid;
            companies += paid;
        }
        with_books(self.world, |books, facilities, agents| {
            books.open_books(facilities, agents)
        });
        (households, companies, spent)
    }

    /// C# `Pay`.
    fn pay(&mut self, node: i32, profit: i64, cash: i64) -> i64 {
        let config = &self.db.config;
        let paid = config.tax_company.mul_int(profit.max(0)).min(cash.max(0));
        if paid <= 0 {
            return 0;
        }
        collect_tax(self.world, self.nodes, self.banks, config, node, paid);
        let continent = self.nodes.continent[node as usize] as usize;
        self.world.get_mut::<IncomeTaxState>().last_companies[continent] += paid;
        paid
    }

    /// C# `CloseAfterLosses`: an NPC facility that lost money for too many years closes; what it
    /// holds falls to the households around, and the row stays at level 0 (DEC-051).
    fn close_after_losses(&mut self, f: usize, profit: i64) {
        let facilities = self.world.get_mut::<FacilityTable>();
        if facilities.level[f] == 0 {
            return;
        }
        facilities.loss_years[f] = if profit < 0 && facilities.owner[f] == OWNER_NPC {
            facilities.loss_years[f] + 1
        } else {
            0
        };
        let def = facilities.def_index[f] as usize;
        if facilities.loss_years[f] < self.db.config.facility_close_loss_years
            || self.db.facilities[def].state_owned
        {
            return;
        }
        if self.world.get::<NpcLoanTable>().find(f as i32).is_some() {
            return;
        }
        let facilities = self.world.get::<FacilityTable>();
        let (node, cash) = (facilities.node[f], facilities.cash[f]);
        pay_local_households(self.world, self.nodes, node, cash);
        let facilities = self.world.get_mut::<FacilityTable>();
        facilities.cash[f] = 0;
        facilities.level[f] = 0;
        self.events.publish(FacilityClosed {
            facility: f as i32,
            turn: self.turn,
        });
    }
}
