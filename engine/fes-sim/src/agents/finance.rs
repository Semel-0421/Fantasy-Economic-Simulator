//! The banks' financial standing and the income-tax books (C# `FinanceState.cs`, `IncomeTax.cs`,
//! `MoneyTotals.cs`; sections `agents.finance`, `agents.income_tax`). Turn-0 state only.
use super::columns::{read_col, read_len, read_list, write_col};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

/// One quarter's financial record (C# `FinanceRecord`). Arrays are per nation index or per continent.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FinanceRecord {
    pub turn: i32,
    pub nation_total: Vec<i64>,
    pub nation_cash: Vec<i64>,
    pub nation_reserve: Vec<i64>,
    pub nation_deposits: Vec<i64>,
    /// Loan balances owed to the nation's bank.
    pub nation_lent: Vec<i64>,
    pub continent_total: Vec<i64>,
    /// Raw Fx.
    pub price_index: Vec<i64>,
}

impl FinanceRecord {
    pub fn new(nations: usize, continents: usize) -> Self {
        Self {
            turn: 0,
            nation_total: vec![0; nations],
            nation_cash: vec![0; nations],
            nation_reserve: vec![0; nations],
            nation_deposits: vec![0; nations],
            nation_lent: vec![0; nations],
            continent_total: vec![0; continents],
            price_index: vec![0; continents],
        }
    }

    fn arrays_mut(&mut self) -> [&mut Vec<i64>; 7] {
        [
            &mut self.nation_total,
            &mut self.nation_cash,
            &mut self.nation_reserve,
            &mut self.nation_deposits,
            &mut self.nation_lent,
            &mut self.continent_total,
            &mut self.price_index,
        ]
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.turn);
        for a in [
            &self.nation_total,
            &self.nation_cash,
            &self.nation_reserve,
            &self.nation_deposits,
            &self.nation_lent,
            &self.continent_total,
            &self.price_index,
        ] {
            write_col(w, a);
        }
    }

    fn read(&mut self, r: &mut DotNetReader) -> Result<(), IoError> {
        self.turn = r.read_i32()?;
        for a in self.arrays_mut() {
            read_col(r, a)?;
        }
        Ok(())
    }
}

/// Each nation's loan rate and financial event, the quarterly records and the published statistics
/// (C# `FinanceState`, `agents.finance`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FinanceState {
    pub nation_count: usize,
    pub continent_count: usize,
    /// Per nation: the yearly loan rate, raw Fx.
    pub loan_rate: Vec<i64>,
    pub event: Vec<u8>,
    pub event_start: Vec<i32>,
    /// Per nation: the turn of the player's last withdrawal.
    pub last_withdraw_turn: Vec<i32>,
    /// Oldest first.
    pub records: Vec<FinanceRecord>,
    /// The year the published statistics cover, or -1 before the first publication.
    pub published_year: i32,
    pub published_continent_total: Vec<i64>,
    /// Raw Fx; 1.0 until the first publication.
    pub published_price_index: Vec<i64>,
    pub published_inflation: Vec<i64>,
    pub published_nation_total: Vec<i64>,
    pub published_reserve_ratio: Vec<i64>,
    pub published_loan_rate: Vec<i64>,
}

impl FinanceState {
    pub fn new(nation_count: usize, continent_count: usize, base_rate: Fx) -> Self {
        Self {
            nation_count,
            continent_count,
            loan_rate: vec![base_rate.raw(); nation_count],
            event: vec![0; nation_count],
            event_start: vec![0; nation_count],
            last_withdraw_turn: vec![-1; nation_count],
            records: Vec::new(),
            published_year: -1,
            published_continent_total: vec![0; continent_count],
            published_price_index: vec![Fx::ONE_RAW; continent_count],
            published_inflation: vec![0; continent_count],
            published_nation_total: vec![0; nation_count],
            published_reserve_ratio: vec![0; nation_count],
            published_loan_rate: vec![0; nation_count],
        }
    }

    fn read_event(&mut self, r: &mut DotNetReader) -> Result<(), IoError> {
        read_len(r, self.event.len())?;
        let bytes = r.read_bytes(self.event.len())?;
        self.event.copy_from_slice(bytes);
        Ok(())
    }

    fn read_records(&mut self, r: &mut DotNetReader) -> Result<(), IoError> {
        let count = r.read_i32()?;
        if count < 0 {
            return Err(IoError::InvalidLength);
        }
        self.records.clear();
        for _ in 0..count {
            let mut record = FinanceRecord::new(self.nation_count, self.continent_count);
            record.read(r)?;
            self.records.push(record);
        }
        Ok(())
    }
}

impl Component for FinanceState {
    fn key(&self) -> &'static str {
        "agents.finance"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.loan_rate);
        let _ = w.write_i32(self.event.len() as i32);
        let _ = w.write_bytes(&self.event);
        write_col(w, &self.event_start);
        write_col(w, &self.last_withdraw_turn);
        let _ = w.write_i32(self.records.len() as i32);
        for record in &self.records {
            record.write(w);
        }
        let _ = w.write_i32(self.published_year);
        write_col(w, &self.published_continent_total);
        write_col(w, &self.published_price_index);
        write_col(w, &self.published_inflation);
        write_col(w, &self.published_nation_total);
        write_col(w, &self.published_reserve_ratio);
        write_col(w, &self.published_loan_rate);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.loan_rate)?;
        self.read_event(r)?;
        read_col(r, &mut self.event_start)?;
        read_col(r, &mut self.last_withdraw_turn)?;
        self.read_records(r)?;
        self.published_year = r.read_i32()?;
        read_col(r, &mut self.published_continent_total)?;
        read_col(r, &mut self.published_price_index)?;
        read_col(r, &mut self.published_inflation)?;
        read_col(r, &mut self.published_nation_total)?;
        read_col(r, &mut self.published_reserve_ratio)?;
        read_col(r, &mut self.published_loan_rate)
    }
}

/// Facility and merchant profit books and last year's tax by continent (C# `IncomeTaxState`,
/// `agents.income_tax`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct IncomeTaxState {
    pub facility_start: Vec<i64>,
    pub facility_dividends: Vec<i64>,
    pub merchant_start: Vec<i64>,
    pub opened: bool,
    pub last_households: Vec<i64>,
    pub last_companies: Vec<i64>,
}

impl IncomeTaxState {
    pub fn new(continents: usize) -> Self {
        Self {
            facility_start: Vec::new(),
            facility_dividends: Vec::new(),
            merchant_start: Vec::new(),
            opened: false,
            last_households: vec![0; continents],
            last_companies: vec![0; continents],
        }
    }

    /// C# `RestartMerchant`: a replaced group's book starts over.
    pub fn restart_merchant(&mut self, a: usize, cash: i64) {
        if let Some(start) = self.merchant_start.get_mut(a) {
            *start = cash;
        }
    }
}

impl Component for IncomeTaxState {
    fn key(&self) -> &'static str {
        "agents.income_tax"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_bool(self.opened);
        write_col(w, &self.facility_start);
        write_col(w, &self.facility_dividends);
        write_col(w, &self.merchant_start);
        for (households, companies) in self.last_households.iter().zip(&self.last_companies) {
            let _ = w.write_i64(*households);
            let _ = w.write_i64(*companies);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        self.opened = r.read_bool()?;
        read_list(r, &mut self.facility_start)?;
        read_list(r, &mut self.facility_dividends)?;
        read_list(r, &mut self.merchant_start)?;
        for (households, companies) in self
            .last_households
            .iter_mut()
            .zip(self.last_companies.iter_mut())
        {
            *households = r.read_i64()?;
            *companies = r.read_i64()?;
        }
        Ok(())
    }
}

/// Money totals of the current turn, recomputed each turn and not saved (C# `MoneyTotals`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MoneyTotals {
    /// Per nation index, in its continent's coin.
    pub nation_cash: Vec<i64>,
    pub nation_reserve: Vec<i64>,
    pub nation_deposits: Vec<i64>,
    /// Per continent: every nation's total, the international bank, and coins held outside any nation.
    pub continent_total: Vec<i64>,
    /// Per continent: coins held outside its nations.
    pub continent_loose: Vec<i64>,
}

impl MoneyTotals {
    pub fn new(nation_count: usize, continent_count: usize) -> Self {
        Self {
            nation_cash: vec![0; nation_count],
            nation_reserve: vec![0; nation_count],
            nation_deposits: vec![0; nation_count],
            continent_total: vec![0; continent_count],
            continent_loose: vec![0; continent_count],
        }
    }

    pub fn nation_total(&self, nation: usize) -> i64 {
        self.nation_cash[nation] + self.nation_reserve[nation]
    }
}
