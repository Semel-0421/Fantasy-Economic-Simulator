//! The price step of one market cell (C# `PriceParams`, `PriceCell`, `PriceKernel`) and C#
//! `PriceUpdateJob`, ported as a plain loop over markets in order.
use super::consumption::{Books, Scratch, Tally};
use crate::economy::MarketStore;
use fes_core::Fx;
use fes_data::SimulationConfig;

/// Pricing constants in raw Q16.
#[derive(Clone, Copy, Debug)]
pub struct PriceParams {
    pub adjust_raw: i64,
    pub max_step_raw: i64,
    pub anchor_raw: i64,
    pub stock_pressure_raw: i64,
    pub min_price: i32,
}

impl PriceParams {
    pub fn from_config(c: &SimulationConfig) -> Self {
        Self {
            adjust_raw: c.price_adjust_rate.raw(),
            max_step_raw: c.price_max_step.raw(),
            anchor_raw: c.price_anchor_rate.raw(),
            stock_pressure_raw: c.stock_pressure.raw(),
            min_price: c.min_price,
        }
    }
}

/// One market cell's numbers, as a price step takes and leaves them.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct PriceCell {
    pub price: i32,
    pub prev_price: i32,
    pub stock: i32,
    pub supply: i32,
    pub demand: i32,
}

/// C# `PriceKernel.Step`: one market cell's price step, integer-only with symmetric rounding.
pub fn step(
    p: &PriceParams,
    base_price: i32,
    anchor_mul_raw: i64,
    elapsed_turns: i32,
    cell: &mut PriceCell,
    input_bid: i32,
) {
    let s = i64::from(cell.supply);
    let d = i64::from(cell.demand);
    let st = i64::from(cell.stock);
    let elapsed = i64::from(elapsed_turns);

    // Pressure in [-1, 1]: positive when demand outruns supply (stock counts partially as supply).
    let effective_supply = s + Fx::round_shift(st.wrapping_mul(p.stock_pressure_raw));
    let wanted = d + i64::from(input_bid);
    let total = wanted + effective_supply;
    let pressure_raw = if total == 0 {
        0
    } else {
        ((wanted - effective_supply) << Fx::FRACTION_BITS) / total
    };

    let max_step = p.max_step_raw.wrapping_mul(elapsed);
    let mut step_raw =
        Fx::round_shift(pressure_raw.wrapping_mul(p.adjust_raw)).wrapping_mul(elapsed);
    if step_raw > max_step {
        step_raw = max_step;
    }
    if step_raw < -max_step {
        step_raw = -max_step;
    }

    // Pull toward the anchor; at least one coin per update unless the market presses the other way.
    let anchor = Fx::round_shift(i64::from(base_price).wrapping_mul(anchor_mul_raw));
    let current = i64::from(cell.price);
    let gap = anchor - current;
    let mut pull = Fx::round_shift(gap.wrapping_mul(p.anchor_raw).wrapping_mul(elapsed));
    if pull == 0
        && gap != 0
        && p.anchor_raw != 0
        && (pressure_raw == 0 || (pressure_raw > 0) == (gap > 0))
    {
        pull = if gap > 0 { 1 } else { -1 };
    }

    let mut next = current + Fx::round_shift(current.wrapping_mul(step_raw)) + pull;
    if next < i64::from(p.min_price) {
        next = i64::from(p.min_price);
    }
    if next > i64::from(i32::MAX) {
        next = i64::from(i32::MAX);
    }

    let available = s + st;
    let traded = d.min(available);
    let remaining = available - traded;

    cell.prev_price = cell.price;
    cell.price = next as i32;
    cell.stock = remaining.min(i64::from(i32::MAX)) as i32;
    cell.supply = 0;
    cell.demand = 0;
}

/// What the market delivers of `pending` household units, at its price: every other unit of demand
/// is a purchase already taken from the stock.
fn delivered_value(m: &MarketStore, c: usize, pending: i32) -> i64 {
    let others = (i64::from(m.demand[c]) - i64::from(pending)).max(0);
    let delivered =
        (i64::from(m.supply[c]) + i64::from(m.stock[c]) - others).min(i64::from(pending));
    if delivered > 0 {
        delivered.wrapping_mul(i64::from(m.price[c]))
    } else {
        0
    }
}

/// C# `PriceUpdateJob` constants.
pub struct Reprice {
    pub params: PriceParams,
    /// Bit l set = markets of level l are due.
    pub due_level_mask: i32,
    /// Bit l set = markets of level l reprice every turn and consume here.
    pub inline_level_mask: i32,
    /// Per level: turns since that level last repriced.
    pub level_elapsed: Vec<i32>,
}

impl Reprice {
    /// Steps every cell of every market whose level is due, paying for the households' units.
    pub fn run(&self, s: &Scratch<'_>, b: &mut Books) {
        for market in 0..b.markets.market_count() {
            self.execute(s, market, b);
        }
    }

    fn execute(&self, s: &Scratch<'_>, market: usize, b: &mut Books) {
        let level = usize::from(s.level[market]);
        if self.due_level_mask & (1 << level) == 0 {
            return;
        }
        let start = b.markets.market_cell_start[market] as usize;
        let end = start + b.markets.market_cell_count[market] as usize;
        let inline = self.inline_level_mask & (1 << level) != 0;
        let mut tally = Tally::default();
        let mut spent = 0i64;
        for c in start..end {
            spent += self.step_cell(s, market, c, level, inline, b, &mut tally);
        }
        b.necessity_wanted[market] += tally.wanted;
        b.necessity_met[market] += tally.met;
        if inline {
            b.cash.desired[market] = tally.desired;
            b.cash.desired_necessity[market] = tally.desired_necessity;
        }
        // Households never pay more than they hold.
        let cash = b.cash.cash[market];
        if spent > cash {
            spent = cash.max(0);
        }
        b.cash.cash[market] = cash - spent;
        b.cash.spent[market] += spent;
    }

    /// Steps one cell; returns what its households paid.
    #[allow(clippy::too_many_arguments)]
    fn step_cell(
        &self,
        s: &Scratch<'_>,
        market: usize,
        c: usize,
        level: usize,
        inline: bool,
        b: &mut Books,
        tally: &mut Tally,
    ) -> i64 {
        let value_mul = s.value_mul(market, c, b.markets);
        let pending = if inline {
            let consumed = s.consume_cell(market, c, value_mul, b, tally);
            MarketStore::clamp_to_non_negative_int(consumed)
        } else {
            let pending = b.cash.pending_units[c];
            if pending > 0 {
                b.cash.pending_units[c] = 0;
            }
            pending
        };
        let paid = if pending > 0 {
            delivered_value(b.markets, c, pending)
        } else {
            0
        };
        let m = &mut *b.markets;
        let mut cell = PriceCell {
            price: m.price[c],
            prev_price: m.prev_price[c],
            stock: m.stock[c],
            supply: m.supply[c],
            demand: m.demand[c],
        };
        let base_price = m.good_base_price[m.cell_good[c] as usize];
        step(
            &self.params,
            base_price,
            value_mul,
            self.level_elapsed[level],
            &mut cell,
            m.input_bid[c],
        );
        m.input_bid[c] = 0;
        m.price[c] = cell.price;
        m.prev_price[c] = cell.prev_price;
        m.stock[c] = cell.stock;
        m.supply[c] = cell.supply;
        m.demand[c] = cell.demand;
        paid
    }
}
