//! `agents.finance` (C# `FinanceSystem`, `Agents/FinanceSystem.cs`, `MoneyTotals.Recompute`,
//! `FinanceRules.LoanRate`): at each quarter's end the money totals are recorded (DEC-043), loan
//! rates follow the reserve ratio (DEC-042) and financial events start or end (DEC-044, DEC-047,
//! DEC-131); at each new year the statistics of the year before are published.
use super::finance::event_kind::{BANK_RUN, CREDIT_CRUNCH, NONE, RATE_SPIKE};
use super::merchants::AgentTable;
use super::money_ledger::pay_nation_households;
use super::securities::{AssetMarkets, VentureTable};
use super::{
    BankState, FacilityTable, FinanceRecord, FinanceState, GroupTable, LoanTable, MoneyTotals,
    NpcLoanTable, PlayerState, PurseState,
};
use crate::clock::WEEKS_PER_QUARTER;
use crate::context::SimContext;
use crate::economy::{CurrencyState, HouseholdCash};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx};
use fes_data::SimulationConfig;
use std::sync::Arc;

/// C# `FinanceOrder.Records`.
pub const ORDER: i32 = sim_order::LATE + 10;

/// A financial event started in a nation (C# `FinancialEventStarted`); `nation` is its node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FinancialEventStarted {
    pub nation: i32,
    pub kind: u8,
    pub turn: i32,
}

/// A financial event ended (C# `FinancialEventEnded`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FinancialEventEnded {
    pub nation: i32,
    pub kind: u8,
    pub turn: i32,
}

/// The year's statistics were published (C# `StatisticsPublished`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct StatisticsPublished {
    pub year: i32,
}

/// C# `FinanceRules.LoanRate`: the base rate, raised as the reserve ratio falls below the target
/// up to the cap, and lifted by a rate spike or (when configured) a credit crunch.
pub fn loan_rate(config: &SimulationConfig, reserve_ratio: Fx, event: u8) -> Fx {
    let mut multiplier = Fx::ONE;
    if reserve_ratio < config.target_reserve_ratio {
        multiplier = if reserve_ratio.raw() <= 0 {
            config.max_loan_rate_multiplier
        } else {
            Fx::min(
                config.target_reserve_ratio / reserve_ratio,
                config.max_loan_rate_multiplier,
            )
        };
    }
    let rate = config.loan_interest_per_year * multiplier;
    let lifted = event == RATE_SPIKE || (event == CREDIT_CRUNCH && config.crunch_lifts_rates);
    if lifted {
        rate * config.rate_spike_multiplier
    } else {
        rate
    }
}

impl MoneyTotals {
    /// C# `Recompute`: cash where it is now (in its node's continent coin) plus bank reserves.
    pub fn recompute(&mut self, world: &WorldState, nodes: &NodeTable, banks: &NodeBanks) {
        self.nation_cash.fill(0);
        self.continent_loose.fill(0);
        let player = world.get::<PlayerState>();
        for (c, &coins) in player.coins.iter().enumerate() {
            self.add_cash(nodes, banks, player.node, c as i32, coins);
        }
        let facilities = world.get::<FacilityTable>();
        for f in 0..facilities.count() {
            self.add_here(nodes, banks, facilities.node[f], facilities.cash[f]);
        }
        let agents = world.get::<AgentTable>();
        for a in 0..agents.count() {
            // A group's coins are its home continent's, wherever it is.
            let continent = nodes.continent[agents.home[a] as usize];
            self.add_cash(nodes, banks, agents.node[a], continent, agents.cash[a]);
        }
        let groups = world.get::<GroupTable>();
        for g in 0..groups.count() {
            self.add_here(nodes, banks, groups.node[g], groups.cash[g]);
        }
        let ventures = world.get::<VentureTable>();
        for v in 0..ventures.count() {
            self.add_here(nodes, banks, ventures.port[v], ventures.cash[v]);
        }
        let purses = world.get::<PurseState>();
        for p in 0..purses.count() {
            let float = purses.float[p];
            if float <= 0 {
                continue;
            }
            if p < self.nation_cash.len() {
                self.nation_cash[p] += float;
            } else {
                self.continent_loose[purses.continent_of(p) as usize] += float;
            }
        }
        let households = world.get::<HouseholdCash>();
        for node in 0..nodes.count() {
            let cash = households.cash[node] + households.spent[node];
            self.add_here(nodes, banks, node as i32, cash);
        }
        let bank = world.get::<BankState>();
        self.nation_reserve.copy_from_slice(&bank.reserve);
        self.nation_deposits.copy_from_slice(&bank.player_deposit);
        for c in 0..self.continent_total.len() {
            self.continent_total[c] = self.continent_loose[c] + bank.international_reserve[c];
        }
        for n in 0..self.nation_cash.len() {
            let node = banks.nation_node(nodes, n as i32);
            self.continent_total[nodes.continent[node as usize] as usize] += self.nation_total(n);
        }
    }

    fn add_here(&mut self, nodes: &NodeTable, banks: &NodeBanks, node: i32, amount: i64) {
        self.add_cash(nodes, banks, node, nodes.continent[node as usize], amount);
    }

    /// Negative cash (upkeep arrears) is owed to nobody, so it is no money.
    fn add_cash(&mut self, nodes: &NodeTable, banks: &NodeBanks, node: i32, c: i32, amount: i64) {
        if amount <= 0 {
            return;
        }
        let nation = banks.nation_of(nodes, node);
        if nation >= 0 && nodes.continent[node as usize] == c {
            self.nation_cash[nation as usize] += amount;
        } else {
            self.continent_loose[c as usize] += amount;
        }
    }
}

/// `agents.finance`.
pub struct FinanceSystem;

impl System for FinanceSystem {
    fn id(&self) -> &'static str {
        "agents.finance"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = Arc::clone(&ctx.services.database);
        let config = &db.config;
        let turn = ctx.turn();
        if ctx.clock().is_new_year() && turn > 0 {
            publish(ctx, ctx.clock().year() - 1);
        }
        if !ctx.clock().is_quarter_end() {
            return Ok(());
        }
        let empty = MoneyTotals::new(0, 0);
        let mut totals = std::mem::replace(ctx.services.get_mut::<MoneyTotals>(), empty);
        let nodes = ctx.services.get::<NodeTable>();
        let banks = ctx.services.get::<NodeBanks>();
        totals.recompute(&ctx.world, nodes, banks);
        let record = record(&ctx.world, &totals, nodes, banks, turn);
        *ctx.services.get_mut::<MoneyTotals>() = totals;
        let ratios: Vec<Fx> = (0..record.nation_total.len())
            .map(|n| record.reserve_ratio(n))
            .collect();
        ctx.world.get_mut::<FinanceState>().records.push(record);
        let finance = ctx.world.get_mut::<FinanceState>();
        let history = config.finance_history_quarters.max(0) as usize;
        while finance.records.len() > history {
            finance.records.remove(0);
        }
        for (n, &ratio) in ratios.iter().enumerate() {
            update_event(ctx, config, n, ratio);
            let finance = ctx.world.get_mut::<FinanceState>();
            finance.loan_rate[n] = loan_rate(config, ratio, finance.event[n]).raw();
        }
        Ok(())
    }
}

/// C# `Record`: this quarter's totals, loans owed to each bank, and price indices.
fn record(
    world: &WorldState,
    totals: &MoneyTotals,
    nodes: &NodeTable,
    banks: &NodeBanks,
    turn: i32,
) -> FinanceRecord {
    let finance = world.get::<FinanceState>();
    let mut record = FinanceRecord::new(finance.nation_count, finance.continent_count);
    record.turn = turn;
    for n in 0..finance.nation_count {
        record.nation_total[n] = totals.nation_total(n);
        record.nation_cash[n] = totals.nation_cash[n];
        record.nation_reserve[n] = totals.nation_reserve[n];
        record.nation_deposits[n] = totals.nation_deposits[n];
    }
    let npc_loans = world.get::<NpcLoanTable>();
    for r in 0..npc_loans.count() {
        record.nation_lent[npc_loans.nation[r] as usize] += npc_loans.balance[r];
    }
    let loans = world.get::<LoanTable>();
    for r in 0..loans.count() {
        record.nation_lent[loans.nation[r] as usize] += loans.balance[r];
    }
    let markets = world.get::<AssetMarkets>();
    for n in 0..finance.nation_count {
        record.nation_lent[n] += markets.nation_debt(n as i32, nodes, banks);
    }
    let currency = world.get::<CurrencyState>();
    for c in 0..finance.continent_count {
        record.continent_total[c] = totals.continent_total[c];
        record.price_index[c] = currency.price_index[c].raw();
    }
    record
}

/// C# `UpdateEvent`: an event lasts at least a quarter and ends once the reserve ratio has
/// recovered. A nation without one has a credit crunch when one of its markets burst this
/// quarter, a run when its bank's cover is under the floor, and otherwise may draw one at random.
fn update_event(ctx: &mut SimContext, config: &SimulationConfig, n: usize, ratio: Fx) {
    let turn = ctx.turn();
    let nation = {
        let nodes = ctx.services.get::<NodeTable>();
        ctx.services.get::<NodeBanks>().nation_node(nodes, n as i32)
    };
    let finance = ctx.world.get_mut::<FinanceState>();
    let current = finance.event[n];
    if current != NONE {
        if turn - finance.event_start[n] < WEEKS_PER_QUARTER
            || ratio < config.financial_event_end_reserve_ratio
        {
            return;
        }
        finance.event[n] = NONE;
        ctx.events.publish(FinancialEventEnded {
            nation,
            kind: current,
            turn,
        });
        return;
    }
    let kind = if ctx.world.get::<AssetMarkets>().burst_nation[n] {
        CREDIT_CRUNCH
    } else if config.bank_run_cover.raw() > 0 && ratio < config.bank_run_cover {
        BANK_RUN
    } else {
        let stream = det_hash::of("agents.finance.events");
        let mut rng = DetRandom::for_key(ctx.seed(), turn, stream, n as i32, 0);
        if !rng.chance_permille(config.financial_event_chance_permille) {
            return;
        }
        (1 + rng.next_int(3)) as u8
    };
    let finance = ctx.world.get_mut::<FinanceState>();
    finance.event[n] = kind;
    finance.event_start[n] = turn;
    // A run starts with the public taking a share of the bank's coins out, to the households.
    if kind == BANK_RUN {
        let bank = ctx.world.get_mut::<BankState>();
        let drain = config.bank_run_drain.mul_int(bank.reserve[n].max(0));
        bank.reserve[n] -= drain;
        pay_nation_households(&mut ctx.world, n as i32, drain);
    }
    ctx.events
        .publish(FinancialEventStarted { nation, kind, turn });
}

/// C# `Publish`: the statistics of the year that just ended, from its last quarter's record.
fn publish(ctx: &mut SimContext, year: i32) {
    let (finance, currency) = ctx.world.get2_mut::<FinanceState, CurrencyState>();
    let Some(record) = finance.records.last() else {
        return;
    };
    let record = record.clone();
    finance.published_year = year;
    for c in 0..finance.continent_count {
        finance.published_continent_total[c] = record.continent_total[c];
        currency.money_total[c] = record.continent_total[c]; // prices follow it (DEC-062)
        let previous = Fx::from_raw(finance.published_price_index[c]);
        let index = Fx::from_raw(record.price_index[c]);
        finance.published_inflation[c] = (index / previous - Fx::ONE).raw();
        finance.published_price_index[c] = index.raw();
    }
    for n in 0..finance.nation_count {
        finance.published_nation_total[n] = record.nation_total[n];
        finance.published_reserve_ratio[n] = record.reserve_ratio(n).raw();
        finance.published_loan_rate[n] = finance.loan_rate[n];
    }
    ctx.events.publish(StatisticsPublished { year });
}
