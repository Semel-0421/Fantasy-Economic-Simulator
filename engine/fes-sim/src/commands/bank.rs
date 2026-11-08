//! The player's banking (C# `BankCommands.cs`).
use super::support::{
    branch, change_local_coins, fail, is_at, local_coins, local_continent, nodes, player, refs,
    run_cap, short_margin,
};
use crate::agents::balance_sheet::credit_limit;
use crate::agents::bank_system::{interest, LoanRepaid};
use crate::agents::banks::{AuctionTable, BankState, LoanTable};
use crate::agents::facilities::FacilityTable;
use crate::agents::finance::FinanceState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::economy::CurrencyState;
use crate::world::NodeBanks;
use fes_core::Fx;

/// The player borrowed from a bank (C# `LoanTaken`); `nation_node` is the lender's nation node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct LoanTaken {
    pub nation_node: i32,
    pub amount: i64,
    pub balance: i64,
    pub weekly_payment: i64,
}

/// Coins changed at an international bank (C# `CoinsExchanged`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct CoinsExchanged {
    pub from_continent: i32,
    pub to_continent: i32,
    pub paid: i64,
    pub received: i64,
    pub fee: i64,
}

/// C# `LoanTable.Find`.
pub fn find_loan(loans: &LoanTable, nation: usize) -> Option<usize> {
    (0..loans.count()).find(|&r| loans.nation[r] == nation as i32)
}

/// C# `AuctionTable.Find`.
pub fn find_auction(auctions: &AuctionTable, facility: i32) -> Option<usize> {
    (0..auctions.count()).find(|&r| auctions.facility[r] == facility)
}

fn nation_node(ctx: &SimContext, nation: usize) -> i32 {
    ctx.services
        .get::<NodeBanks>()
        .nation_node(nodes(ctx), nation as i32)
}

/// Pays local coins into (positive) or takes them out of (negative) the account at the branch
/// the player is at (C# `BankTransferCommand`, DEC-044).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BankTransfer {
    pub amount: i64,
}

impl Command for BankTransfer {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.amount == 0 || self.amount == i64::MIN {
            return fail("Amount must not be zero.");
        }
        let nation = match branch(ctx) {
            Ok(n) => n,
            Err(e) => return fail(e),
        };
        let bank = ctx.world.get::<BankState>();
        if self.amount > 0 {
            return if local_coins(ctx) >= self.amount {
                CommandResult::Ok
            } else {
                fail("Not enough cash.")
            };
        }
        if bank.player_deposit[nation] < -self.amount {
            return fail("The account does not hold that much.");
        }
        let share = ctx.services.database.config.short_open_share;
        if bank.player_deposit[nation] + self.amount < short_margin(ctx, nation, share) {
            return fail("The deposit holds the margin on short sales.");
        }
        let finance = ctx.world.get::<FinanceState>();
        if finance.event_of(nation) == crate::agents::finance::event_kind::BANK_RUN {
            if finance.last_withdraw_turn[nation] == ctx.turn() {
                return fail("During a bank run, one withdrawal a week.");
            }
            let cap = run_cap(ctx, nation);
            if -self.amount > cap {
                return fail(format!(
                    "During a bank run, at most {cap} can be withdrawn."
                ));
            }
        }
        if bank.reserve[nation] >= -self.amount {
            CommandResult::Ok
        } else {
            fail("The bank cannot pay that out now.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Ok(nation) = branch(ctx) else { return };
        change_local_coins(ctx, -self.amount);
        let bank = ctx.world.get_mut::<BankState>();
        bank.player_deposit[nation] += self.amount;
        bank.reserve[nation] += self.amount;
        if self.amount < 0 {
            let turn = ctx.turn();
            ctx.world.get_mut::<FinanceState>().last_withdraw_turn[nation] = turn;
        }
    }
}

/// Changes coins at the international bank of the player's continent (C# `ExchangeCommand`,
/// DEC-031, DEC-036, DEC-061).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Exchange {
    pub from_continent: i32,
    pub to_continent: i32,
    pub amount: i64,
}

impl Exchange {
    /// Coins received and the fee.
    fn quote(&self, ctx: &SimContext) -> (i64, i64) {
        let gross = ctx.world.get::<CurrencyState>().convert(
            self.amount,
            self.from_continent as usize,
            self.to_continent as usize,
        );
        let fee = ctx.services.database.config.exchange_fee.mul_int(gross);
        (gross - fee, fee)
    }
}

impl Command for Exchange {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let p = player(ctx);
        let continents = p.coins.len() as i32;
        let (from, to) = (self.from_continent, self.to_continent);
        if from < 0 || from >= continents || to < 0 || to >= continents || from == to {
            return fail("Pick two different currencies.");
        }
        if self.amount <= 0 {
            return fail("Amount must be positive.");
        }
        if p.is_travelling()
            || !ctx
                .services
                .get::<NodeBanks>()
                .is_international_bank(nodes(ctx), p.node)
        {
            return fail("Coins are exchanged only at an international bank.");
        }
        let local = local_continent(ctx) as i32;
        if from != local && to != local {
            return fail("This bank changes only its own continent's coin.");
        }
        if p.coins[from as usize] < self.amount {
            return fail("Not enough coins.");
        }
        let (received, _) = self.quote(ctx);
        if received <= 0 {
            return fail("The amount is too small to exchange.");
        }
        if ctx.world.get::<BankState>().international_reserve[to as usize] >= received {
            CommandResult::Ok
        } else {
            fail("The international banks hold too few of those coins now.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let (received, fee) = self.quote(ctx);
        let (from, to) = (self.from_continent as usize, self.to_continent as usize);
        let p = ctx.world.get_mut::<crate::agents::player::PlayerState>();
        p.coins[from] -= self.amount;
        p.coins[to] += received;
        let bank = ctx.world.get_mut::<BankState>();
        bank.international_reserve[from] += self.amount;
        bank.international_reserve[to] -= received;
        ctx.events.publish(CoinsExchanged {
            from_continent: self.from_continent,
            to_continent: self.to_continent,
            paid: self.amount,
            received,
            fee,
        });
    }
}

/// Borrows from the bank of the branch the player is at (C# `TakeLoanCommand`, DEC-037, DEC-046).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TakeLoan {
    pub amount: i64,
}

impl Command for TakeLoan {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.amount <= 0 {
            return fail("Amount must be positive.");
        }
        let nation = match branch(ctx) {
            Ok(n) => n,
            Err(e) => return fail(e),
        };
        if find_loan(ctx.world.get::<LoanTable>(), nation).is_some() {
            return fail("This bank's loan is not repaid yet.");
        }
        if ctx.world.get::<FinanceState>().event_of(nation)
            == crate::agents::finance::event_kind::CREDIT_CRUNCH
        {
            return fail("A credit crunch: the bank makes no new loans.");
        }
        if ctx.world.get::<BankState>().reserve[nation] < self.amount {
            return fail("The bank has no coins left to lend.");
        }
        if credit_limit(&ctx.world, refs(ctx), local_continent(ctx) as i32) >= self.amount {
            CommandResult::Ok
        } else {
            fail("Not enough collateral.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Ok(nation) = branch(ctx) else { return };
        let term = ctx.services.database.config.loan_term_weeks;
        let rate = Fx::from_raw(ctx.world.get::<FinanceState>().loan_rate[nation]);
        let balance = self.amount + interest(rate, self.amount, term);
        let weekly = (balance + i64::from(term) - 1) / i64::from(term);
        ctx.world.get_mut::<BankState>().reserve[nation] -= self.amount;
        change_local_coins(ctx, self.amount);
        let loans = ctx.world.get_mut::<LoanTable>();
        loans.nation.push(nation as i32);
        loans.balance.push(balance);
        loans.weekly_payment.push(weekly);
        loans.due.push(0);
        loans.overdue_weeks.push(0);
        let nation_node = nation_node(ctx, nation);
        ctx.events.publish(LoanTaken {
            nation_node,
            amount: self.amount,
            balance,
            weekly_payment: weekly,
        });
    }
}

/// Pays off part or all of the loan of the branch's bank (C# `RepayLoanCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RepayLoan {
    pub amount: i64,
}

impl Command for RepayLoan {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.amount <= 0 {
            return fail("Amount must be positive.");
        }
        let nation = match branch(ctx) {
            Ok(n) => n,
            Err(e) => return fail(e),
        };
        if find_loan(ctx.world.get::<LoanTable>(), nation).is_none() {
            return fail("No loan with this bank.");
        }
        if local_coins(ctx) >= self.amount {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Ok(nation) = branch(ctx) else { return };
        let Some(r) = find_loan(ctx.world.get::<LoanTable>(), nation) else {
            return;
        };
        let paid = self.amount.min(ctx.world.get::<LoanTable>().balance[r]);
        change_local_coins(ctx, -paid);
        let loans = ctx.world.get_mut::<LoanTable>();
        loans.balance[r] -= paid;
        loans.due[r] = (loans.due[r] - paid).max(0);
        if loans.due[r] == 0 {
            loans.overdue_weeks[r] = 0;
        }
        let left = loans.balance[r];
        ctx.world.get_mut::<BankState>().reserve[nation] += paid;
        if left > 0 {
            return;
        }
        ctx.world.get_mut::<LoanTable>().remove_row_swap_back(r);
        let nation_node = nation_node(ctx, nation);
        let turn = ctx.turn();
        ctx.events.publish(LoanRepaid { nation_node, turn });
    }
}

/// Bids for a seized facility at auction (C# `BidCommand`, DEC-039).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Bid {
    pub facility: i32,
    pub amount: i64,
}

impl Command for Bid {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let auctions = ctx.world.get::<AuctionTable>();
        let Some(a) = find_auction(auctions, self.facility) else {
            return fail("That facility is not up for auction.");
        };
        if !is_at(
            ctx,
            ctx.world.get::<FacilityTable>().node[self.facility as usize],
        ) {
            return fail("The player is not at the facility.");
        }
        if self.amount < auctions.reserve_price[a] {
            return fail("The bid is below the reserve price.");
        }
        if self.amount > auctions.player_bid[a] {
            CommandResult::Ok
        } else {
            fail("The bid must beat the current one.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let auctions = ctx.world.get_mut::<AuctionTable>();
        if let Some(a) = find_auction(auctions, self.facility) {
            auctions.player_bid[a] = self.amount;
        }
    }
}
