//! Carrying a load on to where it sells for most (C# `MerchantJobTrips.cs`: `StartSellingTrip`,
//! `BestCarry`, `CarryWorth`; DEC-093).
use super::job::{u, DetailedJob};
use super::job_coins::GroupReach;
use fes_core::Fx;

impl DetailedJob<'_> {
    /// With nothing worth buying, a group carries what it holds to where it sells for most.
    pub(super) fn start_selling_trip(&mut self, a: usize, node: i32) {
        self.collect_rows(a);
        if self.x.rows.is_empty() {
            return;
        }
        let (route, _, trip_cost) = self.best_carry(a, node);
        if route >= 0 && (!self.r.weigh_sales || self.lasts_the_trip(a, route, trip_cost)) {
            self.depart(a, node, route);
        }
    }

    /// The route where the collected rows bring most after the trip's costs, or -1; with the net
    /// and the trip's cost.
    pub(super) fn best_carry(&self, a: usize, node: i32) -> (i32, i64, i64) {
        let reach = self.reach_of(a, node);
        let (mut best_route, mut best_net, mut best_cost) = (-1, 0i64, 0i64);
        let first = u(self.p.out_start[u(node)]);
        for k in first..first + u(self.p.out_count[u(node)]) {
            let r = self.p.route_index[k];
            if !self.may_go(a, node, self.p.route_to[u(r)], &reach) {
                continue;
            }
            let (worth, cost) = self.carry_worth(a, node, r, &reach);
            if worth - cost <= best_net {
                continue;
            }
            best_route = r;
            best_net = worth - cost;
            best_cost = cost;
        }
        (best_route, best_net, best_cost)
    }

    /// What the collected rows bring on a route, and the trip's costs. When sales are weighed a
    /// load is worth only what the traders there can pay, less the weeks of upkeep and what the
    /// road is expected to take.
    fn carry_worth(&self, a: usize, node: i32, r: i32, reach: &GroupReach) -> (i64, i64) {
        let to = self.p.route_to[u(r)];
        let vehicle = self.vehicle_cost(a);
        let mut cost = Fx::mul_int_raw(i64::from(self.p.route_base_fee[u(r)]), vehicle);
        let (mut units, mut value, mut further) = (0i64, 0i64, 0i64);
        for &row in &self.x.rows {
            let good = self.s.cargo_good[u(row)];
            let quantity = i64::from(self.s.cargo_quantity[u(row)]);
            units += quantity;
            if self.r.weigh_sales {
                further += quantity * self.beyond(a, to, node, good, reach, self.r.judge_hops - 1);
            }
            if !reach.licensed && self.needs_license(to, good) {
                continue;
            }
            let cell = self.find_cell(to, good);
            if cell < 0 {
                continue;
            }
            let item_value = quantity * self.offer_at(cell);
            value += item_value;
            cost += self.spend(a, to, Fx::mul_int_raw(item_value, self.toll_rate(to, good)));
        }
        let value = self.earn(a, to, value);
        cost += Fx::mul_int_raw(Fx::mul_int_raw(units, self.p.route_cost_raw[u(r)]), vehicle);
        if !self.r.weigh_sales {
            return (value, cost);
        }
        cost += i64::from(self.p.route_turns[u(r)]) * self.upkeep(a);
        let chance = self.encounter_chance(a, r, value);
        let payable = self.earn(a, to, self.payable(a, to, i64::MAX / 2));
        let loss = Fx::mul_int_raw(Fx::mul_int_raw(value, self.r.cargo_loss_raw), chance) / 2;
        (value.max(further).min(payable) - loss, cost)
    }
}
