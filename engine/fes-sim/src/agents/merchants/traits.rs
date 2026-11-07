//! Merchant personalities and occupations (C# `MerchantTraits.cs`: `MerchantTraits`, `NodeMemory`,
//! `MerchantTraitTable`).
use super::{NodeMemory, TRAIT_COUNT};
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;

/// Personalities per trait row (C# `MerchantTraits.Personalities`).
pub const PERSONALITIES: i32 = 3;
pub const CAUTIOUS: i32 = 0;
pub const NORMAL: i32 = 1;
pub const AGGRESSIVE: i32 = 2;
pub const LOCAL: i32 = 0;
pub const EXPEDITION: i32 = 1;
pub const PEDDLER: i32 = 2;

/// C# `MerchantTraits.Of`.
pub fn trait_of(personality: i32, occupation: i32) -> u8 {
    (personality * PERSONALITIES + occupation) as u8
}

impl NodeMemory {
    /// Number of nodes remembered (C# `NodeMemory.Slots`).
    pub const SLOTS: usize = 4;

    pub fn knows(&self, node: i32) -> bool {
        self.slots.contains(&node)
    }

    /// Remembers a node, replacing the oldest; known and negative nodes change nothing.
    pub fn learn(&mut self, node: i32) {
        if node < 0 || self.knows(node) {
            return;
        }
        self.slots[self.next as usize] = node;
        self.next = (self.next + 1) % Self::SLOTS as i32;
    }
}

/// Per-trait behaviour constants, raw Q16; all neutral when traits are off.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MerchantTraitTable {
    /// Share of its cash a group risks on one load.
    pub spend_raw: Vec<i64>,
    /// Margin, as a share of the buying price, below which a group does not trade.
    pub min_margin_raw: Vec<i64>,
    pub capacity_raw: Vec<i64>,
    pub upkeep_raw: Vec<i64>,
    /// A load's profit is divided by the route's travel turns to this power when choosing.
    pub distance_power: Vec<i32>,
    /// The world level whose ancestor of the group's home bounds where it trades.
    pub range_level: Vec<i32>,
    /// How many times a loss weighs against the same gain (DEC-099).
    pub loss_aversion: Vec<i32>,
}

impl MerchantTraitTable {
    pub fn new(config: &SimulationConfig) -> Self {
        let n = TRAIT_COUNT as usize;
        let mut table = Self {
            spend_raw: vec![0; n],
            min_margin_raw: vec![0; n],
            capacity_raw: vec![0; n],
            upkeep_raw: vec![0; n],
            distance_power: vec![0; n],
            range_level: vec![0; n],
            loss_aversion: vec![0; n],
        };
        for t in 0..n {
            table.fill(t, config);
        }
        table
    }

    fn fill(&mut self, t: usize, config: &SimulationConfig) {
        let personality = t / PERSONALITIES as usize;
        let occupation = t % PERSONALITIES as usize;
        let on = config.merchant_traits_enabled;
        let pick = |value: Fx, neutral: i64| if on { value.raw() } else { neutral };
        self.spend_raw[t] = pick(config.personality_spend[personality], Fx::ONE_RAW);
        self.min_margin_raw[t] = pick(config.personality_min_margin[personality], 0);
        self.capacity_raw[t] = pick(config.occupation_capacity[occupation], Fx::ONE_RAW);
        self.upkeep_raw[t] = pick(config.occupation_upkeep[occupation], Fx::ONE_RAW);
        self.distance_power[t] = if on {
            config.occupation_distance_power[occupation]
        } else {
            0
        };
        self.range_level[t] = if on {
            config.personality_range[personality] as i32
        } else {
            WorldLevel::Continent as i32
        };
        let aversion = config.personality_loss_aversion[personality];
        self.loss_aversion[t] = if on && aversion > 0 {
            aversion
        } else {
            config.merchant_risk_aversion
        };
    }
}
