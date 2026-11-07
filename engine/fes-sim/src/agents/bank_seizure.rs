//! What the bank takes for an overdue loan of the player, and its margin sales (C#
//! `BankSystem.Seize`, `SellAssets`, `Settle` and the seizure helpers; DEC-037 to DEC-039, DEC-130).
use super::asset_prices::{
    facility_value, sell_land_for_player, sell_shares_for_player, sell_venture_shares_for_player,
    share_price, venture_price, PLAYER_HOLDER,
};
use super::balance_sheet::{margin_shortfall, nation_continent};
use super::bank_system::{Bank, CollateralKind, CollateralSeized};
use super::banks::{AuctionTable, BankState};
use super::cargo::{guild_hall_of, warehouse_holder, CargoTable, PLAYER_CART};
use super::facilities::{FacilityTable, MODE_AUTOMATED, OWNER_BANK, OWNER_PLAYER};
use super::groups::GroupTable;
use super::land::LandState;
use super::money_ledger::across_continents;
use super::player::PlayerState;
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use super::securities::{ShareTable, VentureTable};
use super::ship::PlayerShip;
use crate::economy::MarketStore;
use fes_core::Fx;

impl PurseState {
    /// C# `ChangeFloat`.
    pub fn change_float(&mut self, node: i32, amount: i64) {
        let purse = self.node_float[node as usize] as usize;
        self.float[purse] += amount;
    }

    /// C# `Sellable`: how many of `quantity` units at `price` the float of `node` can pay for.
    pub fn sellable(&self, node: i32, quantity: i32, price: i64) -> i32 {
        if price <= 0 {
            return quantity;
        }
        let float = self.float[self.node_float[node as usize] as usize];
        i64::from(quantity).min(float.max(0) / price) as i32
    }
}

/// C# `SimulationConfig.TraderOffer(int)`: the spread is truncated to int.
pub fn trader_offer(spread: Fx, price: i32) -> i32 {
    price - spread.mul_int(i64::from(price)) as i32
}

impl Bank<'_> {
    /// Deposits, warehouse cargo, the cart at a lender's branch, the ship, shares and land, then
    /// facilities, which go to auction.
    pub(crate) fn seize(&mut self, l: usize) {
        self.seize_deposits(l);
        self.seize_stored_cargo(l);
        self.seize_cart(l);
        self.seize_ship(l);
        self.sell_assets(l, false);
        self.seize_facilities(l);
    }

    fn publish_seized(&mut self, l: usize, kind: CollateralKind, facility: i32, value: i64) {
        let nation_node = self.lender_node(l);
        self.events.publish(CollateralSeized {
            nation_node,
            kind,
            facility,
            value,
            turn: self.turn,
        });
    }

    /// Shares, venture shares, then land, to repay loan `l`: its whole balance after a default, or
    /// on a margin call what brings the debt back within the loan-to-value.
    pub(crate) fn sell_assets(&mut self, l: usize, margin: bool) {
        let continent = nation_continent(self.r, self.loans().nation[l]);
        self.sell_shares(l, margin, continent);
        self.sell_venture_shares(l, margin, continent);
        self.sell_land(l, margin, continent);
    }

    fn sell_shares(&mut self, l: usize, margin: bool, continent: i32) {
        let rows = self.world.get::<ShareTable>().count();
        for row in (0..rows).rev() {
            let shares = self.world.get::<ShareTable>();
            if row >= shares.count() || shares.holder[row] != PLAYER_HOLDER {
                continue;
            }
            let f = shares.facility[row] as usize;
            let need = self.need(l, margin, continent);
            if need <= 0 {
                return;
            }
            let node = self.world.get::<FacilityTable>().node[f];
            let from = self.r.nodes.continent[node as usize];
            let price = share_price(self.world, self.r, f);
            let each = self.convert(price, from, continent).max(1);
            let count = ((need + each - 1) / each).min(i64::from(i32::MAX)) as i32;
            let raised =
                sell_shares_for_player(self.world, self.offers, self.r, self.turn, f, count);
            if raised > 0 {
                self.settle(l, CollateralKind::Shares, f as i32, from, raised, margin);
            }
        }
    }

    fn sell_venture_shares(&mut self, l: usize, margin: bool, continent: i32) {
        for v in 0..self.world.get::<VentureTable>().count() {
            let need = self.need(l, margin, continent);
            if need <= 0 {
                return;
            }
            let ventures = self.world.get::<VentureTable>();
            if ventures.player_shares[v] <= 0 {
                continue;
            }
            let from = ventures.continent[v];
            let price = venture_price(self.world, &self.r.db.config, v);
            let each = self.convert(price, from, continent).max(1);
            let count = ((need + each - 1) / each).min(i64::from(i32::MAX)) as i32;
            let raised = sell_venture_shares_for_player(
                self.world,
                self.offers,
                self.r,
                self.turn,
                v,
                count,
            );
            if raised > 0 {
                self.settle(l, CollateralKind::Shares, -1, from, raised, margin);
            }
        }
    }

    fn sell_land(&mut self, l: usize, margin: bool, continent: i32) {
        for n in 0..self.world.get::<LandState>().player_plots.len() {
            self.sell_plots(l, margin, continent, n);
        }
    }

    /// One plot at a time: each sold lowers the price of the next.
    fn sell_plots(&mut self, l: usize, margin: bool, continent: i32, n: usize) {
        while self.world.get::<LandState>().player_plots[n] > 0
            && self.need(l, margin, continent) > 0
        {
            let raised = sell_land_for_player(self.world, self.r.nodes, &self.r.db.config, n, 1);
            if raised <= 0 {
                break;
            }
            let from = self.r.nodes.continent[n];
            self.settle(l, CollateralKind::Land, -1, from, raised, margin);
        }
    }

    /// What is still to raise for loan `l`. Each coin a margin sale raises lowers the debt by a
    /// coin and what may be borrowed by the loan-to-value, so the shortfall takes more than itself.
    fn need(&self, l: usize, margin: bool, continent: i32) -> i64 {
        if !margin {
            return self.owed(l);
        }
        let shortfall = margin_shortfall(self.world, self.r, continent);
        if shortfall <= 0 {
            return 0;
        }
        let keep = (Fx::ONE_RAW - self.r.db.config.loan_to_value.raw()).max(1);
        self.owed(l)
            .min((shortfall * Fx::ONE_RAW + keep - 1) / keep)
    }

    /// A forced sale's proceeds in `from` coins repay loan `l`; the rest goes to the bank after a
    /// default (DEC-039), or to the player's deposit with it on a margin call.
    fn settle(
        &mut self,
        l: usize,
        kind: CollateralKind,
        facility: i32,
        from: i32,
        amount: i64,
        margin: bool,
    ) {
        let nation = self.loans().nation[l];
        let continent = nation_continent(self.r, nation);
        let value = self.convert(amount, from, continent);
        let paid = value.min(self.owed(l));
        self.repay_across(l, from, amount, value, paid);
        if margin {
            self.world.get_mut::<BankState>().player_deposit[nation as usize] += value - paid;
        }
        self.publish_seized(l, kind, facility, value);
    }

    /// Moves `amount` `from` coins worth `value` lender coins to the lender, repaying `paid`.
    fn repay_across(&mut self, l: usize, from: i32, amount: i64, value: i64, paid: i64) {
        let nation = self.loans().nation[l];
        let continent = nation_continent(self.r, nation);
        let bank = self.world.get_mut::<BankState>();
        across_continents(bank, from, amount, continent, value);
        self.repay(l, paid);
        self.world.get_mut::<BankState>().reserve[nation as usize] += value - paid;
    }

    fn seize_deposits(&mut self, l: usize) {
        let continent = nation_continent(self.r, self.loans().nation[l]);
        let nations = self.world.get::<BankState>().player_deposit.len();
        for n in 0..nations {
            if self.owed(l) <= 0 {
                break;
            }
            let from = nation_continent(self.r, n as i32);
            let cover = self.to_cover(self.owed(l), from, continent);
            let take = self.world.get::<BankState>().payable_deposit(n).min(cover);
            if take <= 0 {
                continue;
            }
            // The deposit's coins sit in its bank's reserve, as far as it has not lent them.
            let bank = self.world.get_mut::<BankState>();
            bank.player_deposit[n] -= take;
            bank.reserve[n] -= take;
            let value = self.convert(take, from, continent);
            let paid = value.min(self.owed(l));
            self.repay_across(l, from, take, value, paid);
            self.publish_seized(l, CollateralKind::Deposit, -1, value);
        }
    }

    fn seize_stored_cargo(&mut self, l: usize) {
        for f in 0..self.world.get::<FacilityTable>().count() {
            if self.owed(l) <= 0 {
                break;
            }
            let facilities = self.world.get::<FacilityTable>();
            if facilities.owner[f] != OWNER_PLAYER {
                continue;
            }
            let node = facilities.node[f];
            let value = self.sell_cargo(l, warehouse_holder(f as i32), node);
            if value > 0 {
                self.publish_seized(l, CollateralKind::WarehouseCargo, f as i32, value);
            }
        }
        for holder in self.world.get::<CargoTable>().collect_holders() {
            if self.owed(l) <= 0 {
                break;
            }
            let Some(guild) = guild_hall_of(holder) else {
                continue;
            };
            let city = self.world.get::<GroupTable>().node[guild as usize];
            let value = self.sell_cargo(l, holder, city);
            if value > 0 {
                self.publish_seized(l, CollateralKind::WarehouseCargo, -1, value);
            }
        }
    }

    /// A moving cart is taken when the player is next at one of the lender's branches.
    fn seize_cart(&mut self, l: usize) {
        let player = self.world.get::<PlayerState>();
        let node = player.node;
        if self.owed(l) <= 0
            || player.is_travelling()
            || self.r.banks.branch_nation(self.r.nodes, node) != self.loans().nation[l]
        {
            return;
        }
        let value = self.sell_cargo(l, PLAYER_CART, node);
        if value > 0 {
            self.publish_seized(l, CollateralKind::CartCargo, -1, value);
        }
    }

    /// A ship is sold to the lending nation's traders at the auction share of its price ([Draft]).
    fn seize_ship(&mut self, l: usize) {
        if self.owed(l) <= 0 || !self.world.get::<PlayerShip>().owned {
            return;
        }
        let config = &self.r.db.config;
        let nation = self.loans().nation[l];
        let bought = self.world.get::<PlayerShip>().bought_continent;
        let price = config.auction_reserve_share.mul_int(config.ship_price);
        let value = self.convert(price, bought, nation_continent(self.r, nation));
        self.world.get_mut::<PlayerShip>().owned = false;
        self.world.get_mut::<PurseState>().float[nation as usize] -= value;
        let paid = value.min(self.owed(l));
        self.repay(l, paid);
        self.world.get_mut::<BankState>().reserve[nation as usize] += value - paid;
        self.publish_seized(l, CollateralKind::Ship, -1, value);
    }

    fn seize_facilities(&mut self, l: usize) {
        let config = &self.r.db.config;
        let nation = self.loans().nation[l];
        let continent = nation_continent(self.r, nation);
        for f in 0..self.world.get::<FacilityTable>().count() {
            if self.owed(l) - self.pending_auctions(nation, continent) <= 0 {
                break;
            }
            if self.world.get::<FacilityTable>().owner[f] != OWNER_PLAYER {
                continue;
            }
            let facilities = self.world.get_mut::<FacilityTable>();
            let reserve = config
                .auction_reserve_share
                .mul_int(facility_value(self.r.db, facilities, f));
            facilities.owner[f] = OWNER_BANK;
            facilities.mode[f] = MODE_AUTOMATED;
            let from = self.r.nodes.continent[facilities.node[f] as usize];
            let auctions = self.world.get_mut::<AuctionTable>();
            auctions.facility.push(f as i32);
            auctions.nation.push(nation);
            auctions.closes_turn.push(self.turn + config.auction_weeks);
            auctions.reserve_price.push(reserve);
            auctions.player_bid.push(0);
            auctions.for_player_loan.push(1);
            let value = self.convert(reserve, from, continent);
            self.publish_seized(l, CollateralKind::Facility, f as i32, value);
        }
    }

    fn pending_auctions(&self, nation: i32, continent: i32) -> i64 {
        let auctions = self.world.get::<AuctionTable>();
        let facilities = self.world.get::<FacilityTable>();
        (0..auctions.count())
            .filter(|&a| auctions.nation[a] == nation && auctions.for_player_loan[a] != 0)
            .map(|a| {
                let node = facilities.node[auctions.facility[a] as usize];
                let from = self.r.nodes.continent[node as usize];
                self.convert(auctions.reserve_price[a], from, continent)
            })
            .sum()
    }

    /// An amount of `from` coins worth at least `owed` lender coins (at least one coin); rounding
    /// makes the plain conversion fall short by a little, so step up until it covers.
    fn to_cover(&self, owed: i64, from: i32, to: i32) -> i64 {
        let mut amount = self.convert(owed, to, from).max(1);
        while self.convert(amount, from, to) < owed {
            amount += (amount / 1000).max(1);
        }
        amount
    }

    /// Sells a holder's goods at the market nearest to `node` until the loan is covered.
    fn sell_cargo(&mut self, l: usize, holder: i32, node: i32) -> i64 {
        let continent = nation_continent(self.r, self.loans().nation[l]);
        let here = self.r.nodes.continent[node as usize];
        let spread = self.r.db.config.trader_spread;
        let mut total = 0;
        for (good, quantity) in self.world.get::<CargoTable>().collect(holder) {
            if self.owed(l) <= 0 {
                break;
            }
            let markets = self.world.get::<MarketStore>();
            let Some((_, cell)) = find_nearest_market(self.r.nodes, markets, node, good) else {
                continue;
            };
            let price = i64::from(trader_offer(spread, markets.price[cell]));
            if price <= 0 {
                continue;
            }
            let needed = self.to_cover(self.owed(l), here, continent);
            let wanted = i64::from(quantity).min((needed + price - 1) / price) as i32;
            let units = self.world.get::<PurseState>().sellable(node, wanted, price);
            if units <= 0 {
                continue;
            }
            let worth = i64::from(units) * price;
            self.world
                .get_mut::<CargoTable>()
                .change(holder, good, -units);
            let supply = &mut self.world.get_mut::<MarketStore>().supply[cell];
            *supply = supply.wrapping_add(units);
            self.world
                .get_mut::<PurseState>()
                .change_float(node, -worth);
            let value = self.convert(worth, here, continent);
            // Proceeds beyond the debt go to the bank, not the former owner (DEC-039).
            let paid = value.min(self.owed(l));
            self.repay_across(l, here, worth, value, paid);
            total += value;
        }
        total
    }
}
