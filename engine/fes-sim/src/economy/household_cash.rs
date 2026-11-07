//! Households' money (C# `HouseholdCash`, section `economy.household_cash`, version 7; DEC-061, P13b).
//! Only the saved columns and their turn-0 values are ported; the settlement maths comes with the
//! households system.
use super::{read_i32s, read_i64s, write_i32s, write_i64s};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

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
