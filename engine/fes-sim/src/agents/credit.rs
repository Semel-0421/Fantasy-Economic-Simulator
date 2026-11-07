//! Bills, bond holdings and credit statistics (C# `Credit.cs`; sections `agents.bills`,
//! `agents.bill_room`, `agents.bonds`). `CreditStats` and `BondOffers` are unsaved services.
use super::columns::{native_table, read_col, write_col};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

native_table! {
    /// Bills of exchange (C# `BillTable`, `agents.bills`, version 1).
    BillTable, "agents.bills", 1,
    {
        holder: i32,
        purse: i32,
        face: i64,
        due_turn: i32,
    }
}

native_table! {
    /// Bonds by issuer and holder (C# `BondTable`, `agents.bonds`, version 1).
    BondTable, "agents.bonds", 1,
    {
        issuer: i32,
        holder: i32,
        face: i64,
        interest: i64,
        behind_weeks: i32,
    }
}

/// What each purse may still lend on bills this turn (C# `BillRoom`, `agents.bill_room`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BillRoom {
    pub room: Vec<i64>,
    pub bank_room: Vec<i64>,
    pub bank_spent: Vec<i64>,
}

impl BillRoom {
    pub fn new(floats: usize) -> Self {
        Self {
            room: vec![0; floats],
            bank_room: vec![0; floats],
            bank_spent: vec![0; floats],
        }
    }
}

impl Component for BillRoom {
    fn key(&self) -> &'static str {
        "agents.bill_room"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.room);
        write_col(w, &self.bank_room);
        write_col(w, &self.bank_spent);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.room)?;
        read_col(r, &mut self.bank_room)?;
        read_col(r, &mut self.bank_spent)
    }
}

/// Credit statistics, not saved (C# `CreditStats`).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CreditStats {
    pub bills_sold: i64,
    pub bills_sold_face: i64,
    pub shares_listed: i64,
    pub bills_issued: i64,
    pub bills_discounted: i64,
    pub bills_paid: i64,
    pub bills_lost: i64,
    pub state_raised: i64,
    pub company_raised: i64,
    pub groups_topped_up: i64,
    pub bonds_paid: i64,
    pub bonds_lost: i64,
    pub defaults: i64,
}

/// Bond offers of the current turn, not saved (C# `BondOffers`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BondOffers {
    pub turn: i32,
    pub houses: Vec<i64>,
    pub groups: Vec<i64>,
    pub nations: Vec<i64>,
}

impl Default for BondOffers {
    fn default() -> Self {
        Self {
            turn: -1,
            houses: Vec::new(),
            groups: Vec::new(),
            nations: Vec::new(),
        }
    }
}
