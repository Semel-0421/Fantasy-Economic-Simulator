//! Selling a load (C# `DetailedMerchantJob`: `SellOrCarry`, `ValueHere`, `SellHere`,
//! `SellToHouseholds`, `SellForCoins`, `SellForBills`, `ClaimCover`, `BuyCover`, `BookSale`,
//! `CountImport`; DEC-093, DEC-097, DEC-098, DEC-099, DEC-124).
use super::job::{u, DetailedJob, SOLD};
use crate::economy::MarketStore;
use fes_core::Fx;

impl DetailedJob<'_> {
    /// Sells here when that beats carrying on, or when the group would not last the trip.
    /// Returns the route to carry the load on, or -1.
    pub(super) fn sell_or_carry(&mut self, a: usize, node: i32) -> i32 {
        self.collect_rows(a);
        if self.x.rows.is_empty() {
            return -1;
        }
        let route = if self.r.weigh_sales {
            self.carry_on(a, node)
        } else {
            -1
        };
        if route < 0 {
            self.sell_here(a, node);
        }
        route
    }

    /// The route to carry the load on when it is worth more there and the group lasts the
    /// trip, or -1 (counting carried-on loads and forced sales).
    fn carry_on(&mut self, a: usize, node: i32) -> i32 {
        let (route, net, trip_cost) = self.best_carry(a, node);
        if route < 0 || net <= self.value_here(a, node) {
            return -1;
        }
        if self.lasts_the_trip(a, route, trip_cost) {
            self.x.trip_stats[6] += 1;
            return route;
        }
        self.x.trip_stats[7] += 1;
        -1
    }

    /// What the load brings here now.
    fn value_here(&self, a: usize, node: i32) -> i64 {
        let licensed = self.holds_licenses(a);
        let mut value = 0i64;
        for &row in &self.x.rows {
            let good = self.s.cargo_good[u(row)];
            if !licensed && self.needs_license(node, good) {
                continue;
            }
            let cell = self.find_cell(node, good);
            if cell >= 0 {
                value += i64::from(self.s.cargo_quantity[u(row)]) * self.offer_at(cell);
            }
        }
        self.earn(a, node, self.payable(a, node, value))
    }

    /// The bank makes up what a sale brings below the part of the guarantee on these units.
    fn claim_cover(
        &mut self,
        a: usize,
        purse: i32,
        units: i64,
        carried: i64,
        proceeds: i64,
        abroad: bool,
    ) {
        if self.s.hedge[a] <= 0 || carried <= 0 {
            return;
        }
        let share = self.s.hedge[a] * units / carried;
        self.s.hedge[a] -= share;
        if abroad || proceeds >= share {
            return;
        }
        let paid = (share - proceeds).min(self.x.hedge_room[u(purse)]);
        if paid <= 0 {
            return;
        }
        self.x.hedge_room[u(purse)] -= paid;
        self.x.hedge_net[u(purse)] -= paid;
        self.s.cash[a] += paid;
        self.x.trip_stats[19] += paid;
    }

    /// A group covers a load when the fall it fears, weighed by its loss aversion, is worth more
    /// than the fee (DEC-099).
    pub(super) fn buy_cover(&mut self, a: usize, destination: i32, value: i64, chance_raw: i64) {
        if self.r.hedge_fee_raw <= 0 || self.s.hedge[a] > 0 || self.abroad(a, destination) {
            return;
        }
        let purse = u(self.p.node_float[u(destination)]);
        if self.x.hedge_room[purse] <= 0 {
            return;
        }
        let offer = value - Fx::mul_int_raw(value, self.r.spread_raw);
        let fee = Fx::mul_int_raw(offer, self.r.hedge_fee_raw);
        if fee <= 0 || self.s.cash[a] < fee {
            return;
        }
        let floor = Fx::mul_int_raw(offer, self.r.hedge_floor_raw);
        let would_lose =
            offer - Fx::mul_int_raw(offer, self.r.hedge_fall_raw) < self.s.load_cost[a];
        let aversion = i64::from(self.g.traits.loss_aversion[usize::from(self.g.trait_id[a])]);
        let fear =
            Fx::mul_int_raw(offer - floor, chance_raw) * if would_lose { aversion } else { 1 };
        if fear <= fee {
            return;
        }
        self.s.cash[a] -= fee;
        self.x.hedge_net[purse] += fee;
        self.s.hedge[a] = floor;
        self.x.trip_stats[18] += fee;
    }

    fn book_sale(&mut self, a: usize, units: i64, proceeds: i64, carried: &mut i64) {
        let cost = self.s.load_cost[a] * units / *carried;
        self.s.load_cost[a] -= cost;
        *carried -= units;
        if proceeds >= cost {
            return;
        }
        self.x.trip_stats[4] += 1;
        self.x.trip_stats[5] += cost - proceeds;
    }

    /// Whether the group gets there before its weeks below zero end it.
    pub(super) fn lasts_the_trip(&self, a: usize, route: i32, trip_cost: i64) -> bool {
        self.s.cash[a] >= trip_cost
            || self.s.negative_weeks[a] + self.p.route_turns[u(route)] < self.r.bankrupt_weeks
    }

    /// Sells at what the traders here offer, as far as they can pay; unsellable goods stay on board.
    fn sell_here(&mut self, a: usize, node: i32) {
        self.collect_rows(a);
        let mut carried: i64 = self
            .x
            .rows
            .iter()
            .map(|&r| i64::from(self.s.cargo_quantity[u(r)]))
            .sum();
        for i in 0..self.x.rows.len() {
            let row = self.x.rows[i];
            let good = self.s.cargo_good[u(row)];
            if self.needs_license(node, good) && !self.holds_licenses(a) {
                continue;
            }
            let cell = self.find_cell(node, good);
            if cell < 0 {
                continue;
            }
            self.sell_to_households(a, node, row, cell, &mut carried);
            let price = self.offer_at(cell);
            self.sell_for_coins(a, node, row, cell, price, &mut carried);
            self.sell_for_bills(a, node, row, cell, price, &mut carried);
        }
    }

    /// DEC-124: households take the necessities they wish for first, at the price, as far as
    /// their cash goes; only at home on the group's continent.
    fn sell_to_households(&mut self, a: usize, node: i32, row: i32, cell: i32, carried: &mut i64) {
        let price = i64::from(self.p.price[u(cell)]);
        let need = self.x.household_need[u(cell)];
        if need <= 0 || price <= 0 || self.abroad(a, node) {
            return;
        }
        let quantity = i64::from(self.s.cargo_quantity[u(row)]);
        let units = quantity
            .min(need)
            .min(self.s.household_cash[u(node)].max(0) / price);
        if units <= 0 {
            return;
        }
        let paid = units * price;
        self.s.household_cash[u(node)] -= paid;
        self.s.cash[a] += paid;
        self.x.household_need[u(cell)] -= units;
        let grown = i64::from(self.s.home_grown[u(cell)]) + units;
        self.s.home_grown[u(cell)] = MarketStore::clamp_to_non_negative_int(grown);
        self.s.cargo_quantity[u(row)] -= units as i32;
        self.count_import(a, node, paid);
        self.x.trip_stats[SOLD] += paid;
        let purse = self.p.node_float[u(node)];
        self.claim_cover(a, purse, units, *carried, paid, false);
        self.book_sale(a, units, paid, carried);
    }

    fn sell_for_coins(
        &mut self,
        a: usize,
        node: i32,
        row: i32,
        cell: i32,
        price: i64,
        carried: &mut i64,
    ) {
        let purse = self.p.node_float[u(node)];
        let mut units = i64::from(self.s.cargo_quantity[u(row)]);
        if price > 0 {
            let held = self.s.float[u(purse)];
            let payable = if held > 0 { held / price } else { 0 };
            units = units.min(payable);
        }
        let abroad = self.abroad(a, node);
        let home = self.p.continent[u(self.g.home[a])];
        let mut proceeds = self.earn(a, node, units * price);
        // Foreign coins change only as far as the home bank holds its own (DEC-031).
        let bank = self.s.international[u(home)];
        if abroad && proceeds > bank {
            units = if bank > 0 && proceeds > 0 {
                units * bank / proceeds
            } else {
                0
            };
            proceeds = self.earn(a, node, units * price);
        }
        if units <= 0 {
            return;
        }
        let supply = i64::from(self.s.supply[u(cell)]) + units;
        self.s.supply[u(cell)] = MarketStore::clamp_to_non_negative_int(supply);
        self.s.cash[a] += proceeds;
        self.s.float[u(purse)] -= units * price;
        self.count_import(a, node, units * price);
        if abroad {
            self.s.international[u(self.p.continent[u(node)])] += units * price;
            self.s.international[u(home)] -= proceeds;
        }
        self.s.cargo_quantity[u(row)] -= units as i32;
        self.x.trip_stats[SOLD] += proceeds;
        self.claim_cover(a, purse, units, *carried, proceeds, abroad);
        self.book_sale(a, units, proceeds, carried);
    }

    fn sell_for_bills(
        &mut self,
        a: usize,
        node: i32,
        row: i32,
        cell: i32,
        price: i64,
        carried: &mut i64,
    ) {
        let purse = self.p.node_float[u(node)];
        let quantity = i64::from(self.s.cargo_quantity[u(row)]);
        if self.abroad(a, node) || self.no_bills(a) || price <= 0 || quantity <= 0 {
            return;
        }
        let billed = quantity.min(self.s.bill_room[u(purse)] / price);
        if billed <= 0 {
            return;
        }
        let face = billed * price;
        let received = self.sell_for_bill(a, purse, face);
        let supply = i64::from(self.s.supply[u(cell)]) + billed;
        self.s.supply[u(cell)] = MarketStore::clamp_to_non_negative_int(supply);
        self.s.cash[a] += received;
        self.s.cargo_quantity[u(row)] -= billed as i32;
        self.count_import(a, node, face);
        self.x.trip_stats[SOLD] += face;
        let discount = Fx::mul_int_raw(face, self.r.bill_discount_raw);
        self.claim_cover(a, purse, billed, *carried, face - discount, false);
        self.book_sale(a, billed, face - discount, carried);
        self.x.trip_stats[16] += discount;
    }

    /// A sale to traders outside the group's home nation is their import.
    fn count_import(&mut self, a: usize, node: i32, paid: i64) {
        let payer = self.p.node_float[u(node)];
        if payer != self.p.node_float[u(self.g.home[a])] {
            self.s.imports[u(payer)] += paid;
        }
    }
}
