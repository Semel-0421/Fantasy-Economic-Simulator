//! Merchant groups and their turn-0 spawn (C# `AgentTable`, `AgentsModule.SpawnMerchants`,
//! `MerchantCapital`, `MerchantVehicles.AtStart`; section `agents.merchants`).
mod aggregate;
mod bankrupt;
pub mod flow_job;
mod job;
mod job_bids;
mod job_carry;
mod job_coins;
mod job_sales;
mod job_search;
mod job_trip;
mod needs;
mod state;
mod statics;
mod system;
pub mod traits;

pub use bankrupt::MerchantBankrupt;
pub use system::{refresh_tolls, MerchantSystem};
pub use traits::MerchantTraitTable;

use super::columns::{native_table, Elem};
use super::facilities::{MODE_AUTOMATED, OWNER_NPC};
use crate::world::NodeTable;
use fes_core::det::det_hash;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::DetRandom;
use fes_data::SimulationConfig;

/// Number of merchant traits (C# `MerchantTraits.Count`).
pub const TRAIT_COUNT: i32 = 9;
/// Normal personality, expedition occupation (C# `MerchantTraits.Neutral`).
pub const TRAIT_NEUTRAL: u8 = 4;
/// C# `MerchantVehicles.Pack`.
pub const VEHICLE_PACK: u8 = 0;
/// The capital class that starts like the player (C# `MerchantCapital.PlayerLike`).
pub const PLAYER_LIKE: usize = 3;

/// The nodes a group has seen or heard about, at most four (C# `NodeMemory`); saved as five ints.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct NodeMemory {
    pub slots: [i32; 4],
    pub next: i32,
}

impl NodeMemory {
    pub const EMPTY: NodeMemory = NodeMemory {
        slots: [-1; 4],
        next: 0,
    };
}

impl Elem for NodeMemory {
    const SIZE: usize = 20;
    fn put(self, w: &mut DotNetWriter<Vec<u8>>) {
        for s in self.slots {
            let _ = w.write_i32(s);
        }
        let _ = w.write_i32(self.next);
    }
    fn get(r: &mut DotNetReader) -> Result<Self, IoError> {
        let mut slots = [0; 4];
        for s in &mut slots {
            *s = r.read_i32()?;
        }
        Ok(Self {
            slots,
            next: r.read_i32()?,
        })
    }
}

native_table! {
    /// Merchant groups (C# `AgentTable`, version 8).
    AgentTable, "agents.merchants", 8,
    {
        node: i32,
        owner: i32,
        scale: u8,
        mode: u8,
        cash: i64,
        home: i32,
        destination: i32,
        arrival_turn: i32,
        negative_cash_weeks: i32,
        group: i32,
        /// Personality and occupation (C# column `Trait`).
        trait_id: u8,
        memory: NodeMemory,
        vehicle: u8,
        association: i32,
        load_cost: i64,
        hedge_floor: i64,
    }
}

impl AgentTable {
    /// C# `AgentTable.Add`; returns the new row.
    pub fn add(&mut self, node: i32, owner: i32, scale: u8, mode: u8, cash: i64) -> usize {
        self.node.push(node);
        self.owner.push(owner);
        self.scale.push(scale);
        self.mode.push(mode);
        self.cash.push(cash);
        self.home.push(node);
        self.destination.push(-1);
        self.arrival_turn.push(-1);
        self.negative_cash_weeks.push(0);
        self.group.push(-1);
        self.trait_id.push(TRAIT_NEUTRAL);
        self.memory.push(NodeMemory::EMPTY);
        self.vehicle.push(VEHICLE_PACK);
        self.association.push(-1);
        self.load_cost.push(0);
        self.hedge_floor.push(0);
        self.count() - 1
    }

    /// C# `SpawnMerchants`: each node's merchants by level, with capital, trait and vehicle.
    pub fn spawn(nodes: &NodeTable, config: &SimulationConfig, seed: u64) -> Self {
        let mut table = Self::default();
        for n in 0..nodes.count() as i32 {
            let level = nodes.level_of(n);
            let node = if nodes.absorbed() {
                nodes.core[n as usize]
            } else {
                n
            };
            for _ in 0..config.merchants_per_node[level as usize] {
                table.spawn_one(node, level as u8, config, seed);
            }
        }
        table
    }

    fn spawn_one(&mut self, node: i32, scale: u8, config: &SimulationConfig, seed: u64) {
        let cash = capital_start(config, seed, self.count() as i32);
        let id = self.add(node, OWNER_NPC, scale, MODE_AUTOMATED, cash);
        if config.merchant_traits_enabled {
            let stream = det_hash::of("agents.merchant_trait");
            let mut rng = DetRandom::for_key(seed, 0, stream, id as i32, 0);
            self.trait_id[id] = rng.next_int(TRAIT_COUNT) as u8;
        }
        self.vehicle[id] = config.merchant_class_vehicle[capital_class(config, seed, id as i32)];
    }
}

/// C# `MerchantCapital.ClassOf`: the group's capital class from the seed and its row.
pub fn capital_class(config: &SimulationConfig, seed: u64, merchant: i32) -> usize {
    let stream = det_hash::of("agents.merchant_capital");
    let roll = DetRandom::for_key(seed, 0, stream, merchant, 0).next_int(1000);
    let mut below = 0;
    for c in 0..PLAYER_LIKE {
        below += config.merchant_class_share_permille[c];
        if roll < below {
            return c;
        }
    }
    PLAYER_LIKE
}

/// C# `MerchantCapital.Start`: the group's starting cash.
pub fn capital_start(config: &SimulationConfig, seed: u64, merchant: i32) -> i64 {
    match capital_class(config, seed, merchant) {
        PLAYER_LIKE => config.player_start_cash,
        c => config.merchant_class_cash[c],
    }
}
