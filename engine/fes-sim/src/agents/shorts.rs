//! `agents.shorts` (C# `ShortSystem`, `ShortRules`, `ShortBuyBack`, `Agents/Shorts.cs`, DEC-133):
//! each week the player pays the lenders a fee on their short sales from the deposit at the
//! exchange's bank; under the call share the bank calls for margin, and after the call's weeks it
//! buys them all back, from the deposit, then the coins of its continent, and lends the rest.
use super::asset_prices::{households_holder, share_price, venture_price, Refs};
use super::money_ledger::pay_nation_households;
use super::securities::{AssetMarkets, ShareTable, ShortLicences, ShortTable, VentureTable};
use super::{BankState, FacilityTable, GroupTable, LoanTable, PlayerState};
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use std::sync::Arc;

/// C# `FinanceOrder.Shorts`.
pub const ORDER: i32 = sim_order::LATE + 11;

/// The bank called for margin on the player's short sales (C# `ShortCalled`); `nation` is its node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ShortCalled {
    pub nation: i32,
    /// What the deposit lacks to the call line, in the bank's coin.
    pub shortfall: i64,
    pub cover_turn: i32,
}

/// The bank bought back the player's short sales after a call (C# `ShortsCovered`); `lent` of the
/// cost became a loan.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ShortsCovered {
    pub nation: i32,
    pub cost: i64,
    pub lent: i64,
    pub turn: i32,
}

impl ShortTable {
    /// C# `Find`.
    pub fn find(&self, venture: bool, asset: i32) -> Option<usize> {
        (0..self.count()).find(|&r| self.asset[r] == asset && (self.venture[r] != 0) == venture)
    }

    /// C# `Held`.
    pub fn held(&self, venture: bool, asset: i32) -> i32 {
        self.find(venture, asset).map_or(0, |r| self.shares[r])
    }

    /// C# `Add`: a row reaching zero or less is removed by swap-back.
    pub fn add(&mut self, venture: bool, asset: i32, bank: i32, count: i32) {
        if let Some(r) = self.find(venture, asset) {
            self.shares[r] += count;
            if self.shares[r] <= 0 {
                self.remove_row_swap_back(r);
            }
            return;
        }
        if count <= 0 {
            return;
        }
        self.asset.push(asset);
        self.venture.push(u8::from(venture));
        self.shares.push(count);
        self.bank.push(bank);
    }
}

/// C# `ShortRules.Price`.
pub fn short_price(world: &WorldState, r: Refs, venture: bool, asset: i32) -> i64 {
    if venture {
        venture_price(world, &r.db.config, asset as usize)
    } else {
        share_price(world, r, asset as usize)
    }
}

/// C# `ShortRules.Bank`: the nation whose bank holds a short's margin.
pub fn short_bank(world: &WorldState, r: Refs, venture: bool, asset: i32) -> i32 {
    if venture {
        let continent = world.get::<VentureTable>().continent[asset as usize];
        world
            .get::<AssetMarkets>()
            .venture_lender(r.nodes, r.banks, continent)
    } else {
        let node = world.get::<FacilityTable>().node[asset as usize];
        r.banks.nation_of(r.nodes, node)
    }
}

/// C# `ShortRules.Households`: the nation whose households buy and lend the asset.
pub fn short_households(world: &WorldState, r: Refs, venture: bool, asset: i32) -> i32 {
    let node = if venture {
        world.get::<VentureTable>().port[asset as usize]
    } else {
        world.get::<FacilityTable>().node[asset as usize]
    };
    r.banks.nation_of(r.nodes, node)
}

/// C# `ShortRules.Value`: what buying back all the short sales held at a bank would cost now.
pub fn short_value(world: &WorldState, r: Refs, bank: i32) -> i64 {
    let shorts = world.get::<ShortTable>();
    (0..shorts.count())
        .filter(|&s| shorts.bank[s] == bank)
        .map(|s| {
            let price = short_price(world, r, shorts.venture[s] != 0, shorts.asset[s]);
            i64::from(shorts.shares[s]) * price
        })
        .sum()
}

/// C# `ShortBuyBack.Run`: buys back shares sold short at the price, from the deposit at the bank
/// as far as it pays, and returns them. Returns what the deposit could not pay.
pub fn buy_back(
    world: &mut WorldState,
    r: Refs,
    venture: bool,
    asset: i32,
    count: i32,
    from_deposit: bool,
) -> i64 {
    let cost = i64::from(count) * short_price(world, r, venture, asset);
    let bank = short_bank(world, r, venture, asset);
    let households = short_households(world, r, venture, asset);
    let state = world.get_mut::<BankState>();
    let paid = if from_deposit {
        cost.min(state.payable_deposit(bank as usize))
    } else {
        0
    };
    state.player_deposit[bank as usize] -= paid;
    state.reserve[bank as usize] -= paid;
    pay_nation_households(world, households, cost); // what the deposit does not pay, the caller settles
    if !venture {
        let shares = world.get_mut::<ShareTable>();
        let holder = households_holder(households);
        let back = count.min(shares.held(asset, holder));
        shares.add(asset, holder, -back); // returned to the group
    }
    world
        .get_mut::<ShortTable>()
        .add(venture, asset, bank, -count);
    cost - paid
}

/// `agents.shorts`.
pub struct ShortSystem;

impl System for ShortSystem {
    fn id(&self) -> &'static str {
        "agents.shorts"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if ctx.world.get::<ShortTable>().count() == 0 {
            ctx.world.get_mut::<ShortLicences>().called_turn.fill(-1);
            return Ok(());
        }
        let db = Arc::clone(&ctx.services.database);
        let turn = ctx.turn();
        let mut desk = Desk {
            world: &mut ctx.world,
            events: &mut ctx.events,
            r: Refs {
                db: &db,
                nodes: ctx.services.get::<NodeTable>(),
                banks: ctx.services.get::<NodeBanks>(),
            },
            turn,
        };
        for row in 0..desk.world.get::<ShortTable>().count() {
            desk.pay_fee(row);
        }
        for bank in 0..desk.world.get::<ShortLicences>().called_turn.len() {
            desk.margin(bank);
        }
        Ok(())
    }
}

struct Desk<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    r: Refs<'a>,
    turn: i32,
}

impl Desk<'_> {
    fn pay_fee(&mut self, row: usize) {
        let shorts = self.world.get::<ShortTable>();
        let (venture, asset, bank) = (
            shorts.venture[row] != 0,
            shorts.asset[row],
            shorts.bank[row],
        );
        let worth = i64::from(shorts.shares[row]) * short_price(self.world, self.r, venture, asset);
        let fee = self.r.db.config.short_fee.mul_int(worth) / i64::from(WEEKS_PER_YEAR);
        let state = self.world.get_mut::<BankState>();
        let fee = fee.min(state.payable_deposit(bank as usize));
        if fee <= 0 {
            return;
        }
        state.player_deposit[bank as usize] -= fee;
        state.reserve[bank as usize] -= fee;
        if venture {
            let households = short_households(self.world, self.r, true, asset);
            pay_nation_households(self.world, households, fee);
            return;
        }
        let group = self.world.get::<FacilityTable>().group[asset as usize];
        if group >= 0 {
            self.world.get_mut::<GroupTable>().cash[group as usize] += fee;
        } else {
            pay_nation_households(self.world, bank, fee);
        }
    }

    fn margin(&mut self, bank: usize) {
        let config = &self.r.db.config;
        let value = short_value(self.world, self.r, bank as i32);
        let line = config.short_call_share.mul_int(value);
        let deposit = self.world.get::<BankState>().player_deposit[bank];
        let licences = self.world.get_mut::<ShortLicences>();
        if value <= 0 || deposit >= line {
            licences.called_turn[bank] = -1;
            return;
        }
        let nation = self.r.banks.nation_node(self.r.nodes, bank as i32);
        if licences.called_turn[bank] < 0 {
            licences.called_turn[bank] = self.turn;
            self.events.publish(ShortCalled {
                nation,
                shortfall: line - deposit,
                cover_turn: self.turn + config.margin_call_weeks,
            });
            return;
        }
        if self.turn - licences.called_turn[bank] < config.margin_call_weeks {
            return;
        }
        licences.called_turn[bank] = -1;
        let mut unpaid = 0;
        for row in (0..self.world.get::<ShortTable>().count()).rev() {
            let shorts = self.world.get::<ShortTable>();
            if shorts.bank[row] != bank as i32 {
                continue;
            }
            let (venture, asset, shares) = (
                shorts.venture[row] != 0,
                shorts.asset[row],
                shorts.shares[row],
            );
            unpaid += buy_back(self.world, self.r, venture, asset, shares, true);
        }
        self.settle(bank, value, unpaid);
    }

    /// What the deposit could not pay comes from the coins of the bank's continent, and the bank
    /// lends the rest.
    fn settle(&mut self, bank: usize, cost: i64, unpaid: i64) {
        let nation = self.r.banks.nation_node(self.r.nodes, bank as i32);
        let continent = self.r.nodes.continent[nation as usize] as usize;
        let player = self.world.get_mut::<PlayerState>();
        let from_coins = unpaid.min(player.coins[continent].max(0));
        player.coins[continent] -= from_coins;
        let lent = unpaid - from_coins;
        self.world.get_mut::<BankState>().reserve[bank] -= lent;
        if lent > 0 {
            let term = i64::from(self.r.db.config.loan_term_weeks);
            let loans = self.world.get_mut::<LoanTable>();
            match (0..loans.count()).find(|&l| loans.nation[l] == bank as i32) {
                Some(l) => loans.balance[l] += lent,
                None => {
                    loans.nation.push(bank as i32);
                    loans.balance.push(lent);
                    loans.weekly_payment.push((lent + term - 1) / term);
                    loans.due.push(0);
                    loans.overdue_weeks.push(0);
                }
            }
        }
        let turn = self.turn;
        self.events.publish(ShortsCovered {
            nation,
            cost,
            lent,
            turn,
        });
    }
}
