//! Sparse structure-of-arrays market state (C# `MarketStore`, section `economy.markets`, version 2)
//! and the per-market multiplier table (C# `MarketFlows`, not saved).
use super::{read_i32s, write_i32s};
use crate::world::{Component, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

/// Market (= node) cells: market `m` owns cells `market_cell_start[m] .. + market_cell_count[m]`,
/// sorted by good id. Supply and demand are accumulators since the market's last price update.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MarketStore {
    // Layout (static, rebuilt from data).
    pub market_cell_start: Vec<i32>,
    pub market_cell_count: Vec<i32>,
    pub cell_good: Vec<i32>,
    /// Per cell: 1 when the good is a specialty of the market's node (anchored lower), else 0.
    pub cell_specialty: Vec<u8>,
    // Per-good static data copied from definitions.
    pub good_base_price: Vec<i32>,
    pub good_base_demand: Vec<i32>,
    pub good_category: Vec<i32>,
    pub good_weight: Vec<i32>,
    pub good_size: Vec<i32>,
    // Dynamic state (saved).
    pub price: Vec<i32>,
    pub prev_price: Vec<i32>,
    pub stock: Vec<i32>,
    pub supply: Vec<i32>,
    pub demand: Vec<i32>,
    /// Per cell: inputs facilities could not buy since the last price update (DEC-078).
    pub input_bid: Vec<i32>,
}

impl MarketStore {
    /// An all-zero store of this shape.
    pub fn new(market_count: usize, cell_count: usize, good_count: usize) -> Self {
        Self {
            market_cell_start: vec![0; market_count],
            market_cell_count: vec![0; market_count],
            cell_good: vec![0; cell_count],
            cell_specialty: vec![0; cell_count],
            good_base_price: vec![0; good_count],
            good_base_demand: vec![0; good_count],
            good_category: vec![0; good_count],
            good_weight: vec![0; good_count],
            good_size: vec![0; good_count],
            price: vec![0; cell_count],
            prev_price: vec![0; cell_count],
            stock: vec![0; cell_count],
            supply: vec![0; cell_count],
            demand: vec![0; cell_count],
            input_bid: vec![0; cell_count],
        }
    }

    pub fn market_count(&self) -> usize {
        self.market_cell_start.len()
    }

    pub fn cell_count(&self) -> usize {
        self.cell_good.len()
    }

    /// The cell of `good` in `market`, by binary search over the market's sorted cells.
    pub fn try_find_cell(&self, market: usize, good: i32) -> Option<usize> {
        let start = self.market_cell_start[market] as usize;
        let end = start + self.market_cell_count[market] as usize;
        self.cell_good[start..end]
            .binary_search(&good)
            .ok()
            .map(|i| start + i)
    }

    pub fn carries(&self, market: usize, good: i32) -> bool {
        self.try_find_cell(market, good).is_some()
    }

    /// Clamps to `0..=i32::MAX` (C# `ClampToNonNegativeInt`).
    pub fn clamp_to_non_negative_int(value: i64) -> i32 {
        value.clamp(0, i64::from(i32::MAX)) as i32
    }
}

impl Component for MarketStore {
    fn key(&self) -> &'static str {
        "economy.markets"
    }

    fn version(&self) -> i32 {
        2
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_i32s(w, &self.price);
        write_i32s(w, &self.prev_price);
        write_i32s(w, &self.stock);
        write_i32s(w, &self.supply);
        write_i32s(w, &self.demand);
        write_i32s(w, &self.input_bid);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_i32s(r, &mut self.price)?;
        read_i32s(r, &mut self.prev_price)?;
        read_i32s(r, &mut self.stock)?;
        read_i32s(r, &mut self.supply)?;
        read_i32s(r, &mut self.demand)?;
        read_i32s(r, &mut self.input_bid)
    }
}

/// Number of `ModifierTarget` values (C# `ModifierTargets.Count`).
pub const MODIFIER_TARGETS: usize = 3;

/// Event-modifier multipliers folded per market, category and target; derived each turn, not saved.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MarketFlows {
    pub category_count: usize,
    /// Raw Q16 multipliers indexed `((market * category_count) + category) * MODIFIER_TARGETS + target`.
    pub multipliers: Vec<i64>,
}

impl MarketFlows {
    pub fn new(nodes: &NodeTable, category_count: usize) -> Self {
        let category_count = category_count.max(1);
        Self {
            category_count,
            multipliers: vec![Fx::ONE_RAW; nodes.count() * category_count * MODIFIER_TARGETS],
        }
    }

    pub fn multiplier_index(&self, market: usize, category: usize, target: usize) -> usize {
        (market * self.category_count + category) * MODIFIER_TARGETS + target
    }
}
