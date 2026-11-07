//! C# `DetailedMerchantJob` (`MerchantJob.cs`): every merchant group of a detailed node, every
//! group based at one and every group on a trip, in row order (SYS-MERCHANTS, DEC-003). The Burst
//! job runs here as a plain loop. Tolls and bankruptcies wait in the scratch for the step after the
//! job; cargo rows emptied here keep a zero quantity until the job ends.
use super::traits::MerchantTraitTable;
use super::NodeMemory;
use crate::agents::facilities::{MODE_AUTOMATED, OWNER_PLAYER};
use fes_core::{DetRandom, Fx, SimDetail};

/// Index of `TripStats[8]`, the coins from sales, which `MustAct` compares before and after `Act`.
pub const SOLD: usize = 8;
/// Number of `TripStats` counters.
pub const TRIP_STATS: usize = 21;

#[inline]
pub(super) fn u(i: i32) -> usize {
    i as usize
}

/// The job's scalar rules (C# job fields set in `SetRules` and `SetTraits`).
#[derive(Clone, Debug, Default)]
pub struct JobRules {
    pub seed: u64,
    pub turn: i32,
    pub encounter_stream: u32,
    pub license_stream: u32,
    pub licensed_permille: i32,
    pub international_stream: u32,
    pub international_permille: i32,
    pub intercontinental_stream: u32,
    pub intercontinental_permille: i32,
    pub base_good_count: i32,
    pub category_count: i32,
    pub bankrupt_weeks: i32,
    pub reserve_upkeep_weeks: i32,
    pub merchant_holder_base: i32,
    pub cash_loss_raw: i64,
    pub cargo_loss_raw: i64,
    pub danger_value_scale: i64,
    pub rumors: bool,
    pub weigh_sales: bool,
    pub judge_hops: i32,
    pub bids: bool,
    pub bids_while_loaded: bool,
    pub return_home: bool,
    pub cross_level: i32,
    pub continent_count: i32,
    pub exchange_fee_raw: i64,
    pub spread_raw: i64,
    pub judge_coin_units: i32,
    pub bill_discount_raw: i64,
    pub bill_weeks: i32,
    pub guard_base_cost: i64,
    pub member_premium_raw: i64,
    pub count_trip_costs: bool,
    pub must_act: bool,
    pub hedge_floor_raw: i64,
    pub hedge_fee_raw: i64,
    pub hedge_fall_raw: i64,
    pub hedge_chance_raw: i64,
    pub guard_cost_share_raw: i64,
    pub guard_reduction_raw: i64,
    pub insurance_premium_raw: i64,
    pub insurance_refund_raw: i64,
}

/// Read-only node, route and market columns (C# `SetPlaces`).
pub struct JobPlaces<'a> {
    pub detail: &'a [u8],
    pub continent: &'a [i32],
    pub parent: &'a [i32],
    pub level: &'a [u8],
    pub core_strength: &'a [i32],
    pub category_specialty: &'a [u8],
    pub tariff: &'a [i64],
    pub surcharge: &'a [i64],
    pub license_row: &'a [i32],
    pub licensed: &'a [u8],
    pub node_float: &'a [i32],
    pub continent_index: &'a [i64],
    pub node_port: &'a [u8],
    pub route_toward: &'a [i32],
    pub node_insurer: &'a [i32],
    pub coin_rate: &'a [i64],
    pub out_start: &'a [i32],
    pub out_count: &'a [i32],
    pub route_index: &'a [i32],
    pub route_to: &'a [i32],
    pub route_turns: &'a [i32],
    pub route_base_fee: &'a [i32],
    pub route_cost_raw: &'a [i64],
    pub route_danger_raw: &'a [i64],
    pub cell_start: &'a [i32],
    pub cell_count: &'a [i32],
    pub cell_good: &'a [i32],
    pub good_base_price: &'a [i32],
    pub good_weight: &'a [i32],
    pub good_size: &'a [i32],
    pub good_category: &'a [i32],
    pub price: &'a [i32],
}

/// The capacity and upkeep of a group based at a level (C# `MerchantLevelLimits`).
#[derive(Clone, Copy, Debug, Default)]
pub struct LevelLimits {
    pub max_weight: i64,
    pub max_size: i64,
    pub upkeep: i64,
}

/// Read-only group columns and per-trait and per-vehicle tables (C# `SetTraits`, `SetGroups`).
pub struct JobGroups<'a> {
    pub home: &'a [i32],
    pub owner: &'a [i32],
    pub association: &'a [i32],
    pub mode: &'a [u8],
    pub scale: &'a [u8],
    pub trait_id: &'a [u8],
    pub vehicle: &'a [u8],
    pub traits: &'a MerchantTraitTable,
    pub levels: &'a [LevelLimits],
    pub vehicle_capacity: &'a [i64],
    pub vehicle_cost: &'a [i64],
    pub vehicle_danger: &'a [i64],
    pub vehicle_premium: &'a [i64],
}

/// The columns the job writes, moved out of the world for the job and back after it.
#[derive(Default)]
pub struct JobState {
    pub memory: Vec<NodeMemory>,
    pub node: Vec<i32>,
    pub destination: Vec<i32>,
    pub arrival_turn: Vec<i32>,
    pub negative_weeks: Vec<i32>,
    pub cash: Vec<i64>,
    pub load_cost: Vec<i64>,
    pub hedge: Vec<i64>,
    pub stock: Vec<i32>,
    pub supply: Vec<i32>,
    pub demand: Vec<i32>,
    pub input_bid: Vec<i32>,
    pub float: Vec<i64>,
    pub imports: Vec<i64>,
    pub household_cash: Vec<i64>,
    pub home_grown: Vec<i32>,
    pub group_cash: Vec<i64>,
    pub cargo_holder: Vec<i32>,
    pub cargo_good: Vec<i32>,
    pub cargo_quantity: Vec<i32>,
    pub bill_holder: Vec<i32>,
    pub bill_purse: Vec<i32>,
    pub bill_face: Vec<i64>,
    pub bill_due: Vec<i32>,
    pub bill_room: Vec<i64>,
    pub bill_bank_room: Vec<i64>,
    pub bill_bank_spent: Vec<i64>,
    pub international: Vec<i64>,
}

/// The job's scratch and its outputs for the step after it; kept by the system between turns.
#[derive(Default)]
pub struct Scratch {
    pub first_row: Vec<i32>,
    pub next_row: Vec<i32>,
    pub rows: Vec<i32>,
    pub board: Vec<NodeMemory>,
    pub stocked_start: Vec<i32>,
    pub stocked_count: Vec<i32>,
    pub stocked_cells: Vec<i32>,
    pub bid_best: Vec<i64>,
    pub bid_linked: Vec<bool>,
    pub bid_bound: Vec<i64>,
    pub bid_top_price: i64,
    pub cell_bits: Vec<u64>,
    pub cells_before: Vec<i32>,
    pub household_need: Vec<i64>,
    pub hedge_room: Vec<i64>,
    pub hedge_net: Vec<i64>,
    pub tolls_by_node: Vec<i64>,
    pub trip_stats: [i64; TRIP_STATS],
    pub bill_stats: [i64; 2],
    pub bankrupt: Vec<i32>,
}

/// One run of the detailed merchant job.
pub struct DetailedJob<'a> {
    pub r: &'a JobRules,
    pub p: &'a JobPlaces<'a>,
    pub g: &'a JobGroups<'a>,
    pub s: &'a mut JobState,
    pub x: &'a mut Scratch,
}

impl DetailedJob<'_> {
    /// C# `Execute`.
    pub fn execute(&mut self) {
        self.reset_scratch();
        for a in 0..self.s.node.len() {
            if self.is_stepped(a) {
                self.step(a);
            }
        }
        self.remove_empty_rows();
    }

    fn reset_scratch(&mut self) {
        self.x.stocked_start.fill(-1);
        self.index_cells();
        self.x.stocked_cells.clear();
        if self.r.bids || self.r.bids_while_loaded {
            self.prepare_bid_bounds();
        }
        self.x.first_row.fill(-1);
        self.x.next_row.clear();
        let groups = self.x.first_row.len() as i32;
        for r in 0..self.s.cargo_holder.len() {
            let a = self.s.cargo_holder[r].wrapping_sub(self.r.merchant_holder_base);
            let merchant = a >= 0 && a < groups;
            self.x
                .next_row
                .push(if merchant { self.x.first_row[u(a)] } else { -1 });
            if merchant {
                self.x.first_row[u(a)] = r as i32;
            }
        }
        if self.r.rumors {
            self.x.board.fill(NodeMemory::EMPTY);
        }
    }

    fn is_stepped(&self, a: usize) -> bool {
        let detailed = SimDetail::Detailed as u8;
        if self.s.destination[a] < 0
            && self.p.detail[u(self.s.node[a])] != detailed
            && self.p.detail[u(self.g.home[a])] != detailed
        {
            return false;
        }
        self.g.mode[a] == MODE_AUTOMATED
    }

    fn step(&mut self, a: usize) {
        if self.s.destination[a] >= 0 && self.r.turn >= self.s.arrival_turn[a] {
            self.arrive(a);
        }
        if self.s.destination[a] < 0 {
            self.act(a);
        }
        self.pay_upkeep(a);
    }

    fn act(&mut self, a: usize) {
        if self.r.rumors {
            self.listen(a);
        }
        let sold_before = self.x.trip_stats[SOLD];
        let node = self.s.node[a];
        let carry_on = self.sell_or_carry(a, node);
        if carry_on >= 0 {
            self.depart(a, node, carry_on);
        } else {
            self.try_start_trip(a);
        }
        if self.r.must_act && self.s.destination[a] < 0 && self.x.trip_stats[SOLD] == sold_before {
            self.set_off(a, self.s.node[a]);
        }
    }

    /// Rows emptied during the job, removed backwards with swap-back (row order is state).
    fn remove_empty_rows(&mut self) {
        for r in (0..self.s.cargo_quantity.len()).rev() {
            if self.s.cargo_quantity[r] != 0 {
                continue;
            }
            self.s.cargo_holder.swap_remove(r);
            self.s.cargo_good.swap_remove(r);
            self.s.cargo_quantity.swap_remove(r);
        }
    }

    /// What the group knows of its node, and what others here know, passes around.
    fn listen(&mut self, a: usize) {
        let node = self.s.node[a];
        let mut memory = self.s.memory[a];
        memory.learn(node);
        let mut board = self.x.board[u(node)];
        for i in 0..NodeMemory::SLOTS {
            memory.learn(board.slots[i]);
        }
        for i in 0..NodeMemory::SLOTS {
            board.learn(memory.slots[i]);
        }
        self.s.memory[a] = memory;
        self.x.board[u(node)] = board;
    }

    /// The group's cargo rows with goods in them, sorted by good (insertion sort, as C#).
    pub(super) fn collect_rows(&mut self, a: usize) {
        self.x.rows.clear();
        let mut r = self.x.first_row[a];
        while r >= 0 {
            if self.s.cargo_quantity[u(r)] > 0 {
                self.insert_row(r);
            }
            r = self.x.next_row[u(r)];
        }
    }

    /// Inserts a row into the collected rows, keeping them sorted by good.
    fn insert_row(&mut self, r: i32) {
        let good = self.s.cargo_good[u(r)];
        let mut at = self.x.rows.len();
        self.x.rows.push(r);
        while at > 0 && self.s.cargo_good[u(self.x.rows[at - 1])] > good {
            self.x.rows[at] = self.x.rows[at - 1];
            at -= 1;
        }
        self.x.rows[at] = r;
    }

    fn arrive(&mut self, a: usize) {
        let node = self.s.destination[a];
        self.s.node[a] = node;
        self.s.destination[a] = -1;
        self.s.arrival_turn[a] = -1;
        if self.r.rumors || self.r.must_act {
            self.s.memory[a].learn(node);
        }
    }

    pub(super) fn has_cargo(&self, a: usize) -> bool {
        let mut r = self.x.first_row[a];
        while r >= 0 {
            if self.s.cargo_quantity[u(r)] > 0 {
                return true;
            }
            r = self.x.next_row[u(r)];
        }
        false
    }

    pub(super) fn add_cargo(&mut self, a: usize, good: i32, quantity: i32) {
        let mut r = self.x.first_row[a];
        while r >= 0 {
            if self.s.cargo_good[u(r)] == good {
                self.s.cargo_quantity[u(r)] += quantity;
                return;
            }
            r = self.x.next_row[u(r)];
        }
        self.s
            .cargo_holder
            .push(self.r.merchant_holder_base + a as i32);
        self.s.cargo_good.push(good);
        self.s.cargo_quantity.push(quantity);
        self.x.next_row.push(self.x.first_row[a]);
        self.x.first_row[a] = self.s.cargo_quantity.len() as i32 - 1;
    }

    /// Upkeep can push cash below zero; enough such weeks and a new group replaces this one.
    fn pay_upkeep(&mut self, a: usize) {
        let upkeep = self.upkeep(a);
        self.s.cash[a] -= upkeep;
        self.x.trip_stats[14] += upkeep;
        let node = self.s.node[a];
        let local = self.pay_there(a, node, upkeep);
        self.s.household_cash[u(node)] += local;
        if self.s.cash[a] >= 0 {
            self.s.negative_weeks[a] = 0;
            return;
        }
        self.s.negative_weeks[a] += 1;
        if self.s.negative_weeks[a] >= self.r.bankrupt_weeks {
            self.x.bankrupt.push(a as i32);
        }
    }

    pub(super) fn upkeep(&self, a: usize) -> i64 {
        let level = self.g.levels[usize::from(self.g.scale[a])].upkeep;
        let traited = Fx::mul_int_raw(
            level,
            self.g.traits.upkeep_raw[usize::from(self.g.trait_id[a])],
        );
        Fx::mul_int_raw(traited, self.g.vehicle_cost[usize::from(self.g.vehicle[a])])
    }

    /// The home's ancestor at the personality's level, or the home itself above that level.
    pub(super) fn range_anchor(&self, a: usize) -> i32 {
        let level = self.g.traits.range_level[usize::from(self.g.trait_id[a])];
        let mut m = self.g.home[a];
        while i32::from(self.p.level[u(m)]) > level {
            m = self.p.parent[u(m)];
        }
        m
    }

    pub(super) fn within(&self, node: i32, anchor: i32) -> bool {
        let mut m = node;
        while m >= 0 {
            if m == anchor {
                return true;
            }
            m = self.p.parent[u(m)];
        }
        false
    }

    pub(super) fn holds_licenses(&self, a: usize) -> bool {
        DetRandom::for_key(self.r.seed, 0, self.r.license_stream, a as i32, 0)
            .chance_permille(self.r.licensed_permille)
    }

    /// Going home needs no license, so a group caught abroad is never stranded (DEC-116).
    pub(super) fn holds_trade_license(&self, a: usize, stream: u32, permille: i32) -> bool {
        DetRandom::for_key(self.r.seed, 0, stream, a as i32, 0).chance_permille(permille)
    }

    pub(super) fn needs_license(&self, node: i32, good: i32) -> bool {
        let row = self.p.license_row[u(node)] * self.r.base_good_count;
        self.p.licensed[u(row + good / fes_core::QualityGrade::COUNT as i32)] != 0
    }

    pub(super) fn toll_rate(&self, node: i32, good: i32) -> i64 {
        let mut rate = self.p.tariff[u(node)];
        let category = self.p.good_category[u(good)];
        if self.p.category_specialty[u(node * self.r.category_count + category)] != 0 {
            rate += self.p.surcharge[u(node)];
        }
        rate
    }

    pub(super) fn find_nearest_cell(&self, node: i32, good: i32) -> i32 {
        let mut m = node;
        while m >= 0 {
            let cell = self.find_cell(m, good);
            if cell >= 0 {
                return cell;
            }
            m = self.p.parent[u(m)];
        }
        -1
    }

    /// A market's cells run in the order of their goods, so a cell's place is how many of its
    /// goods come before.
    pub(super) fn find_cell(&self, market: i32, good: i32) -> i32 {
        let words = self.x.cell_bits.len() / self.p.cell_start.len();
        let word = u(market) * words + u(good >> 6);
        let bits = self.x.cell_bits[word];
        let bit = 1u64 << (good & 63);
        if bits & bit == 0 {
            return -1;
        }
        self.p.cell_start[u(market)]
            + self.x.cells_before[word]
            + (bits & (bit - 1)).count_ones() as i32
    }

    fn index_cells(&mut self) {
        let markets = self.p.cell_start.len();
        let words = self.x.cell_bits.len() / markets;
        self.x.cell_bits.fill(0);
        for market in 0..markets {
            let start = u(self.p.cell_start[market]);
            let end = start + u(self.p.cell_count[market]);
            let row = market * words;
            for c in start..end {
                let good = self.p.cell_good[c];
                self.x.cell_bits[row + u(good >> 6)] |= 1u64 << (good & 63);
            }
            let mut before = 0;
            for w in 0..words {
                self.x.cells_before[row + w] = before;
                before += self.x.cell_bits[row + w].count_ones() as i32;
            }
        }
    }

    /// The player's groups deal in coins: only bonds are open to the player (DEC-102).
    pub(super) fn no_bills(&self, a: usize) -> bool {
        self.g.owner[a] == OWNER_PLAYER
    }
}
