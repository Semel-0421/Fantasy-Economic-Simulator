//! State banks and the player's loans and auctions (C# `BankTables.cs`; sections `agents.banks`,
//! `agents.margin`, `agents.loans`, `agents.auctions`). Turn-0 state; `BankSystem` comes later.
use super::columns::{native_table, read_col, write_col};
use crate::economy::CurrencyState;
use crate::world::{Component, NodeBanks, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

/// Money held by the state banks (C# `BankState`, section `agents.banks`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BankState {
    /// Per nation index: the bank's own coins, deposits included.
    pub reserve: Vec<i64>,
    /// Per nation index: the player's deposit.
    pub player_deposit: Vec<i64>,
    /// Per continent: coins the international bank holds.
    pub international_reserve: Vec<i64>,
    /// Per continent: coin supply already handed to the banks.
    pub distributed_supply: Vec<i64>,
}

impl BankState {
    pub fn new(nation_count: usize, continent_count: usize) -> Self {
        Self {
            reserve: vec![0; nation_count],
            player_deposit: vec![0; nation_count],
            international_reserve: vec![0; continent_count],
            distributed_supply: vec![0; continent_count],
        }
    }

    /// C# `BankSystem.SetUpReserves`: each continent's initial share of the coin supply goes to its
    /// nations' banks, the remainder to the international bank.
    pub fn set_up_reserves(
        &mut self,
        banks: &NodeBanks,
        nodes: &NodeTable,
        currency: &CurrencyState,
        initial_share: Fx,
    ) {
        for c in 0..currency.continent_count() {
            let amount = initial_share.mul_int(currency.coin_supply[c]);
            self.distribute(banks, nodes, c as i32, amount);
            self.distributed_supply[c] = currency.coin_supply[c];
        }
    }

    pub(crate) fn distribute(
        &mut self,
        banks: &NodeBanks,
        nodes: &NodeTable,
        continent: i32,
        amount: i64,
    ) {
        let members: Vec<usize> = (0..banks.nation_count(nodes))
            .filter(|&n| nodes.continent[banks.nation_node(nodes, n) as usize] == continent)
            .map(|n| n as usize)
            .collect();
        let c = continent as usize;
        if members.is_empty() {
            self.international_reserve[c] += amount;
            return;
        }
        let share = amount / members.len() as i64;
        for n in &members {
            self.reserve[*n] += share;
        }
        self.international_reserve[c] += amount - share * members.len() as i64;
    }

    /// C# `Spendable`: the reserve above the player's deposits.
    pub fn spendable(&self, nation: usize) -> i64 {
        (self.reserve[nation] - self.player_deposit[nation]).max(0)
    }
}

impl Component for BankState {
    fn key(&self) -> &'static str {
        "agents.banks"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.reserve);
        write_col(w, &self.player_deposit);
        write_col(w, &self.international_reserve);
        write_col(w, &self.distributed_supply);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.reserve)?;
        read_col(r, &mut self.player_deposit)?;
        read_col(r, &mut self.international_reserve)?;
        read_col(r, &mut self.distributed_supply)
    }
}

/// DEC-130: the turn the bank called for margin on the player's loans, or -1
/// (C# `MarginState`, section `agents.margin`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MarginState {
    pub called_turn: i32,
}

impl Default for MarginState {
    fn default() -> Self {
        Self { called_turn: -1 }
    }
}

impl Component for MarginState {
    fn key(&self) -> &'static str {
        "agents.margin"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.called_turn);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        self.called_turn = r.read_i32()?;
        Ok(())
    }
}

native_table! {
    /// The player's loans, at most one per nation's bank (C# `LoanTable`, `agents.loans`, version 1).
    LoanTable, "agents.loans", 1,
    {
        nation: i32,
        /// Everything still owed, interest included.
        balance: i64,
        weekly_payment: i64,
        /// Owed now: this week's instalment plus arrears.
        due: i64,
        /// Consecutive weeks that ended with something still due.
        overdue_weeks: i32,
    }
}

native_table! {
    /// Seized facilities up for auction (C# `AuctionTable`, `agents.auctions`, version 2).
    AuctionTable, "agents.auctions", 2,
    {
        facility: i32,
        /// Nation index of the bank selling it.
        nation: i32,
        /// The auction closes at the end of this turn.
        closes_turn: i32,
        reserve_price: i64,
        /// The player's bid, or 0.
        player_bid: i64,
        /// 1 when the sale repays the player's loan.
        for_player_loan: u8,
    }
}
