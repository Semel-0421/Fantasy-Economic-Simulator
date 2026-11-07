//! Household consumption per market cell, shared by the repricing pass of markets that reprice
//! every turn and by C# `ConsumptionJob` for the others (ported as a plain loop over markets in
//! order).
use super::demand::{cell_base, weekly, DemandParams};
use crate::economy::markets::MODIFIER_TARGETS;
use crate::economy::{HouseholdCash, MarketStore};
use fes_core::Fx;

/// C# `ModifierTarget.Value` and `ModifierTarget.Demand`.
const TARGET_VALUE: usize = 2;
const TARGET_DEMAND: usize = 1;

/// Per-turn numbers the pricing jobs read (C# `PricingSystem` scratch arrays and job constants).
pub struct Scratch<'a> {
    pub demand: DemandParams,
    pub category_count: usize,
    pub specialty_anchor_mul_raw: i64,
    pub turn: i32,
    pub need_memory_weeks: i32,
    /// Per node: level.
    pub level: &'a [u8],
    /// Per node: currency price index of its continent (raw Q16).
    pub anchor_mul: Vec<i64>,
    pub necessity_weight: Vec<i64>,
    pub luxury_weight: Vec<i64>,
    pub budget: Vec<i64>,
    pub luxury_budget: Vec<i64>,
    /// Per good: how far it is bought like a luxury (raw Q16).
    pub good_luxury_share: Vec<i64>,
    /// Per level demand weight (raw Q16).
    pub level_weight: Vec<i64>,
    /// `MarketFlows::multipliers`.
    pub modifier_mul: &'a [i64],
}

/// The mutable state a pricing job works on.
pub struct Books<'a> {
    pub markets: &'a mut MarketStore,
    pub cash: &'a mut HouseholdCash,
    pub necessity_wanted: &'a mut [i64],
    pub necessity_met: &'a mut [i64],
}

/// One cell's consumption, as the market tallies it.
#[derive(Default)]
pub struct Tally {
    pub wanted: i64,
    pub met: i64,
    pub desired: i64,
    pub desired_necessity: i64,
}

/// C# `PriceUpdateJob.Supplied`.
fn supplied(met: i64, supply: i32, stock: i32, demand: i32) -> i64 {
    let left = i64::from(supply) + i64::from(stock) - i64::from(demand);
    if left <= 0 {
        0
    } else {
        left.min(met)
    }
}

/// C# `PriceUpdateJob.Deliverable`.
fn deliverable(wished: i64, supply: i32, stock: i32, demand: i32) -> i64 {
    let left = i64::from(supply) + i64::from(stock) - i64::from(demand);
    if left < 0 {
        0
    } else {
        left.min(wished)
    }
}

impl Scratch<'_> {
    fn modifier_base(&self, market: usize, good: usize, markets: &MarketStore) -> usize {
        (market * self.category_count + markets.good_category[good] as usize) * MODIFIER_TARGETS
    }

    /// The cell's anchor multiplier: value modifier, price index and specialty.
    pub fn value_mul(&self, market: usize, c: usize, markets: &MarketStore) -> i64 {
        let good = markets.cell_good[c] as usize;
        let k = self.modifier_base(market, good, markets);
        let mut value_mul = Fx::round_shift(
            self.modifier_mul[k + TARGET_VALUE].wrapping_mul(self.anchor_mul[market]),
        );
        if markets.cell_specialty[c] != 0 {
            value_mul = Fx::round_shift(value_mul.wrapping_mul(self.specialty_anchor_mul_raw));
        }
        value_mul
    }

    /// Adds this turn's household consumption to the cell's demand and returns the units consumed
    /// (unclamped); the caller decides whether they wait in `pending_units`.
    pub fn consume_cell(
        &self,
        market: usize,
        c: usize,
        value_mul: i64,
        b: &mut Books,
        tally: &mut Tally,
    ) -> i64 {
        let m = &mut *b.markets;
        let cash = &mut *b.cash;
        let good = m.cell_good[c] as usize;
        let k = self.modifier_base(market, good, m);
        let level = usize::from(self.level[market]);
        let (weekly_base, anchor) = cell_base(
            m.good_base_demand[good],
            m.good_base_price[good],
            self.level_weight[level],
            self.modifier_mul[k + TARGET_DEMAND],
            value_mul,
        );
        let price = m.price[c];
        let wish = weekly(
            &self.demand,
            weekly_base,
            price,
            anchor,
            self.good_luxury_share[good],
            self.necessity_weight[market],
            self.luxury_weight[market],
        );
        let eaten = i64::from(cash.home_grown[c]).min(wish.met);
        cash.home_grown[c] -= eaten as i32;
        let to_buy = wish.met - eaten;
        let luxuries = wish.units - wish.met;
        let (supply, stock, demand) = (m.supply[c], m.stock[c], m.demand[c]);
        let p = i64::from(price);
        tally.desired += deliverable(to_buy + luxuries, supply, stock, demand).wrapping_mul(p);
        tally.desired_necessity += deliverable(to_buy, supply, stock, demand).wrapping_mul(p);
        let cell_met = Fx::round_shift(to_buy.wrapping_mul(self.budget[market]));
        let consumed =
            cell_met + Fx::round_shift(luxuries.wrapping_mul(self.luxury_budget[market]));
        if i64::from(supply) + i64::from(stock) + eaten > 0 {
            cash.last_offered[c] = self.turn;
        }
        if cash.last_offered[c] >= self.turn - self.need_memory_weeks {
            tally.wanted += wish.wanted;
            tally.met += eaten + supplied(cell_met, supply, stock, demand);
        }
        m.demand[c] = MarketStore::clamp_to_non_negative_int(i64::from(demand) + consumed);
        consumed
    }

    /// C# `ConsumptionJob`: consumption for markets of the levels in `level_mask`, paid for when
    /// the market reprices.
    pub fn consume(&self, level_mask: i32, b: &mut Books) {
        for market in 0..b.markets.market_count() {
            let level = i32::from(self.level[market]);
            if level_mask & (1 << level) == 0 {
                continue;
            }
            let start = b.markets.market_cell_start[market] as usize;
            let end = start + b.markets.market_cell_count[market] as usize;
            let mut tally = Tally::default();
            for c in start..end {
                let value_mul = self.value_mul(market, c, b.markets);
                let consumed = self.consume_cell(market, c, value_mul, b, &mut tally);
                b.cash.pending_units[c] = MarketStore::clamp_to_non_negative_int(
                    i64::from(b.cash.pending_units[c]) + consumed,
                );
            }
            b.necessity_wanted[market] += tally.wanted;
            b.necessity_met[market] += tally.met;
            b.cash.desired[market] = tally.desired;
            b.cash.desired_necessity[market] = tally.desired_necessity;
        }
    }
}
