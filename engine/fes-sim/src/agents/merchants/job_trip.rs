//! Setting off and what happens on the road (C# `DetailedMerchantJob`: `SetOff`, `HeadHome`,
//! `EncounterChance`, `TripCharges`, `Depart`, `BuyProtection`, `RollEncounter`, `TakePremium`,
//! `RefundFrom`; DEC-015, DEC-026, DEC-061, DEC-095, DEC-096, DEC-099, DEC-106).
use super::job::{u, DetailedJob};
use crate::agents::merchants::VEHICLE_PACK;
use fes_core::{DetRandom, Fx};

/// What a route charges the collected rows, in the group's coins.
pub(super) struct Charges {
    pub fee: i64,
    pub tolls: i64,
    pub value: i64,
}

impl DetailedJob<'_> {
    /// A group that did nothing this turn takes to the road (DEC-106): to a market it knows
    /// nothing of if it can, for the cheapest trip it can pay for (DEC-107).
    pub(super) fn set_off(&mut self, a: usize, node: i32) {
        let reach = self.reach_of(a, node);
        self.s.memory[a].learn(node);
        let memory = self.s.memory[a];
        self.collect_rows(a);
        let (mut best, mut best_cost, mut best_new) = (-1, i64::MAX, false);
        let first = u(self.p.out_start[u(node)]);
        for k in first..first + u(self.p.out_count[u(node)]) {
            let r = self.p.route_index[k];
            let to = self.p.route_to[u(r)];
            if self.p.continent[u(to)] != self.p.continent[u(node)]
                || !self.may_go(a, node, to, &reach)
            {
                continue;
            }
            let charges = self.trip_charges(a, r);
            let cost = charges.fee + charges.tolls;
            if cost > self.s.cash[a] {
                continue;
            }
            let is_new = !memory.knows(to);
            if best >= 0 && (best_new && !is_new || best_new == is_new && cost >= best_cost) {
                continue;
            }
            best = r;
            best_cost = cost;
            best_new = is_new;
        }
        if best < 0 {
            return;
        }
        self.x.trip_stats[20] += 1;
        self.depart(a, node, best);
    }

    /// Abroad a group sails home with what it holds (DEC-096); above its home it comes back down
    /// empty to where the goods are (DEC-095).
    pub(super) fn head_home(&mut self, a: usize, node: i32) {
        if self.s.destination[a] >= 0 {
            return;
        }
        let home = self.g.home[a];
        if self.abroad(a, node) {
            let way = node * self.r.continent_count + self.p.continent[u(home)];
            let route = self.p.route_toward[u(way)];
            if route >= 0 {
                self.depart(a, node, route);
            }
            return;
        }
        if self.has_cargo(a) {
            return;
        }
        let level = self.p.level;
        if !self.r.return_home || node == home || level[u(node)] >= level[u(home)] {
            return;
        }
        let first = u(self.p.out_start[u(node)]);
        for k in first..first + u(self.p.out_count[u(node)]) {
            let down = self.p.route_to[u(self.p.route_index[k])];
            if level[u(down)] <= level[u(node)] || !self.within(home, down) {
                continue;
            }
            self.depart(a, node, self.p.route_index[k]);
            return;
        }
    }

    /// The chance of an encounter with a load of this value; bigger vehicles draw more (DEC-085).
    pub(super) fn encounter_chance(&self, a: usize, route: i32, value: i64) -> i64 {
        let scaled =
            Fx::ONE_RAW + value.wrapping_shl(Fx::FRACTION_BITS) / self.r.danger_value_scale;
        let mut chance =
            self.p.route_danger_raw[u(route)].wrapping_mul(scaled) >> Fx::FRACTION_BITS;
        let vehicle = self.g.vehicle_danger[usize::from(self.g.vehicle[a])];
        chance = Fx::round_shift(chance.wrapping_mul(vehicle));
        chance.clamp(0, Fx::ONE_RAW)
    }

    /// The fee, the tolls at the route's end and what the load is worth there.
    pub(super) fn trip_charges(&self, a: usize, route: i32) -> Charges {
        let destination = self.p.route_to[u(route)];
        let (mut units, mut tolls, mut value) = (0i64, 0i64, 0i64);
        for &row in &self.x.rows {
            let good = self.s.cargo_good[u(row)];
            let quantity = i64::from(self.s.cargo_quantity[u(row)]);
            units += quantity;
            let cell = self.find_nearest_cell(destination, good);
            if cell < 0 {
                continue;
            }
            let item_value = quantity * i64::from(self.p.price[u(cell)]);
            tolls += Fx::mul_int_raw(item_value, self.toll_rate(destination, good));
            value += item_value;
        }
        let base = i64::from(self.p.route_base_fee[u(route)]);
        let carriage = base + Fx::mul_int_raw(units, self.p.route_cost_raw[u(route)]);
        let fee = Fx::mul_int_raw(carriage, self.vehicle_cost(a));
        Charges {
            fee,
            tolls: self.spend(a, destination, tolls),
            value: self.earn(a, destination, value),
        }
    }

    pub(super) fn vehicle_cost(&self, a: usize) -> i64 {
        self.g.vehicle_cost[usize::from(self.g.vehicle[a])]
    }

    /// The same trip cost as the player's (DEC-015): merchants need no ship (DEC-034).
    pub(super) fn depart(&mut self, a: usize, node: i32, route: i32) {
        let destination = self.p.route_to[u(route)];
        self.collect_rows(a);
        let c = self.trip_charges(a, route);
        self.s.cash[a] -= c.fee + c.tolls;
        self.x.trip_stats[10] += c.fee;
        self.x.trip_stats[11] += c.tolls;
        let wages = self.pay_there(a, node, c.fee);
        self.s.household_cash[u(node)] += wages;
        let tolls = self.pay_there(a, destination, c.tolls);
        self.x.tolls_by_node[u(destination)] += tolls;
        self.s.destination[a] = destination;
        self.s.arrival_turn[a] = self.r.turn + self.p.route_turns[u(route)];
        let mut chance = self.encounter_chance(a, route, c.value);
        self.x.trip_stats[0] += 1;
        let insured = self.buy_protection(a, node, c.value, &mut chance);
        self.buy_cover(a, destination, c.value, self.r.hedge_chance_raw);
        self.roll_encounter(a, node, c.value, chance, insured);
    }

    /// The policy first, then guards for what the group still fears, weighed by its loss aversion
    /// (DEC-099). Returns whether it is insured.
    fn buy_protection(&mut self, a: usize, node: i32, value: i64, chance: &mut i64) -> bool {
        let abroad = self.abroad(a, node);
        let r = self.r;
        let mut expected = Fx::mul_int_raw(Fx::mul_int_raw(value, r.cargo_loss_raw), *chance) / 2;
        let aversion = i64::from(self.g.traits.loss_aversion[usize::from(self.g.trait_id[a])]);
        let vehicle = self.g.vehicle[a];
        let mut premium = Fx::mul_int_raw(
            Fx::mul_int_raw(value, r.insurance_premium_raw),
            self.g.vehicle_premium[usize::from(vehicle)],
        );
        if self.g.association[a] >= 0 && vehicle > VEHICLE_PACK {
            premium = Fx::mul_int_raw(premium, r.member_premium_raw);
        }
        let insured = !abroad
            && Fx::mul_int_raw(expected, r.insurance_refund_raw) * aversion > premium
            && self.s.cash[a] >= premium;
        if insured {
            self.s.cash[a] -= premium;
            self.x.trip_stats[13] += premium;
            self.take_premium(node, premium);
            expected -= Fx::mul_int_raw(expected, r.insurance_refund_raw);
            self.x.trip_stats[2] += 1;
        }
        let guard_cost = r.guard_base_cost + Fx::mul_int_raw(value, r.guard_cost_share_raw);
        // The policy covers no coins: only guards keep them safe.
        let cash = self.s.cash[a].max(0);
        let coins_at_risk = Fx::mul_int_raw(Fx::mul_int_raw(cash, r.cash_loss_raw), *chance) / 2;
        if !abroad
            && Fx::mul_int_raw(expected + coins_at_risk, r.guard_reduction_raw) * aversion
                > guard_cost
            && self.s.cash[a] >= guard_cost
        {
            self.s.cash[a] -= guard_cost;
            self.x.trip_stats[12] += guard_cost;
            self.s.household_cash[u(node)] += guard_cost;
            *chance -= Fx::round_shift(chance.wrapping_mul(r.guard_reduction_raw));
            self.x.trip_stats[1] += 1;
        }
        insured
    }

    fn roll_encounter(&mut self, a: usize, node: i32, value: i64, chance: i64, insured: bool) {
        let r = self.r;
        let mut rng = DetRandom::for_key(r.seed, r.turn, r.encounter_stream, a as i32, 1);
        if rng.next_fx01().raw() >= chance {
            return;
        }
        self.x.trip_stats[3] += 1;
        if rng.next_int(2) == 0 {
            // [Draft] Stolen coins end up spent where the trip began.
            let stolen = Fx::mul_int_raw(self.s.cash[a].max(0), r.cash_loss_raw);
            self.s.cash[a] -= stolen;
            self.x.trip_stats[15] += stolen;
            let local = self.pay_there(a, node, stolen);
            self.s.household_cash[u(node)] += local;
            return;
        }
        for i in 0..self.x.rows.len() {
            let row = u(self.x.rows[i]);
            let quantity = self.s.cargo_quantity[row];
            self.s.cargo_quantity[row] -=
                Fx::mul_int_raw(i64::from(quantity), r.cargo_loss_raw) as i32;
        }
        self.s.hedge[a] -= Fx::mul_int_raw(self.s.hedge[a], r.cargo_loss_raw);
        if !insured {
            return;
        }
        let claim = Fx::mul_int_raw(
            Fx::mul_int_raw(value, r.cargo_loss_raw),
            r.insurance_refund_raw,
        );
        let refund = self.refund_from(node, claim);
        self.s.cash[a] += refund;
        self.x.trip_stats[17] += refund;
    }

    /// The origin city's guild runs the insurance (DEC-055, DEC-061); without one, its households.
    fn take_premium(&mut self, node: i32, premium: i64) {
        let guild = self.p.node_insurer[u(node)];
        if guild >= 0 {
            self.s.group_cash[u(guild)] += premium;
        } else {
            self.s.household_cash[u(node)] += premium;
        }
    }

    /// [Draft] As far as the one behind the policy holds.
    fn refund_from(&mut self, node: i32, refund: i64) -> i64 {
        let guild = self.p.node_insurer[u(node)];
        let held = if guild >= 0 {
            self.s.group_cash[u(guild)]
        } else {
            self.s.household_cash[u(node)]
        };
        let refund = if refund > held { held.max(0) } else { refund };
        if guild >= 0 {
            self.s.group_cash[u(guild)] -= refund;
        } else {
            self.s.household_cash[u(node)] -= refund;
        }
        refund
    }
}
