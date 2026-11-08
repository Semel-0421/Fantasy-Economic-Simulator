//! `agents.banks` (C# `BankSystem`, `Agents/BankSystem.cs`): hands newly minted coins to the
//! nations' banks (DEC-042), balances the international banks (DEC-096), credits deposit interest,
//! services the player's loans and NPC loans, calls for margin (DEC-130), lends to NPCs short of cash
//! and closes auctions. Seizure lives in `bank_seizure`, NPC lending in `bank_npc`.
use super::asset_prices::Refs;
use super::balance_sheet::{collateral_value, convert, debt, margin_shortfall, nation_continent};
use super::banks::{AuctionTable, BankState, LoanTable, MarginState};
use super::credit::BondOffers;
use super::facilities::{FacilityTable, MODE_AUTOMATED, OWNER_NPC, OWNER_PLAYER};
use super::groups::GroupTable;
use super::money_ledger::{across_continents, family_at, fund_new_operator, pay_from_group};
use super::orders::MerchantTripStats;
use super::player::PlayerState;
use super::production::bankruptcy::restart_facility;
use super::IncomeTaxState;
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::CurrencyState;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::Fx;
use std::sync::Arc;

/// C# `FinanceOrder.Banks`.
pub const ORDER: i32 = sim_order::LATE;
/// C# `ControlMode.Manual`.
const MODE_MANUAL: u8 = 0;

/// The player's loan with a bank was paid off (C# `LoanRepaid`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct LoanRepaid {
    pub nation_node: i32,
    pub turn: i32,
}

/// DEC-130: the debt rose above the call share of the collateral (C# `MarginCalled`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct MarginCalled {
    pub nation_node: i32,
    /// What is over the loan-to-value, in the lending bank's coin.
    pub shortfall: i64,
    pub sale_turn: i32,
}

/// C# `CollateralKind`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
#[repr(u8)]
pub enum CollateralKind {
    Deposit = 0,
    WarehouseCargo = 1,
    CartCargo = 2,
    Facility = 3,
    Ship = 4,
    Shares = 5,
    Land = 6,
}

/// Collateral the bank took for an overdue loan (C# `CollateralSeized`); `value` is in the lending
/// bank's coin, `facility` is -1 when none.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct CollateralSeized {
    pub nation_node: i32,
    pub kind: CollateralKind,
    pub facility: i32,
    pub value: i64,
    pub turn: i32,
}

/// C# `AuctionClosed`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct AuctionClosed {
    pub facility: i32,
    pub won_by_player: bool,
    pub price: i64,
    pub turn: i32,
}

impl BankState {
    /// C# `PayableDeposit`: the deposit as far as the bank holds its coins.
    pub fn payable_deposit(&self, nation: usize) -> i64 {
        self.player_deposit[nation].min(self.reserve[nation].max(0))
    }
}

/// `agents.banks`.
#[derive(Default)]
pub struct BankSystem;

impl System for BankSystem {
    fn id(&self) -> &'static str {
        "agents.banks"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = Arc::clone(&ctx.services.database);
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let spent = {
            let r = Refs {
                db: &db,
                nodes: ctx.services.get::<NodeTable>(),
                banks: ctx.services.get::<NodeBanks>(),
            };
            let mut bank = Bank {
                world: &mut ctx.world,
                events: &mut ctx.events,
                offers: &mut offers,
                r,
                turn,
                spent_loans: 0,
            };
            bank.run();
            bank.spent_loans
        };
        *ctx.services.get_mut::<BondOffers>() = offers;
        ctx.services.get_mut::<MerchantTripStats>().spent_loans += spent;
        Ok(())
    }
}

/// One run of the bank system over the world.
pub struct Bank<'a> {
    pub world: &'a mut WorldState,
    pub events: &'a mut EventBus,
    pub offers: &'a mut BondOffers,
    pub r: Refs<'a>,
    pub turn: i32,
    /// C# `MerchantTripStats.SpentLoans`, added when the run ends.
    pub spent_loans: i64,
}

impl Bank<'_> {
    fn run(&mut self) {
        self.distribute_minted();
        if self.r.db.config.international_balance {
            self.balance_international();
        }
        // Weekly share of the yearly rate; small deposits round down to no interest.
        let rate = self.r.db.config.deposit_interest_per_year;
        for deposit in &mut self.world.get_mut::<BankState>().player_deposit {
            *deposit += rate.mul_int(*deposit) / i64::from(WEEKS_PER_YEAR);
        }
        for l in (0..self.loans().count()).rev() {
            self.service(l);
        }
        self.check_margin();
        for l in (0..self.world.get::<super::NpcLoanTable>().count()).rev() {
            self.service_npc(l);
        }
        self.lend_to_npcs();
        for a in (0..self.world.get::<AuctionTable>().count()).rev() {
            if self.turn >= self.world.get::<AuctionTable>().closes_turn[a] {
                self.close(a);
            }
        }
    }

    fn distribute_minted(&mut self) {
        let (bank, currency) = self.world.get2_mut::<BankState, CurrencyState>();
        for c in 0..currency.continent_count() {
            let minted = currency.coin_supply[c] - bank.distributed_supply[c];
            if minted <= 0 {
                continue;
            }
            bank.distribute(self.r.banks, self.r.nodes, c as i32, minted);
            bank.distributed_supply[c] = currency.coin_supply[c];
        }
    }

    /// A continent's nations refill its international bank to its share of the coin supply and
    /// take back what it holds above twice that (DEC-096).
    fn balance_international(&mut self) {
        let share = self.r.db.config.international_bank_reserve_share;
        let (bank, currency) = self.world.get2_mut::<BankState, CurrencyState>();
        for c in 0..currency.continent_count() {
            let target = share.mul_int(currency.coin_supply[c]);
            let held = bank.international_reserve[c];
            if held > 2 * target {
                bank.international_reserve[c] = 2 * target;
                bank.distribute(self.r.banks, self.r.nodes, c as i32, held - 2 * target);
                continue;
            }
            let need = target - held;
            refill(self.r, bank, c, need);
        }
    }

    pub(crate) fn loans(&self) -> &LoanTable {
        self.world.get::<LoanTable>()
    }

    pub(crate) fn owed(&self, l: usize) -> i64 {
        self.loans().balance[l]
    }

    pub(crate) fn lender_node(&self, l: usize) -> i32 {
        self.r
            .banks
            .nation_node(self.r.nodes, self.loans().nation[l])
    }

    pub(crate) fn convert(&self, amount: i64, from: i32, to: i32) -> i64 {
        convert(self.world, amount, from, to)
    }

    /// Instalments come out of the deposit with the lending bank first, then the coins carried.
    fn service(&mut self, l: usize) {
        let nation = self.loans().nation[l];
        let n = nation as usize;
        let continent = nation_continent(self.r, nation) as usize;
        let (loans, bank) = self.world.get2_mut::<LoanTable, BankState>();
        loans.due[l] = loans.balance[l].min(loans.due[l] + loans.weekly_payment[l]);
        let from_deposit = loans.due[l].min(bank.player_deposit[n]);
        bank.player_deposit[n] -= from_deposit;
        bank.reserve[n] -= from_deposit; // repay puts it back: the deposit already sat in the reserve
        let still = loans.due[l] - from_deposit;
        let coins = &mut self.world.get_mut::<PlayerState>().coins[continent];
        let from_coins = still.min((*coins).max(0));
        *coins -= from_coins;
        self.repay(l, from_deposit + from_coins);

        if self.owed(l) == 0 {
            self.world.get_mut::<LoanTable>().remove_row_swap_back(l);
            self.publish_repaid(nation);
            return;
        }
        let loans = self.world.get_mut::<LoanTable>();
        loans.overdue_weeks[l] = if loans.due[l] > 0 {
            loans.overdue_weeks[l] + 1
        } else {
            0
        };
        if loans.overdue_weeks[l] > self.r.db.config.loan_grace_weeks {
            self.seize(l);
        }
    }

    pub(crate) fn repay(&mut self, l: usize, amount: i64) {
        let (loans, bank) = self.world.get2_mut::<LoanTable, BankState>();
        loans.balance[l] -= amount;
        loans.due[l] = (loans.due[l] - amount).max(0);
        bank.reserve[loans.nation[l] as usize] += amount;
    }

    fn publish_repaid(&mut self, nation: i32) {
        let nation_node = self.r.banks.nation_node(self.r.nodes, nation);
        self.events.publish(LoanRepaid {
            nation_node,
            turn: self.turn,
        });
    }

    /// DEC-130: over the call share the player has some weeks; then shares and land are sold, week
    /// after week, until the debt is within the loan-to-value.
    fn check_margin(&mut self) {
        let config = &self.r.db.config;
        let count = self.loans().count();
        let continent = if count > 0 {
            nation_continent(self.r, self.loans().nation[0])
        } else {
            0
        };
        if count == 0
            || debt(self.world, self.r, continent)
                <= config
                    .margin_call_share
                    .mul_int(collateral_value(self.world, self.r, continent))
        {
            self.world.get_mut::<MarginState>().called_turn = -1;
            return;
        }
        let called = self.world.get::<MarginState>().called_turn;
        if called < 0 {
            self.world.get_mut::<MarginState>().called_turn = self.turn;
            let event = MarginCalled {
                nation_node: self.lender_node(0),
                shortfall: margin_shortfall(self.world, self.r, continent),
                sale_turn: self.turn + config.margin_call_weeks,
            };
            self.events.publish(event);
            return;
        }
        if self.turn - called < config.margin_call_weeks {
            return;
        }
        let mut l = 0;
        while l < self.loans().count() {
            self.sell_assets(l, true);
            l += 1;
        }
        for l in (0..self.loans().count()).rev() {
            if self.owed(l) > 0 {
                continue;
            }
            let nation = self.loans().nation[l];
            self.world.get_mut::<LoanTable>().remove_row_swap_back(l);
            self.publish_repaid(nation);
        }
    }

    /// Without a winning bid the facility goes to a new NPC operator at the reserve price (DEC-039).
    fn close(&mut self, a: usize) {
        let auctions = self.world.get::<AuctionTable>();
        let (f, nation) = (auctions.facility[a] as usize, auctions.nation[a]);
        let for_player_loan = auctions.for_player_loan[a] != 0;
        let (bid, reserve) = (auctions.player_bid[a], auctions.reserve_price[a]);
        let node = self.world.get::<FacilityTable>().node[f];
        let facility_continent = self.r.nodes.continent[node as usize];
        let won = bid >= reserve && bid > 0 && self.pay(facility_continent, bid);
        let price = if won { bid } else { reserve };
        self.world.get_mut::<AuctionTable>().remove_row_swap_back(a);
        self.hand_over(f, node, won, for_player_loan, price);

        let continent = nation_continent(self.r, nation);
        let value = self.convert(price, facility_continent, continent);
        across_continents(
            self.world.get_mut::<BankState>(),
            facility_continent,
            price,
            continent,
            value,
        );
        let l = if for_player_loan {
            self.loans().nation.iter().position(|&n| n == nation)
        } else {
            None
        };
        let paid = l.map_or(0, |l| value.min(self.owed(l)));
        if paid > 0 {
            self.repay(l.expect("a loan"), paid);
        }
        self.world.get_mut::<BankState>().reserve[nation as usize] += value - paid;
        if let Some(l) = l.filter(|&l| self.owed(l) == 0) {
            self.world.get_mut::<LoanTable>().remove_row_swap_back(l);
            self.publish_repaid(nation);
        }
        self.events.publish(AuctionClosed {
            facility: f as i32,
            won_by_player: won,
            price,
            turn: self.turn,
        });
    }

    fn hand_over(&mut self, f: usize, node: i32, won: bool, for_player_loan: bool, price: i64) {
        let config = &self.r.db.config;
        let group = if won {
            let groups = self.world.get::<GroupTable>();
            (0..groups.count())
                .position(|g| groups.is_player[g] != 0)
                .map_or(-1, |g| g as i32)
        } else {
            // [Draft] Without a winning bid a local family buys it.
            family_at(self.world, self.r.nodes, config, node)
        };
        let facilities = self.world.get_mut::<FacilityTable>();
        facilities.owner[f] = if won { OWNER_PLAYER } else { OWNER_NPC };
        facilities.mode[f] = if won { MODE_MANUAL } else { MODE_AUTOMATED };
        facilities.repurposable[f] = 1; // taken over by others (DEC-125)
        facilities.group[f] = group;
        // What the player left in the warehouse stays theirs for a while (DEC-052).
        if won {
            facilities.warehouse_claim_until[f] = -1;
        } else if for_player_loan {
            facilities.warehouse_claim_until[f] = self.turn + config.lost_warehouse_weeks;
        }
        if won {
            return;
        }
        // The buyer pays the price, and its new manager's starting cash, from its cash (DEC-061).
        let old_cash = facilities.cash[f];
        let tier = self.r.db.facilities[facilities.def_index[f] as usize].tier as usize;
        pay_from_group(self.world, self.r.nodes, group, node, price);
        let start = config.facility_start_cash[tier];
        let cash = fund_new_operator(self.world, self.r.nodes, group, node, old_cash, start);
        self.world.get_mut::<FacilityTable>().cash[f] = cash;
        restart_facility(self.world.get_mut::<IncomeTaxState>(), f, cash);
        self.world.get_mut::<FacilityTable>().negative_cash_weeks[f] = 0;
    }

    /// A winning bid is paid from the coins of the facility's continent, then deposits there.
    fn pay(&mut self, continent: i32, amount: i64) -> bool {
        let nations = self.world.get::<BankState>().player_deposit.len();
        let mut deposits = 0;
        for n in 0..nations {
            if nation_continent(self.r, n as i32) == continent {
                deposits += self.world.get::<BankState>().payable_deposit(n);
            }
        }
        let coins = &mut self.world.get_mut::<PlayerState>().coins[continent as usize];
        if *coins + deposits < amount {
            return false;
        }
        let from_coins = amount.min((*coins).max(0));
        *coins -= from_coins;
        let mut rest = amount - from_coins;
        for n in 0..nations {
            if rest <= 0 {
                break;
            }
            if nation_continent(self.r, n as i32) != continent {
                continue;
            }
            let bank = self.world.get_mut::<BankState>();
            let take = rest.min(bank.payable_deposit(n));
            bank.player_deposit[n] -= take;
            bank.reserve[n] -= take;
            rest -= take;
        }
        true
    }
}

/// The nations of continent `c`, in order, move up to `need` of their spendable reserve to its
/// international bank.
fn refill(r: Refs, bank: &mut BankState, c: usize, mut need: i64) {
    for n in 0..r.banks.nation_count(r.nodes) {
        if need <= 0 {
            break;
        }
        if nation_continent(r, n) != c as i32 {
            continue;
        }
        let take = need.min(bank.spendable(n as usize));
        bank.reserve[n as usize] -= take;
        bank.international_reserve[c] += take;
        need -= take;
    }
}

/// C# `FinanceRules.Interest`: a yearly rate over a term of weeks.
pub fn interest(yearly_rate: Fx, amount: i64, term_weeks: i32) -> i64 {
    yearly_rate.mul_int(amount) * i64::from(term_weeks) / i64::from(WEEKS_PER_YEAR)
}
