//! Bids for goods a market lacks (C# `MerchantJobTrips.cs`: `AddBid`, `BidForWhatIsMissing`,
//! `MostPaidNearby`, `PrepareBidBounds`, `WeighBid`; DEC-095, DEC-096).
use super::job::{u, DetailedJob};
use super::job_coins::GroupReach;
use super::job_search::{CellOffer, TripSearch};
use crate::economy::MarketStore;
use fes_core::{Fx, QualityGrade};

/// The best bid found so far (C# `BestBid`).
struct BestBid {
    cell: i32,
    profit: i64,
    units: i64,
}

/// `WeighUnit`'s test with no tolls or carriage, which only lower a margin.
fn could_show_margin(sell: i64, buy: i64, min_margin_raw: i64) -> bool {
    sell > buy && (sell - buy) << Fx::FRACTION_BITS >= min_margin_raw * buy
}

/// As many units as could be paid for, each at that margin, with no fee.
fn could_beat_bid(sell: i64, buy: i64, fit: i64, budget: i64, beat: i64) -> bool {
    let units = fit.min(budget / buy.max(1));
    units > 0 && units * (sell - buy) > beat
}

fn goods(base_good_count: i32) -> usize {
    u(base_good_count) * QualityGrade::COUNT
}

impl DetailedJob<'_> {
    pub(super) fn add_bid(&mut self, cell: i32, units: i64) {
        let bid = i64::from(self.s.input_bid[u(cell)]) + units;
        self.s.input_bid[u(cell)] = MarketStore::clamp_to_non_negative_int(bid);
    }

    /// A group says what it would carry if the market had it, when that beats what it found.
    pub(super) fn bid_for_what_is_missing(&mut self, a: usize, beat: i64, search: &TripSearch) {
        let mut bid = BestBid {
            cell: -1,
            profit: beat,
            units: 0,
        };
        let node = search.node;
        let continents = self.r.continent_count;
        let home = self.p.continent[u(self.g.home[a])];
        let bound_row =
            u(self.p.continent[u(node)] * continents + home) * goods(self.r.base_good_count);
        let paid = if self.r.judge_coin_units > 0 {
            self.most_paid_nearby(a, node, -1, &search.reach, self.r.judge_hops)
        } else {
            i64::MAX
        };
        let start = self.p.cell_start[u(node)];
        for cell in start..start + self.p.cell_count[u(node)] {
            if self.s.stock[u(cell)] > 0 {
                continue;
            }
            let good = self.p.cell_good[u(cell)];
            let buy = self.spend(a, node, i64::from(self.p.price[u(cell)]));
            let sell = self.x.bid_bound[bound_row + u(good)].min(paid);
            if !could_show_margin(sell, buy, search.min_margin_raw) {
                continue;
            }
            let fit = self.fit(good, search.room_weight, search.room_size);
            if fit > 0 && could_beat_bid(sell, buy, fit, search.budget, bid.profit) {
                let offer = CellOffer {
                    cell,
                    good,
                    buy,
                    fit,
                    cap: fit,
                };
                self.weigh_bid(a, &offer, search, &mut bid);
            }
        }
        if bid.cell >= 0 {
            self.add_bid(bid.cell, bid.units);
        }
    }

    /// No trader pays more for a unit than a fair lot of it fits the purse, so this bounds a
    /// group's sales at this moment, whatever the good.
    fn most_paid_nearby(&self, a: usize, at: i32, from: i32, reach: &GroupReach, hops: i32) -> i64 {
        let mut best = 0i64;
        let first = u(self.p.out_start[u(at)]);
        for k in first..first + u(self.p.out_count[u(at)]) {
            let next = self.p.route_to[u(self.p.route_index[k])];
            if next == from || !self.may_go(a, at, next, reach) {
                continue;
            }
            let purse = u(self.p.node_float[u(next)]);
            let bills = if !self.abroad(a, next) && !self.no_bills(a) {
                self.s.bill_room[purse]
            } else {
                0
            };
            let held = self.s.float[purse].max(bills);
            let price = (held / i64::from(self.r.judge_coin_units)).min(self.x.bid_top_price);
            best = best.max(self.earn(a, next, price - Fx::mul_int_raw(price, self.r.spread_raw)));
            if hops > 1 {
                best = best.max(self.most_paid_nearby(a, next, at, reach, hops - 1));
            }
        }
        best
    }

    /// Prices hold still during the job, so this bounds every sale a group could weigh.
    pub(super) fn prepare_bid_bounds(&mut self) {
        let goods = goods(self.r.base_good_count);
        let continents = u(self.r.continent_count);
        self.x.bid_best.fill(0);
        self.x.bid_linked.fill(false);
        self.x.bid_top_price = 0;
        for node in 0..self.p.continent.len() {
            let continent = u(self.p.continent[node]);
            let start = u(self.p.cell_start[node]);
            for c in start..start + u(self.p.cell_count[node]) {
                let i = continent * goods + u(self.p.cell_good[c]);
                self.x.bid_best[i] = self.x.bid_best[i].max(self.offer_at(c as i32));
                self.x.bid_top_price = self.x.bid_top_price.max(i64::from(self.p.price[c]));
            }
            if self.r.cross_level >= 0 {
                self.link_routes_of(node, continent * continents);
            }
        }
        self.link_continents_across_routes();
        for from in 0..continents {
            for home in 0..continents {
                self.bound_bids_from(from, home, goods);
            }
        }
    }

    /// Marks the continents the node's routes lead to as linked from its own (row `row`).
    fn link_routes_of(&mut self, node: usize, row: usize) {
        let first = u(self.p.out_start[node]);
        for k in first..first + u(self.p.out_count[node]) {
            let to = self.p.route_to[u(self.p.route_index[k])];
            self.x.bid_linked[row + u(self.p.continent[u(to)])] = true;
        }
    }

    fn link_continents_across_routes(&mut self) {
        let count = u(self.r.continent_count);
        for c in 0..count {
            self.x.bid_linked[c * count + c] = true;
        }
        // Via outer, from inner, as C#.
        for i in 0..count * count {
            let (via, from) = (i / count, i % count);
            if self.x.bid_linked[from * count + via] {
                self.link_onward(from, via, count);
            }
        }
    }

    fn link_onward(&mut self, from: usize, via: usize, count: usize) {
        for to in 0..count {
            if self.x.bid_linked[via * count + to] {
                self.x.bid_linked[from * count + to] = true;
            }
        }
    }

    fn bound_bids_from(&mut self, from: usize, home: usize, goods: usize) {
        let count = u(self.r.continent_count);
        let row = (from * count + home) * goods;
        self.x.bid_bound[row..row + goods].fill(0);
        for there in 0..count {
            if !self.x.bid_linked[from * count + there] {
                continue;
            }
            for good in 0..goods {
                let earned = self.earn_across(
                    there as i32,
                    home as i32,
                    self.x.bid_best[there * goods + good],
                );
                self.x.bid_bound[row + good] = self.x.bid_bound[row + good].max(earned);
            }
        }
    }

    fn earn_across(&self, there: i32, home: i32, local: i64) -> i64 {
        if there == home {
            return local;
        }
        let own = self.coins_in(local, there, home);
        own - Fx::mul_int_raw(own, self.r.exchange_fee_raw)
    }

    fn weigh_bid(&self, a: usize, offer: &CellOffer, search: &TripSearch, bid: &mut BestBid) {
        let first = u(self.p.out_start[u(search.node)]);
        for k in first..first + u(self.p.out_count[u(search.node)]) {
            let r = self.p.route_index[k];
            let to = self.p.route_to[u(r)];
            if !self.may_go(a, search.node, to, &search.reach) {
                continue;
            }
            let to_cell = self.find_cell(to, offer.good);
            if to_cell < 0 || (!search.reach.licensed && self.needs_license(to, offer.good)) {
                continue;
            }
            let there = self.earn(a, to, self.offer_at(to_cell));
            let Some(unit) = self.weigh_unit(a, r, to_cell, there, offer, search) else {
                continue;
            };
            let units = offer
                .fit
                .min((search.budget - unit.base_fee) / unit.unit_cost);
            if units <= 0 {
                continue;
            }
            let profit = Fx::mul_int_raw(units, unit.margin_raw) - unit.base_fee;
            if profit <= bid.profit {
                continue;
            }
            bid.profit = profit;
            bid.cell = offer.cell;
            bid.units = units;
        }
    }
}
