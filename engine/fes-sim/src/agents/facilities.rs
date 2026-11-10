//! Producers and their turn-0 spawn (C# `FacilityTable`, `AgentsModule.SpawnFacilities`,
//! `SpawnContinental`; section `agents.facilities`).
use super::columns::native_table;
use crate::error::SimError;
use crate::world::NodeTable;
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx, IndustryTier, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::FacilityDef;
use std::collections::HashSet;

/// C# `OwnerIds`.
pub const OWNER_NPC: i32 = -1;
pub const OWNER_PLAYER: i32 = 0;
pub const OWNER_BANK: i32 = -2;
/// C# `ControlMode.Automated`.
pub const MODE_AUTOMATED: u8 = 1;
const TIERS: usize = 3;

native_table! {
    /// Producers: farms, mines, workshops, services (C# `FacilityTable`, version 5).
    FacilityTable, "agents.facilities", 5,
    {
        node: i32,
        def_index: i32,
        owner: i32,
        mode: u8,
        level: i32,
        /// Raw Fx.
        efficiency: i64,
        cash: i64,
        negative_cash_weeks: i32,
        warehouse_claim_until: i32,
        group: i32,
        loss_years: i32,
        repurposable: u8,
    }
}

/// What a new facility starts with (the arguments of C# `FacilityTable.Add`).
pub struct NewFacility {
    pub node: i32,
    pub def_index: i32,
    pub owner: i32,
    pub mode: u8,
    pub level: i32,
    pub efficiency: Fx,
    pub cash: i64,
}

impl FacilityTable {
    /// C# `FacilityTable.Add`; returns the new row.
    pub fn add(&mut self, new: NewFacility) -> usize {
        self.node.push(new.node);
        self.def_index.push(new.def_index);
        self.owner.push(new.owner);
        self.mode.push(new.mode);
        self.level.push(new.level);
        self.efficiency.push(new.efficiency.raw());
        self.cash.push(new.cash);
        self.negative_cash_weeks.push(0);
        self.warehouse_claim_until.push(-1);
        self.group.push(-1);
        self.loss_years.push(0);
        self.repurposable.push(0);
        self.count() - 1
    }

    /// The NPC producers of a new world (C# `SpawnFacilities`): drawn per node and tier, then the
    /// continental producers.
    pub fn spawn(nodes: &NodeTable, db: &GameDatabase, seed: u64) -> Result<Self, SimError> {
        let mut table = Self::default();
        let mut spawn = FacilitySpawn::new(nodes, db, seed);
        spawn.learn_what_nodes_make();
        spawn.add_producers(&mut table);
        spawn_continental(&mut table, nodes, db)?;
        Ok(table)
    }
}

// Producers are drawn at random per node and tier. A facility with inputs is drawn only from the
// types whose every input some facility makes in the node's subtree; with none such, a primary
// producer takes the slot.
struct FacilitySpawn<'a> {
    nodes: &'a NodeTable,
    db: &'a GameDatabase,
    seed: u64,
    stream: u32,
    defs_by_tier: [Vec<&'a FacilityDef>; TIERS],
    made: Vec<HashSet<i32>>,
}

impl<'a> FacilitySpawn<'a> {
    fn new(nodes: &'a NodeTable, db: &'a GameDatabase, seed: u64) -> Self {
        let mut defs_by_tier: [Vec<&FacilityDef>; TIERS] = Default::default();
        for def in db.facilities.iter().filter(|d| d.continent.is_none()) {
            defs_by_tier[def.tier as usize].push(def);
        }
        Self {
            nodes,
            db,
            seed,
            stream: det_hash::of("agents.spawn"),
            defs_by_tier,
            made: vec![HashSet::new(); nodes.count()],
        }
    }

    fn slots(&self, node: usize, tier: usize) -> i32 {
        self.db.config.producers_per_node[self.nodes.level[node] as usize][tier]
    }

    // Nodes run from the top down, so a town's own slots see the village output below only on a
    // second look.
    fn learn_what_nodes_make(&mut self) {
        for _pass in 0..2 {
            for n in (0..self.nodes.count()).rev() {
                self.learn_node(n);
            }
        }
    }

    fn learn_node(&mut self, n: usize) {
        for t in 0..TIERS {
            if t == IndustryTier::Tertiary as usize {
                continue;
            }
            for k in 0..self.slots(n, t) {
                self.learn_slot(n, t, k);
            }
        }
    }

    fn learn_slot(&mut self, n: usize, t: usize, k: i32) {
        let Some(def) = self.draw(n, t, k) else {
            return;
        };
        for line in &self.db.recipes[def.recipe].outputs {
            let mut m = n as i32;
            while m >= 0 {
                self.made[m as usize].insert(line.good.0);
                m = self.nodes.parent[m as usize];
            }
        }
    }

    fn add_producers(&self, table: &mut FacilityTable) {
        for n in 0..self.nodes.count() {
            for t in 0..TIERS {
                self.add_slots(table, n, t);
            }
        }
    }

    fn add_slots(&self, table: &mut FacilityTable, n: usize, t: usize) {
        for k in 0..self.slots(n, t) {
            self.add_producer(table, n, t, k);
        }
    }

    fn add_producer(&self, table: &mut FacilityTable, node: usize, tier: usize, k: i32) {
        let Some(def) = self.draw(node, tier, k) else {
            return;
        };
        // Classes' producers stand at their core, and fine workshops only at a core.
        let nodes = self.nodes;
        let mut at = nodes.core[node];
        if nodes.absorbed() && def.core_only && !nodes.is_seat(at) {
            at = nodes.core[nodes.ancestor_at(node as i32, WorldLevel::Region) as usize];
        }
        table.add(NewFacility {
            node: at,
            def_index: def.index,
            owner: OWNER_NPC,
            mode: MODE_AUTOMATED,
            level: 1,
            efficiency: Fx::ONE,
            cash: self.db.config.facility_start_cash[def.tier as usize],
        });
    }

    fn draw(&self, node: usize, tier: usize, k: i32) -> Option<&'a FacilityDef> {
        let made = &self.made[node];
        let db = self.db;
        let mut eligible: Vec<&'a FacilityDef> = self.defs_by_tier[tier]
            .iter()
            .copied()
            .filter(|def| {
                db.recipes[def.recipe]
                    .inputs
                    .iter()
                    .all(|line| made.contains(&line.good.0))
            })
            .collect();
        if eligible.is_empty() && !self.defs_by_tier[tier].is_empty() {
            eligible = self.defs_by_tier[IndustryTier::Primary as usize].clone();
        }
        if eligible.is_empty() {
            return None;
        }
        // Salt packs (tier, index) without collisions for up to 2^24 producers per tier and node.
        let salt = ((tier as u32) << 24) | k as u32;
        let mut rng = DetRandom::for_key(self.seed, 0, self.stream, node as i32, salt);
        Some(eligible[rng.next_int(eligible.len() as i32) as usize])
    }
}

// A continent's own goods start with a few producers, spread over its villages.
fn spawn_continental(
    table: &mut FacilityTable,
    nodes: &NodeTable,
    db: &GameDatabase,
) -> Result<(), SimError> {
    let per = db.config.continental_producers;
    for def in &db.facilities {
        let Some(key) = &def.continent else {
            continue;
        };
        let continent = nodes.try_find(key).ok_or_else(|| {
            SimError::Invalid(format!(
                "facilities.csv: continent '{key}' of '{}' is not on the map.",
                def.key
            ))
        })?;
        let villages: Vec<i32> = (0..nodes.count() as i32)
            .filter(|&n| {
                nodes.level_of(n) == WorldLevel::Village
                    && nodes.is_ancestor_or_self(continent.0, n)
            })
            .collect();
        let count = villages.len() as i32;
        let mut k = 0;
        while k < per && count > 0 {
            let at = villages[((def.index + k * count / per) % count) as usize];
            table.add(NewFacility {
                node: at,
                def_index: def.index,
                owner: OWNER_NPC,
                mode: MODE_AUTOMATED,
                level: 1,
                efficiency: Fx::ONE,
                cash: db.config.facility_start_cash[def.tier as usize],
            });
            k += 1;
        }
    }
    Ok(())
}
