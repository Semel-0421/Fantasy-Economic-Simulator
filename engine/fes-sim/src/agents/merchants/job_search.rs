//! How a group chooses a load and a route (C# `MerchantJobTrips.cs`; SYS-MERCHANTS, DEC-093,
//! DEC-095, DEC-096, DEC-122).
use super::job::{u, DetailedJob};
use super::job_coins::GroupReach;
use crate::economy::MarketStore;
use fes_core::Fx;

/// What a group asks of a load, and the best it has found (C# `TripSearch`).
#[derive(Clone, Copy, Debug)]
pub(super) struct TripSearch {
    pub node: i32,
    pub power: i32,
    pub reach: GroupReach,
    pub memory: super::NodeMemory,
    pub budget: i64,
    pub room_weight: i64,
    pub room_size: i64,
    pub min_margin_raw: i64,
    pub index_raw: i64,
    pub route: i32,
    pub cell: i32,
    pub quantity: i64,
    pub score: i64,
    pub wanted: i64,
    pub profit: i64,
}

/// A good a group could take from its node, what a unit costs it and how many would fit.
#[derive(Clone, Copy, Debug)]
pub(super) struct CellOffer {
    pub cell: i32,
    pub good: i32,
    pub buy: i64,
    pub fit: i64,
    pub cap: i64,
}

/// What a unit brings on a route (C# `UnitOnRoute`).
#[derive(Clone, Copy, Debug, Default)]
pub(super) struct UnitOnRoute {
    pub sell: i64,
    pub base_fee: i64,
    pub margin_raw: i64,
    pub unit_cost: i64,
}

impl DetailedJob<'_> {
    pub(super) fn reach_of(&self, a: usize, node: i32) -> GroupReach {
        let anchor = self.range_anchor(a);
        GroupReach {
            licensed: self.holds_licenses(a),
            anchor,
            in_range: self.within(node, anchor),
        }
    }

    pub(super) fn try_start_trip(&mut self, a: usize) {
        let node = self.s.node[a];
        if self.s.cash[a] <= 0 {
            self.sell_or_go_home(a, node);
            return;
        }
        let mut search = self.new_search(a, node);
        self.list_stocked_cells(node);
        let start = u(self.x.stocked_start[u(node)]);
        for s in start..start + u(self.x.stocked_count[u(node)]) {
            let cell = self.x.stocked_cells[s];
            self.weigh_cell(a, cell, &mut search);
        }
        if search.route < 0 {
            if self.r.bids {
                self.bid_for_what_is_missing(a, 0, &search);
            }
            self.sell_or_go_home(a, node);
            return;
        }
        if self.r.bids && search.wanted > search.quantity {
            self.add_bid(search.cell, search.wanted - search.quantity);
        }
        if self.r.bids_while_loaded {
            self.bid_for_what_is_missing(a, search.profit, &search);
        }
        let quantity = self.within_the_bank_there(a, node, search.cell, search.quantity);
        if quantity <= 0 {
            self.sell_or_go_home(a, node);
            return;
        }
        self.buy_load(a, node, search.cell, quantity);
        self.depart(a, node, search.route);
    }

    fn sell_or_go_home(&mut self, a: usize, node: i32) {
        self.start_selling_trip(a, node);
        self.head_home(a, node);
    }

    fn new_search(&self, a: usize, node: i32) -> TripSearch {
        let t = usize::from(self.g.trait_id[a]);
        // DEC-122: weeks of upkeep stay in coins, so a slow sale does not ruin the group.
        let reserve = i64::from(self.r.reserve_upkeep_weeks) * self.upkeep(a);
        let cash = (self.s.cash[a] - reserve).max(0);
        let spend_raw = self.g.traits.spend_raw[t];
        let (mut load_weight, mut load_size) = (0i64, 0i64);
        let mut r = self.x.first_row[a];
        while r >= 0 {
            let good = u(self.s.cargo_good[u(r)]);
            let quantity = i64::from(self.s.cargo_quantity[u(r)]);
            load_weight += quantity * i64::from(self.p.good_weight[good]);
            load_size += quantity * i64::from(self.p.good_size[good]);
            r = self.x.next_row[u(r)];
        }
        let level = self.g.levels[usize::from(self.g.scale[a])];
        TripSearch {
            node,
            power: self.g.traits.distance_power[t],
            reach: self.reach_of(a, node),
            memory: self.s.memory[a],
            budget: if spend_raw == Fx::ONE_RAW {
                cash
            } else {
                Fx::mul_int_raw(cash, spend_raw)
            },
            room_weight: self.capacity(a, level.max_weight) - load_weight,
            room_size: self.capacity(a, level.max_size) - load_size,
            min_margin_raw: self.g.traits.min_margin_raw[t],
            index_raw: self.p.continent_index[u(self.p.continent[u(node)])],
            route: -1,
            cell: -1,
            quantity: 0,
            score: 0,
            wanted: 0,
            profit: 0,
        }
    }

    fn capacity(&self, a: usize, at_level: i64) -> i64 {
        let traited = Fx::mul_int_raw(
            at_level,
            self.g.traits.capacity_raw[usize::from(self.g.trait_id[a])],
        );
        Fx::mul_int_raw(
            traited,
            self.g.vehicle_capacity[usize::from(self.g.vehicle[a])],
        )
    }

    /// The first group at a node lists its stocked cells (stock only falls during the job).
    fn list_stocked_cells(&mut self, node: i32) {
        let n = u(node);
        if self.x.stocked_start[n] >= 0 {
            return;
        }
        self.x.stocked_start[n] = self.x.stocked_cells.len() as i32;
        let start = self.p.cell_start[n];
        for c in start..start + self.p.cell_count[n] {
            if self.s.stock[u(c)] > 0 {
                self.x.stocked_cells.push(c);
            }
        }
        self.x.stocked_count[n] = self.x.stocked_cells.len() as i32 - self.x.stocked_start[n];
    }

    fn weigh_cell(&mut self, a: usize, cell: i32, search: &mut TripSearch) {
        let stock = i64::from(self.s.stock[u(cell)]);
        if stock <= 0 {
            return;
        }
        let good = self.p.cell_good[u(cell)];
        let fit = self.fit(good, search.room_weight, search.room_size).max(0);
        let cap = stock.min(fit);
        if cap <= 0 {
            return;
        }
        let buy = self.spend(a, search.node, i64::from(self.p.price[u(cell)]));
        let offer = CellOffer {
            cell,
            good,
            buy,
            fit,
            cap,
        };
        let first = u(self.p.out_start[u(search.node)]);
        for k in first..first + u(self.p.out_count[u(search.node)]) {
            self.weigh_route(a, self.p.route_index[k], &offer, search);
        }
    }

    pub(super) fn fit(&self, good: i32, room_weight: i64, room_size: i64) -> i64 {
        let mut fit = i64::from(i32::MAX);
        let weight = i64::from(self.p.good_weight[u(good)]);
        let size = i64::from(self.p.good_size[u(good)]);
        if weight > 0 {
            fit = fit.min(room_weight / weight);
        }
        if size > 0 {
            fit = fit.min(room_size / size);
        }
        fit
    }

    fn weigh_route(&self, a: usize, r: i32, offer: &CellOffer, search: &mut TripSearch) {
        let to = self.p.route_to[u(r)];
        if !self.may_go(a, search.node, to, &search.reach) {
            return;
        }
        let to_cell = self.find_cell(to, offer.good);
        if to_cell < 0 || (!search.reach.licensed && self.needs_license(to, offer.good)) {
            return;
        }
        // A node nobody told the group about is judged at the anchor price.
        let there = if !self.r.rumors || search.memory.knows(to) || self.p.core_strength[u(to)] > 0
        {
            self.earn(a, to, self.offer_at(to_cell))
        } else {
            let base = i64::from(self.p.good_base_price[u(offer.good)]);
            Fx::round_shift(base.wrapping_mul(search.index_raw))
        };
        let Some(unit) = self.weigh_unit(a, r, to_cell, there, offer, search) else {
            return;
        };
        let affordable = (search.budget - unit.base_fee) / unit.unit_cost;
        let quantity = offer.cap.min(affordable);
        if quantity <= 0 {
            return;
        }
        let mut profit = Fx::mul_int_raw(quantity, unit.margin_raw) - unit.base_fee;
        let turns = self.p.route_turns[u(r)];
        if self.r.count_trip_costs {
            let worth = quantity * unit.sell;
            let loss = Fx::mul_int_raw(
                Fx::mul_int_raw(worth, self.r.cargo_loss_raw),
                self.encounter_chance(a, r, worth),
            ) / 2;
            profit -= i64::from(turns) * self.upkeep(a) + loss;
            if profit <= 0 {
                return;
            }
        }
        let score = trip_score(profit, turns, search.power);
        if score <= search.score {
            return;
        }
        search.route = r;
        search.cell = offer.cell;
        search.quantity = quantity;
        search.score = score;
        search.wanted = offer.fit.min(affordable);
        search.profit = profit;
    }

    /// What a unit brings on a route at this price there; `None` when that falls short of what
    /// the group asks.
    pub(super) fn weigh_unit(
        &self,
        a: usize,
        r: i32,
        to_cell: i32,
        there: i64,
        offer: &CellOffer,
        search: &TripSearch,
    ) -> Option<UnitOnRoute> {
        let to = self.p.route_to[u(r)];
        let vehicle = self.vehicle_cost(a);
        let per_unit_raw = Fx::round_shift(self.p.route_cost_raw[u(r)].wrapping_mul(vehicle));
        let toll = Fx::mul_int_raw(there, self.toll_rate(to, offer.good));
        let price = i64::from(self.p.price[u(to_cell)]);
        let there = if self.traders_pay(a, to, to_cell, price) {
            there
        } else {
            0
        };
        let beyond = self.beyond(
            a,
            to,
            search.node,
            offer.good,
            &search.reach,
            self.r.judge_hops - 1,
        );
        let sell = there.max(beyond);
        let base_fee = Fx::mul_int_raw(i64::from(self.p.route_base_fee[u(r)]), vehicle);
        let margin_raw = ((sell - offer.buy - toll) << Fx::FRACTION_BITS) - per_unit_raw;
        let unit_cost = (offer.buy + toll + Fx::mul_int_raw(1, per_unit_raw)).max(1);
        let enough = margin_raw > 0 && margin_raw >= search.min_margin_raw * offer.buy;
        enough.then_some(UnitOnRoute {
            sell,
            base_fee,
            margin_raw,
            unit_cost,
        })
    }

    /// Abroad, what it pays comes out of the bank there as local coins (DEC-031).
    fn within_the_bank_there(&self, a: usize, node: i32, cell: i32, quantity: i64) -> i64 {
        let bank = self.s.international[u(self.p.continent[u(node)])];
        let price = i64::from(self.p.price[u(cell)]);
        if !self.abroad(a, node) || quantity * price <= bank {
            return quantity;
        }
        if bank > 0 && price > 0 {
            bank / price
        } else {
            0
        }
    }

    fn buy_load(&mut self, a: usize, node: i32, cell: i32, quantity: i64) {
        let c = u(cell);
        self.s.stock[c] -= quantity as i32;
        self.s.supply[c] =
            MarketStore::clamp_to_non_negative_int(i64::from(self.s.supply[c]) + quantity);
        self.s.demand[c] =
            MarketStore::clamp_to_non_negative_int(i64::from(self.s.demand[c]) + quantity);
        let cost = quantity * i64::from(self.p.price[c]);
        let own = self.spend(a, node, cost);
        self.s.cash[a] -= own;
        self.s.load_cost[a] += own;
        self.x.trip_stats[9] += own;
        self.s.float[u(self.p.node_float[u(node)])] += cost;
        if self.abroad(a, node) {
            self.s.international[u(self.p.continent[u(self.g.home[a])])] += own;
            self.s.international[u(self.p.continent[u(node)])] -= cost;
        }
        self.add_cargo(a, self.p.cell_good[c], quantity as i32);
    }

    /// What a unit brings past a node within the hops left, after each hop's charge and toll
    /// (DEC-095). It may pass through a market that does not trade the good.
    pub(super) fn beyond(
        &self,
        a: usize,
        at: i32,
        from: i32,
        good: i32,
        reach: &GroupReach,
        hops: i32,
    ) -> i64 {
        if hops <= 0 {
            return 0;
        }
        let mut best = 0i64;
        let first = u(self.p.out_start[u(at)]);
        for k in first..first + u(self.p.out_count[u(at)]) {
            let r = self.p.route_index[k];
            let next = self.p.route_to[u(r)];
            if next == from || !self.may_go(a, at, next, reach) {
                continue;
            }
            let cell = self.find_cell(next, good);
            let here = if cell >= 0 {
                self.earn(a, next, self.offer_at(cell))
            } else {
                0
            };
            let further = if hops > 1 {
                self.beyond(a, next, at, good, reach, hops - 1)
            } else {
                0
            };
            let worth = self.sold_at(a, next, cell, good, here, reach).max(further);
            if worth <= 0 {
                continue;
            }
            let carriage =
                Fx::round_shift(self.p.route_cost_raw[u(r)].wrapping_mul(self.vehicle_cost(a)));
            let toll = Fx::mul_int_raw(
                if cell >= 0 { here } else { worth },
                self.toll_rate(next, good),
            );
            best = best.max(worth - toll - Fx::mul_int_raw(1, carriage));
        }
        best
    }

    fn sold_at(
        &self,
        a: usize,
        node: i32,
        cell: i32,
        good: i32,
        worth: i64,
        reach: &GroupReach,
    ) -> i64 {
        let sells = cell >= 0
            && (reach.licensed || !self.needs_license(node, good))
            && self.traders_pay(a, node, cell, i64::from(self.p.price[u(cell)]));
        if sells {
            worth
        } else {
            0
        }
    }
}

/// C# `TripScore`: profit per turn, or per turn squared, for the group's distance power.
pub(super) fn trip_score(profit: i64, route_turns: i32, power: i32) -> i64 {
    if profit <= 0 || power <= 0 {
        return profit;
    }
    let turns = i64::from(route_turns.max(1));
    let divisor = if power == 1 { turns } else { turns * turns };
    (profit << Fx::FRACTION_BITS) / divisor
}
