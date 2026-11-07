//! Households' money (C# `HouseholdCash`, section `economy.household_cash`, version 7; DEC-061, P13b).
//! The settlement and budget maths serve `economy.pricing`.
use super::{read_i32s, read_i64s, write_i32s, write_i64s};
use crate::clock::WEEKS_PER_YEAR;
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

/// Income quintiles per node (C# `HouseholdDemand.Quintiles`).
pub const QUINTILES: usize = 5;

/// A good the market has never offered: older than any memory of it.
pub const NEVER_OFFERED: i32 = -1_000_000;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HouseholdCash {
    /// Per node: the households' coins.
    pub cash: Vec<i64>,
    /// Per node: paid for consumption this turn, not yet handed to the traders.
    pub spent: Vec<i64>,
    /// Per node: last turn's wished consumption the markets could deliver, before the budget.
    pub desired: Vec<i64>,
    /// Per node: the part of `desired` that is necessities, bought first (DEC-083).
    pub desired_necessity: Vec<i64>,
    /// Per cell: household units consumed since the market last repriced, not yet paid for.
    pub pending_units: Vec<i32>,
    /// Per cell: the last turn the market offered the good (P13d).
    pub last_offered: Vec<i32>,
    /// Per node: paid for consumption since the year began (P13c).
    pub spent_year: Vec<i64>,
    /// Per node: cash when the year began.
    pub cash_year_start: Vec<i64>,
    /// Per node: lent out since the year began, less what came back, with the interest earned.
    pub saved_year: Vec<i64>,
    /// Per node and income quintile, poorest first: each one's part of `cash` at the last settlement (DEC-115).
    pub quintile_cash: Vec<i64>,
    /// Per node: `spent_year` and `spent` as of the last settlement.
    pub spent_settled: Vec<i64>,
    /// Per cell: what households made and have not eaten (DEC-117, DEC-118).
    pub home_grown: Vec<i32>,
}

impl HouseholdCash {
    pub fn new(node_count: usize, cell_count: usize) -> Self {
        Self {
            cash: vec![0; node_count],
            spent: vec![0; node_count],
            desired: vec![0; node_count],
            desired_necessity: vec![0; node_count],
            pending_units: vec![0; cell_count],
            last_offered: vec![NEVER_OFFERED; cell_count],
            spent_year: vec![0; node_count],
            cash_year_start: vec![0; node_count],
            saved_year: vec![0; node_count],
            quintile_cash: vec![0; node_count * QUINTILES],
            spent_settled: vec![0; node_count],
            home_grown: vec![0; cell_count],
        }
    }
}

impl Component for HouseholdCash {
    fn key(&self) -> &'static str {
        "economy.household_cash"
    }

    fn version(&self) -> i32 {
        7
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_i64s(w, &self.cash);
        write_i64s(w, &self.spent);
        write_i64s(w, &self.desired);
        write_i32s(w, &self.pending_units);
        write_i64s(w, &self.spent_year);
        write_i64s(w, &self.cash_year_start);
        write_i32s(w, &self.last_offered);
        write_i64s(w, &self.desired_necessity);
        write_i64s(w, &self.saved_year);
        write_i64s(w, &self.quintile_cash);
        write_i64s(w, &self.spent_settled);
        write_i32s(w, &self.home_grown);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_i64s(r, &mut self.cash)?;
        read_i64s(r, &mut self.spent)?;
        read_i64s(r, &mut self.desired)?;
        read_i32s(r, &mut self.pending_units)?;
        read_i64s(r, &mut self.spent_year)?;
        read_i64s(r, &mut self.cash_year_start)?;
        read_i32s(r, &mut self.last_offered)?;
        read_i64s(r, &mut self.desired_necessity)?;
        read_i64s(r, &mut self.saved_year)?;
        read_i64s(r, &mut self.quintile_cash)?;
        read_i64s(r, &mut self.spent_settled)?;
        read_i32s(r, &mut self.home_grown)
    }
}

impl HouseholdCash {
    /// C# `HouseholdCash.StartYear`: starts measuring a new year's income.
    pub fn start_year(&mut self) {
        for n in 0..self.cash.len() {
            self.cash_year_start[n] = self.cash[n] + self.spent[n];
            self.spent_settled[n] -= self.spent_year[n];
            self.spent_year[n] = 0;
            self.saved_year[n] = 0;
        }
    }
}

/// C# `HouseholdCash.Share`: the share (raw Q16, at most 1) of `desired` that `budget` pays for.
fn share(budget: i64, desired: i64) -> i64 {
    if desired <= 0 {
        return Fx::ONE_RAW;
    }
    if budget <= 0 {
        return 0;
    }
    if desired <= budget {
        return Fx::ONE_RAW;
    }
    (budget << Fx::FRACTION_BITS) / desired
}

impl HouseholdCash {
    fn week_budget(&self, row: usize, budget_weeks: i32) -> i64 {
        self.quintile_cash[row].max(0) / i64::from(budget_weeks.max(1))
    }

    /// C# `NecessityBudgetFactor` with alms (DEC-083, DEC-115, DEC-127): the share (raw Q16, at most
    /// 1) of their wished necessities a node's households can pay for this week.
    pub fn necessity_budget_factor(&self, node: usize, budget_weeks: i32, alms_raw: i64) -> i64 {
        let start = node * QUINTILES;
        let top = start + QUINTILES - 1;
        let fifth = self.desired_necessity[node] / QUINTILES as i64;
        let (mut paid, mut shortfall) = (0i64, 0i64);
        for row in start..start + QUINTILES {
            let own = self.week_budget(row, budget_weeks).min(fifth);
            paid += own;
            if row != top {
                shortfall += fifth - own;
            }
        }
        paid += shortfall.min(Fx::mul_int_raw(self.quintile_cash[top].max(0), alms_raw));
        share(paid, fifth * QUINTILES as i64)
    }

    /// C# `LuxuryWealthMul` (DEC-127): luxury demand grows with what the top quintile holds.
    pub fn luxury_wealth_mul(
        &self,
        node: usize,
        weekly_income: i64,
        per_year_raw: i64,
        cap_raw: i64,
    ) -> i64 {
        let held = self.quintile_cash[node * QUINTILES + QUINTILES - 1].max(0);
        let years = Fx::from_ratio(
            held,
            weekly_income.wrapping_mul(i64::from(WEEKS_PER_YEAR)).max(1),
        )
        .raw();
        let mul = Fx::ONE_RAW + Fx::round_shift(per_year_raw.wrapping_mul(years));
        mul.min(cap_raw)
    }

    /// C# `LuxuryBudgetFactor`: the share of their wished luxuries what each quintile has left after
    /// necessities pays for.
    pub fn luxury_budget_factor(&self, node: usize, budget_weeks: i32) -> i64 {
        let start = node * QUINTILES;
        let fifth = self.desired_necessity[node] / QUINTILES as i64;
        let mut spare = 0i64;
        for row in start..start + QUINTILES {
            spare += (self.week_budget(row, budget_weeks) - fifth).max(0);
        }
        share(spare, self.desired[node] - self.desired_necessity[node])
    }

    /// C# `Settle`: brings a node's quintiles up to its cash. Income since the last settlement goes
    /// by income share; what was spent comes first from each one's fifth of the necessities, the
    /// rest from whoever still holds money.
    pub fn settle(&mut self, node: usize, income_shares: &[Fx]) {
        let start = node * QUINTILES;
        let spent_now = self.spent_year[node] + self.spent[node];
        let spent = spent_now - self.spent_settled[node];
        self.spent_settled[node] = spent_now;
        let held: i64 = self.quintile_cash[start..start + QUINTILES].iter().sum();
        let income = self.cash[node] - held + spent;
        if income > 0 {
            self.share_income(start, income, income_shares);
        }
        let fifth = spent.min(self.desired_necessity[node]) / QUINTILES as i64;
        let mut rest = spent + (-income).max(0);
        for row in start..start + QUINTILES {
            let paid = self.quintile_cash[row].max(0).min(fifth);
            self.quintile_cash[row] -= paid;
            rest -= paid;
        }
        self.take_from_holders(start, rest);
    }

    fn share_income(&mut self, start: usize, income: i64, income_shares: &[Fx]) {
        let mut left = income;
        for (q, share) in income_shares.iter().enumerate().take(QUINTILES - 1) {
            let part = share.mul_int(income);
            self.quintile_cash[start + q] += part;
            left -= part;
        }
        self.quintile_cash[start + QUINTILES - 1] += left;
    }

    /// In proportion to what each holds; a remainder falls on the top quintile, so the parts always
    /// add up to the cash.
    fn take_from_holders(&mut self, start: usize, amount: i64) {
        let rows = start..start + QUINTILES;
        let total: i64 = self.quintile_cash[rows.clone()]
            .iter()
            .map(|c| (*c).max(0))
            .sum();
        let mut left = amount;
        if total > 0 {
            for row in rows {
                let held = self.quintile_cash[row].max(0);
                let cut = (amount / total).wrapping_mul(held)
                    + (amount % total).wrapping_mul(held) / total;
                self.quintile_cash[row] -= cut;
                left -= cut;
            }
        }
        self.quintile_cash[start + QUINTILES - 1] -= left;
    }
}
