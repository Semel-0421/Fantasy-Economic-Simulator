//! C# `MerchantFlowJob`: aggregate merchant trade (DEC-003). For every aggregate node, along every
//! route, each good both markets trade moves when the destination price beats the origin's plus
//! the per-unit charge and toll, as a share of the origin's stock within the node's pooled cargo
//! capacity. Nodes, cells and routes run in order; the Burst job runs here as a plain loop.
use crate::economy::MarketStore;
use fes_core::{Fx, QualityGrade, SimDetail};

/// The flow job's inputs and outputs, as slices of the C# job's arrays.
pub struct FlowJob<'a> {
    pub category_count: i32,
    pub flow_share_raw: i64,
    pub licensed_share_raw: i64,
    pub base_good_count: i32,
    /// Share of the normal flow between continents (raw Fx); zero is none.
    pub cross_flow_raw: i64,
    pub continent_count: i32,
    pub coin_rate: &'a [i64],
    pub exchange_fee_raw: i64,
    pub international: &'a mut [i64],
    /// Share of a flow's margin the traders pay as wages at the origin (raw Fx, DEC-098).
    pub wage_raw: i64,
    pub household_cash: &'a mut [i64],
    pub detail: &'a [u8],
    pub continent: &'a [i32],
    pub capacity: &'a [i64],
    pub spend_raw: &'a [i64],
    pub min_margin_raw: &'a [i64],
    pub vehicle_cost_raw: &'a [i64],
    pub vehicle_danger_raw: &'a [i64],
    pub category_specialty: &'a [u8],
    pub tariff: &'a [i64],
    pub surcharge: &'a [i64],
    pub out_start: &'a [i32],
    pub out_count: &'a [i32],
    pub route_index: &'a [i32],
    pub route_to: &'a [i32],
    pub route_turns: &'a [i32],
    pub route_cost_raw: &'a [i64],
    pub route_budget: &'a mut [i64],
    pub route_expected_loss: &'a [i64],
    pub cell_start: &'a [i32],
    pub cell_count: &'a [i32],
    pub cell_good: &'a [i32],
    pub good_category: &'a [i32],
    pub good_weight: &'a [i32],
    pub license_row: &'a [i32],
    pub licensed: &'a [u8],
    pub price: &'a [i32],
    pub node_float: &'a [i32],
    pub float: &'a mut [i64],
    pub imports: &'a mut [i64],
    pub stock: &'a mut [i32],
    pub supply: &'a mut [i32],
    pub demand: &'a mut [i32],
}

fn u(i: i32) -> usize {
    i as usize
}

impl FlowJob<'_> {
    pub fn execute(&mut self) {
        for n in 0..self.detail.len() {
            if self.detail[n] == SimDetail::Detailed as u8
                || self.capacity[n] <= 0
                || !self.share_capacity(n)
            {
                continue;
            }
            let start = self.cell_start[n];
            for c in start..start + self.cell_count[n] {
                self.move_cell(n, c);
            }
        }
    }

    /// Each route open to the node's groups gets its part of their capacity, less the longer it
    /// takes. False when none is open.
    fn share_capacity(&mut self, n: usize) -> bool {
        let first = u(self.out_start[n]);
        let last = first + u(self.out_count[n]);
        let routes = (first..last)
            .filter(|&k| {
                self.cross_flow_raw > 0
                    || self.continent[u(self.route_to[u(self.route_index[k])])] == self.continent[n]
            })
            .count() as i64;
        if routes == 0 {
            return false;
        }
        for k in first..last {
            let r = u(self.route_index[k]);
            let cross = self.continent[u(self.route_to[r])] != self.continent[n];
            self.route_budget[r] = if self.cross_flow_raw <= 0 && cross {
                0
            } else {
                self.capacity[n] / (routes * i64::from(self.route_turns[r].max(1)))
            };
        }
        true
    }

    fn move_cell(&mut self, n: usize, c: i32) {
        let first = u(self.out_start[n]);
        let last = first + u(self.out_count[n]);
        let mut k = first;
        while k < last && self.stock[u(c)] > 0 {
            let r = self.route_index[k];
            k += 1;
            if self.route_budget[u(r)] <= 0 {
                continue;
            }
            let to_cell = self.find_cell(self.route_to[u(r)], self.cell_good[u(c)]);
            if to_cell < 0 {
                continue;
            }
            let mut budget = self.route_budget[u(r)];
            self.flow(n as i32, c, to_cell, r, &mut budget);
            self.route_budget[u(r)] = budget;
        }
    }

    fn find_cell(&self, market: i32, good: i32) -> i32 {
        let mut lo = self.cell_start[u(market)];
        let mut hi = lo + self.cell_count[u(market)] - 1;
        while lo <= hi {
            let mid = (lo + hi) >> 1;
            let g = self.cell_good[u(mid)];
            if g == good {
                return mid;
            }
            if g < good {
                lo = mid + 1;
            } else {
                hi = mid - 1;
            }
        }
        -1
    }

    /// C# `Move`.
    fn flow(&mut self, from: i32, from_cell: i32, to_cell: i32, r: i32, budget: &mut i64) {
        let to = self.route_to[u(r)];
        let good = self.cell_good[u(from_cell)];
        let cross = self.continent[u(from)] != self.continent[u(to)];
        let sell = self.in_origin_coin(from, to, i64::from(self.price[u(to_cell)]));
        if cross && sell <= 0 {
            return;
        }
        let buy = i64::from(self.price[u(from_cell)]);
        let vehicle = self.vehicle_cost_raw[u(from)];
        let per_unit_raw = Fx::round_shift(self.route_cost_raw[u(r)].wrapping_mul(vehicle));
        let toll = Fx::mul_int_raw(sell, self.toll_rate(to, good));
        let margin_raw = ((sell - buy - toll) << Fx::FRACTION_BITS) - per_unit_raw;
        if margin_raw <= 0
            || self.stock[u(from_cell)] <= 0
            || margin_raw < self.min_margin_raw[u(from)] * buy
        {
            return;
        }
        let pay = self.pay_in_destination_coin(from, to, buy);
        let quantity = self.flow_quantity(from, from_cell, to, sell, margin_raw, *budget);
        let quantity = self.within_coins(from, to, quantity, buy, pay);
        if quantity <= 0 {
            return;
        }
        *budget -= quantity * i64::from(self.good_weight[u(good)]);
        self.settle(from, to, quantity, buy, pay);
        if self.wage_raw > 0 {
            let wage = Fx::mul_int_raw(Fx::mul_int_raw(quantity, margin_raw), self.wage_raw);
            self.pay_wage(from, to, wage);
        }
        let danger = self.vehicle_danger_raw[u(from)];
        let loss_raw = Fx::round_shift(self.route_expected_loss[u(r)].wrapping_mul(danger));
        let fc = u(from_cell);
        self.stock[fc] -= quantity as i32;
        self.supply[fc] =
            MarketStore::clamp_to_non_negative_int(i64::from(self.supply[fc]) + quantity);
        self.demand[fc] =
            MarketStore::clamp_to_non_negative_int(i64::from(self.demand[fc]) + quantity);
        let arrived = quantity - Fx::mul_int_raw(quantity, loss_raw);
        let tc = u(to_cell);
        self.supply[tc] =
            MarketStore::clamp_to_non_negative_int(i64::from(self.supply[tc]) + arrived);
    }

    /// A destination price in the origin's coin after the exchange fee.
    fn in_origin_coin(&self, from: i32, to: i32, price: i64) -> i64 {
        let (from_coin, to_coin) = (self.continent[u(from)], self.continent[u(to)]);
        if from_coin == to_coin {
            return price;
        }
        let value = Fx::mul_int_raw(
            price,
            self.coin_rate[u(to_coin * self.continent_count + from_coin)],
        );
        value - Fx::mul_int_raw(value, self.exchange_fee_raw)
    }

    fn pay_in_destination_coin(&self, from: i32, to: i32, buy: i64) -> i64 {
        let (from_coin, to_coin) = (self.continent[u(from)], self.continent[u(to)]);
        if from_coin == to_coin {
            return buy;
        }
        let pay = Fx::mul_int_raw(
            buy,
            self.coin_rate[u(from_coin * self.continent_count + to_coin)],
        );
        pay + Fx::mul_int_raw(pay, self.exchange_fee_raw)
    }

    fn toll_rate(&self, to: i32, good: i32) -> i64 {
        let mut rate = self.tariff[u(to)];
        let category = self.good_category[u(good)];
        if self.category_specialty[u(to * self.category_count + category)] != 0 {
            rate += self.surcharge[u(to)];
        }
        rate
    }

    fn flow_quantity(
        &self,
        from: i32,
        from_cell: i32,
        to: i32,
        sell: i64,
        margin_raw: i64,
        budget: i64,
    ) -> i64 {
        let good = self.cell_good[u(from_cell)];
        let stock = i64::from(self.stock[u(from_cell)]);
        let share = Fx::round_shift(self.flow_share_raw.wrapping_mul(self.spend_raw[u(from)]));
        let mut quantity = Fx::mul_int_raw(stock, share) * margin_raw / (sell << Fx::FRACTION_BITS);
        let row =
            self.license_row[u(to)] * self.base_good_count + good / QualityGrade::COUNT as i32;
        if self.licensed[u(row)] != 0 {
            quantity = Fx::mul_int_raw(quantity, self.licensed_share_raw);
        }
        let weight = i64::from(self.good_weight[u(good)]);
        if weight > 0 {
            quantity = quantity.min(budget / weight);
        }
        if self.continent[u(from)] != self.continent[u(to)] {
            quantity = Fx::mul_int_raw(quantity, self.cross_flow_raw);
        }
        quantity.min(stock) // good vehicles can ask for more than there is
    }

    fn within_coins(&self, from: i32, to: i32, quantity: i64, buy: i64, pay: i64) -> i64 {
        let mut quantity = quantity;
        if self.continent[u(from)] != self.continent[u(to)] && buy > 0 {
            let bank = self.international[u(self.continent[u(from)])].max(0);
            quantity = quantity.min(bank / buy);
        }
        let payer = self.node_float[u(to)];
        if payer != self.node_float[u(from)] && pay > 0 {
            quantity = quantity.min(self.float[u(payer)].max(0) / pay);
        }
        quantity
    }

    fn settle(&mut self, from: i32, to: i32, quantity: i64, buy: i64, pay: i64) {
        let (payer, payee) = (u(self.node_float[u(to)]), u(self.node_float[u(from)]));
        if payer != payee {
            self.float[payer] -= quantity * pay;
            self.float[payee] += quantity * buy;
            self.imports[payer] += quantity * pay;
        }
        let (from_coin, to_coin) = (self.continent[u(from)], self.continent[u(to)]);
        if from_coin == to_coin {
            return;
        }
        self.international[u(to_coin)] += quantity * pay;
        self.international[u(from_coin)] -= quantity * buy;
    }

    fn pay_wage(&mut self, from: i32, to: i32, wage: i64) {
        let cross = self.continent[u(from)] != self.continent[u(to)];
        let purse = u(if cross {
            self.node_float[u(from)]
        } else {
            self.node_float[u(to)]
        });
        let wage = if wage > self.float[purse] {
            self.float[purse].max(0)
        } else {
            wage
        };
        self.float[purse] -= wage;
        self.household_cash[u(from)] += wage;
    }
}
