//! Coins, ranges and what traders can pay (C# `DetailedMerchantJob`: `Abroad`, `CoinsIn`, `Earn`,
//! `Spend`, `PayThere`, `MayGo`, `Payable`, `SellForBill`, `TradersPay`, `OfferAt`).
use super::job::{u, DetailedJob};
use fes_core::{Fx, WorldLevel};

/// Where a group may go from here, and whether it may sell licensed goods (C# `GroupReach`).
#[derive(Clone, Copy, Debug)]
pub struct GroupReach {
    pub licensed: bool,
    pub anchor: i32,
    pub in_range: bool,
}

/// C# `BillHolders.Bank`.
pub const BILL_HOLDER_BANK: i32 = -1;

impl DetailedJob<'_> {
    /// What traders pay a seller per unit: the price less their margin (DEC-098).
    pub(super) fn offer_at(&self, cell: i32) -> i64 {
        let price = i64::from(self.p.price[u(cell)]);
        price - Fx::mul_int_raw(price, self.r.spread_raw)
    }

    pub(super) fn abroad(&self, a: usize, node: i32) -> bool {
        self.p.continent[u(node)] != self.p.continent[u(self.g.home[a])]
    }

    pub(super) fn coins_in(&self, amount: i64, from: i32, to: i32) -> i64 {
        if from == to {
            return amount;
        }
        Fx::mul_int_raw(
            amount,
            self.p.coin_rate[u(from * self.r.continent_count + to)],
        )
    }

    /// Local coins at a node in the group's own, after the exchange fee abroad (DEC-008).
    pub(super) fn earn(&self, a: usize, node: i32, local: i64) -> i64 {
        if !self.abroad(a, node) {
            return local;
        }
        let home = self.p.continent[u(self.g.home[a])];
        let own = self.coins_in(local, self.p.continent[u(node)], home);
        own - Fx::mul_int_raw(own, self.r.exchange_fee_raw)
    }

    /// What local coins at a node cost in the group's own when it buys abroad.
    pub(super) fn spend(&self, a: usize, node: i32, local: i64) -> i64 {
        if !self.abroad(a, node) {
            return local;
        }
        let home = self.p.continent[u(self.g.home[a])];
        let own = self.coins_in(local, self.p.continent[u(node)], home);
        own + Fx::mul_int_raw(own, self.r.exchange_fee_raw)
    }

    /// The group pays its own coins at a node; abroad they go to its home's international bank
    /// and the local coins come out of the bank there as far as it holds them. Returns the local.
    pub(super) fn pay_there(&mut self, a: usize, node: i32, own: i64) -> i64 {
        if !self.abroad(a, node) {
            return own;
        }
        let home = self.p.continent[u(self.g.home[a])];
        let here = self.p.continent[u(node)];
        let mut local = self.coins_in(own, home, here);
        local -= Fx::mul_int_raw(local, self.r.exchange_fee_raw);
        let held = self.s.international[u(here)];
        if local > held {
            local = held.max(0);
        }
        self.s.international[u(home)] += own;
        self.s.international[u(here)] -= local;
        local
    }

    /// Within its range at home, anywhere once abroad, and between continents only if large and
    /// bold enough (DEC-066, DEC-096).
    pub(super) fn may_go(&self, a: usize, at: i32, to: i32, reach: &GroupReach) -> bool {
        let c = self.p.continent;
        let home = self.g.home[a];
        if c[u(to)] != c[u(at)] {
            let range = self.g.traits.range_level[usize::from(self.g.trait_id[a])];
            return self.r.cross_level >= 0
                && i32::from(self.g.scale[a]) <= self.r.cross_level
                && (range <= WorldLevel::Nation as i32 || self.p.node_port[u(home)] != 0)
                && (c[u(to)] == c[u(home)]
                    || self.holds_trade_license(
                        a,
                        self.r.intercontinental_stream,
                        self.r.intercontinental_permille,
                    ));
        }
        let row = self.p.license_row;
        if row[u(to)] != row[u(at)]
            && row[u(to)] != row[u(home)]
            && !self.holds_trade_license(
                a,
                self.r.international_stream,
                self.r.international_permille,
            )
        {
            return false;
        }
        !reach.in_range || self.abroad(a, at) || self.within(to, reach.anchor)
    }

    /// As much of a sum as the traders of a node can pay in coins, then in bills at home (DEC-097).
    pub(super) fn payable(&self, a: usize, node: i32, value: i64) -> i64 {
        let purse = u(self.p.node_float[u(node)]);
        let coins = value.min(self.s.float[purse].max(0));
        if self.abroad(a, node) || self.no_bills(a) {
            return coins;
        }
        let billed = (value - coins).min(self.s.bill_room[purse]);
        coins + billed - Fx::mul_int_raw(billed, self.r.bill_discount_raw)
    }

    /// Traders who cannot pay give a bill; the bank pays the seller at a discount as far as it has
    /// room, or the seller holds it to maturity (DEC-097). Returns the coins received now.
    pub(super) fn sell_for_bill(&mut self, a: usize, purse: i32, face: i64) -> i64 {
        let p = u(purse);
        self.s.bill_room[p] -= face;
        self.x.bill_stats[0] += face;
        let discounted = face - Fx::mul_int_raw(face, self.r.bill_discount_raw);
        let bank = self.s.bill_bank_room[p] >= discounted;
        self.s
            .bill_holder
            .push(if bank { BILL_HOLDER_BANK } else { a as i32 });
        self.s.bill_purse.push(purse);
        self.s.bill_face.push(face);
        self.s.bill_due.push(self.r.turn + self.r.bill_weeks);
        if !bank {
            return 0;
        }
        self.s.bill_bank_room[p] -= discounted;
        self.s.bill_bank_spent[p] += discounted;
        self.x.bill_stats[1] += discounted;
        discounted
    }

    /// Whether the traders of a market can pay for a fair lot at this price, in coins or at home
    /// in bills (DEC-096); households who wish for a necessity pay as well at home (DEC-124).
    pub(super) fn traders_pay(&self, a: usize, node: i32, cell: i32, price: i64) -> bool {
        let units = i64::from(self.r.judge_coin_units);
        if units <= 0 {
            return true;
        }
        let lot = units * price;
        let purse = u(self.p.node_float[u(node)]);
        let abroad = self.abroad(a, node);
        if self.s.float[purse] >= lot
            || (!abroad && !self.no_bills(a) && self.s.bill_room[purse] >= lot)
        {
            return true;
        }
        !abroad && self.x.household_need[u(cell)] >= units && self.s.household_cash[u(node)] >= lot
    }
}
