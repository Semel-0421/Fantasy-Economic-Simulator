//! NPC and trader loans (C# `NpcLending.cs`, `TraderLoans.cs`; sections `agents.npc_loans`,
//! `agents.trader_loans`). Turn-0 state; the lending and repayment systems come later.
use super::columns::{native_table, read_items, write_items};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

native_table! {
    /// Loans of NPC facilities and merchant groups, one per borrower (C# `NpcLoanTable`,
    /// `agents.npc_loans`, version 1).
    NpcLoanTable, "agents.npc_loans", 1,
    {
        borrower: i32,
        nation: i32,
        /// Everything still owed, interest included.
        balance: i64,
        weekly_payment: i64,
        /// Owed now: this week's instalment plus arrears.
        due: i64,
        overdue_weeks: i32,
    }
}

/// What each nation's traders owe for the money lent to their float (C# `TraderLoans`,
/// `agents.trader_loans`, version 1). The columns are saved as bare elements, without lengths.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TraderLoans {
    /// Per node: lent by its households and not yet paid back.
    pub owed_households: Vec<i64>,
    /// Per node: the interest due on it.
    pub interest_households: Vec<i64>,
    /// Per nation: the same for its bank.
    pub owed_bank: Vec<i64>,
    pub interest_bank: Vec<i64>,
}

impl TraderLoans {
    pub fn new(nodes: usize, nations: usize) -> Self {
        Self {
            owed_households: vec![0; nodes],
            interest_households: vec![0; nodes],
            owed_bank: vec![0; nations],
            interest_bank: vec![0; nations],
        }
    }
}

impl Component for TraderLoans {
    fn key(&self) -> &'static str {
        "agents.trader_loans"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_items(w, &self.owed_households);
        write_items(w, &self.interest_households);
        write_items(w, &self.owed_bank);
        write_items(w, &self.interest_bank);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_items(r, &mut self.owed_households)?;
        read_items(r, &mut self.interest_households)?;
        read_items(r, &mut self.owed_bank)?;
        read_items(r, &mut self.interest_bank)
    }
}

impl NpcLoanTable {
    /// The row of `borrower`'s loan (C# `Find`: the index keeps the last row per borrower).
    pub fn find(&self, borrower: i32) -> Option<usize> {
        self.borrower.iter().rposition(|&b| b == borrower)
    }

    /// Drops a borrower's loan, if any, by swap-back (C# `WriteOff`): what is owed is lost.
    pub fn write_off(&mut self, borrower: i32) {
        if let Some(row) = self.find(borrower) {
            self.remove_row_swap_back(row);
        }
    }
}
