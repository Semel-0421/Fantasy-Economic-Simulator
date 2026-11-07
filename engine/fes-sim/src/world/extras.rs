//! Static node and route tables derived from the map: specialties, tolls, banks, hazards, ports.
//! None of them is saved; they are rebuilt from the map and the definitions.
use super::nodes::NodeTable;
use super::routes::RouteTable;
use crate::error::SimError;
use fes_core::{Fx, WorldLevel};
use fes_data::database::{GameDatabase, SPECIALTIES_FILE};
use fes_data::{MapDef, SimulationConfig};
use std::collections::BTreeSet;

fn invalid(message: String) -> SimError {
    SimError::Invalid(message)
}

/// The node a map definition was built into; the table was made from the same map.
fn node_of(nodes: &NodeTable, key: &str) -> usize {
    nodes.try_find(key).expect("map node is in the table").0 as usize
}

/// Each node's specialty base goods, from its terrain tags (DEC-016). Node n's specialties are
/// `base_good[start[n]..start[n] + count[n]]`, sorted.
#[derive(Clone, Debug)]
pub struct NodeSpecialties {
    pub start: Vec<i32>,
    pub count: Vec<i32>,
    pub base_good: Vec<i32>,
}

/// The base goods a tag makes specialties.
fn tag_goods<'a>(db: &'a GameDatabase, node: &str, tag: &str) -> Result<&'a [i32], SimError> {
    let rule = db.specialty(tag).ok_or_else(|| {
        invalid(format!(
            "map node '{node}': tag '{tag}' is not in {SPECIALTIES_FILE}."
        ))
    })?;
    Ok(&rule.base_goods)
}

impl NodeSpecialties {
    pub fn new(map: &MapDef, nodes: &NodeTable, db: &GameDatabase) -> Result<Self, SimError> {
        let mut per_node: Vec<Option<BTreeSet<i32>>> = vec![None; nodes.count()];
        for def in &map.nodes {
            if def.tags.is_empty() {
                continue;
            }
            let mut goods = BTreeSet::new();
            for tag in &def.tags {
                goods.extend(tag_goods(db, &def.key, tag)?);
            }
            per_node[node_of(nodes, &def.key)] = Some(goods);
        }
        let mut out = Self {
            start: vec![0; nodes.count()],
            count: vec![0; nodes.count()],
            base_good: Vec::new(),
        };
        for (n, goods) in per_node.iter().enumerate() {
            out.start[n] = out.base_good.len() as i32;
            if let Some(goods) = goods {
                out.base_good.extend(goods.iter().copied());
                out.count[n] = goods.len() as i32;
            }
        }
        Ok(out)
    }

    pub fn is_specialty(&self, node: i32, base_good: i32) -> bool {
        let start = self.start[node as usize] as usize;
        let end = start + self.count[node as usize] as usize;
        self.base_good[start..end].contains(&base_good)
    }
}

/// Toll rates of every node (DEC-014): a share of the value of cargo entering it, plus a surcharge
/// on goods in its specialty categories. Raw `Fx` shares; the rates in force are the map's times the
/// nation's toll policy (DEC-058).
#[derive(Clone, Debug, Default)]
pub struct NodeTolls {
    pub tariff: Vec<i64>,
    pub specialty_surcharge: Vec<i64>,
    base_tariff: Vec<i64>,
    base_surcharge: Vec<i64>,
}

impl NodeTolls {
    pub fn new(
        map: &MapDef,
        nodes: &NodeTable,
        config: &SimulationConfig,
    ) -> Result<Self, SimError> {
        let mut tariff = vec![0i64; nodes.count()];
        let mut surcharge = vec![0i64; nodes.count()];
        for def in &map.nodes {
            if def.tariff_permille < -1 || def.specialty_surcharge_permille < -1 {
                return Err(invalid(format!(
                    "map node '{}': toll rates must not be negative.",
                    def.key
                )));
            }
            let n = node_of(nodes, &def.key);
            tariff[n] = if def.tariff_permille < 0 {
                config.toll_rate
            } else {
                Fx::from_permille(i64::from(def.tariff_permille))
            }
            .raw();
            surcharge[n] = if def.specialty_surcharge_permille < 0 {
                config.toll_specialty_surcharge
            } else {
                Fx::from_permille(i64::from(def.specialty_surcharge_permille))
            }
            .raw();
        }
        Ok(Self {
            base_tariff: tariff.clone(),
            base_surcharge: surcharge.clone(),
            tariff,
            specialty_surcharge: surcharge,
        })
    }

    /// C# `PolicySystem.ApplyTolls`: every node inside a nation at the map's rates times
    /// `1 + policy_step * toll_step[nation]`; nodes outside nations keep the map's rates. One pass
    /// over the nodes gives what C# gets from `ScaleNation` per nation.
    pub fn apply_steps(&mut self, nodes: &NodeTable, banks: &NodeBanks, steps: &[i32], step: Fx) {
        for n in 0..nodes.count() {
            let nation_node = nodes.ancestor_at(n as i32, WorldLevel::Nation);
            if nation_node < 0 {
                continue;
            }
            let nation = banks.nation_index(nodes, nation_node) as usize;
            let scale = Fx::ONE + step * Fx::from_int(i64::from(steps[nation]));
            self.tariff[n] = scale.mul_int(self.base_tariff[n]);
            self.specialty_surcharge[n] = scale.mul_int(self.base_surcharge[n]);
        }
    }

    /// Sets the rates of every node inside `nation_node` to the map's times `scale`.
    pub fn scale_nation(&mut self, nodes: &NodeTable, nation_node: i32, scale: Fx) {
        for n in 0..nodes.count() {
            if nodes.ancestor_at(n as i32, WorldLevel::Nation) != nation_node {
                continue;
            }
            self.tariff[n] = scale.mul_int(self.base_tariff[n]);
            self.specialty_surcharge[n] = scale.mul_int(self.base_surcharge[n]);
        }
    }
}

/// Where banks are (DEC-032, DEC-036): every node from nation down to city level is a branch of its
/// nation's bank, and each continent has at most one international bank, in a node the map marks.
#[derive(Clone, Debug)]
pub struct NodeBanks {
    /// Per continent: the international bank's node, or -1 when the map marks none.
    international_bank: Vec<i32>,
}

impl NodeBanks {
    pub fn new(map: &MapDef, nodes: &NodeTable) -> Result<Self, SimError> {
        let mut banks = Self {
            international_bank: vec![-1; nodes.continent_count() as usize],
        };
        for def in map.nodes.iter().filter(|d| d.international_bank) {
            let node = nodes
                .try_find(&def.key)
                .expect("map node is in the table")
                .0;
            if !banks.is_branch(nodes, node) {
                return Err(invalid(format!(
                    "map node '{}': an international bank must sit in a city or above, inside a nation.",
                    def.key
                )));
            }
            let continent = nodes.continent[node as usize] as usize;
            if banks.international_bank[continent] >= 0 {
                return Err(invalid(format!(
                    "map node '{}': its continent already has an international bank.",
                    def.key
                )));
            }
            banks.international_bank[continent] = node;
        }
        Ok(banks)
    }

    pub fn nation_count(&self, nodes: &NodeTable) -> i32 {
        nodes.level_count(WorldLevel::Nation)
    }

    /// Dense nation index (0..nation count) of a nation node.
    pub fn nation_index(&self, nodes: &NodeTable, nation_node: i32) -> i32 {
        nation_node - nodes.level_start(WorldLevel::Nation)
    }

    pub fn nation_node(&self, nodes: &NodeTable, nation_index: i32) -> i32 {
        nation_index + nodes.level_start(WorldLevel::Nation)
    }

    /// The nation index of a node, or -1 outside any nation.
    pub fn nation_of(&self, nodes: &NodeTable, node: i32) -> i32 {
        let nation = nodes.ancestor_at(node, WorldLevel::Nation);
        if nation < 0 {
            -1
        } else {
            self.nation_index(nodes, nation)
        }
    }

    /// The nation index of the bank whose branch is at `node`, or -1 (villages, continents).
    pub fn branch_nation(&self, nodes: &NodeTable, node: i32) -> i32 {
        if !self.is_branch(nodes, node) {
            return -1;
        }
        self.nation_index(nodes, nodes.ancestor_at(node, WorldLevel::Nation))
    }

    pub fn is_branch(&self, nodes: &NodeTable, node: i32) -> bool {
        if nodes.absorbed() {
            return nodes.is_seat(node); // banks stand at the core towns (DEC-104)
        }
        let level = nodes.level[node as usize];
        level >= WorldLevel::Nation as u8 && level <= WorldLevel::City as u8
    }

    pub fn international_bank_of(&self, continent: i32) -> i32 {
        self.international_bank[continent as usize]
    }

    pub fn is_international_bank(&self, nodes: &NodeTable, node: i32) -> bool {
        self.international_bank[nodes.continent[node as usize] as usize] == node
    }
}

/// Danger of every route before cargo and countermeasures (DEC-026): the terrain's chance of an
/// encounter per trip times the danger factor of the destination's region.
#[derive(Clone, Debug)]
pub struct RouteHazards {
    /// Raw `Fx` chance of an encounter per trip with worthless cargo and no guards.
    pub base_danger: Vec<i64>,
}

impl RouteHazards {
    pub fn new(
        map: &MapDef,
        nodes: &NodeTable,
        routes: &RouteTable,
        config: &SimulationConfig,
    ) -> Result<Self, SimError> {
        let mut region_danger = vec![config.region_danger.raw(); nodes.count()];
        for def in map.nodes.iter().filter(|d| d.danger_permille >= 0) {
            if def.level != WorldLevel::Region {
                return Err(invalid(format!(
                    "map node '{}': danger_permille is set per region.",
                    def.key
                )));
            }
            region_danger[node_of(nodes, &def.key)] =
                Fx::from_permille(i64::from(def.danger_permille)).raw();
        }
        let base_danger = (0..routes.count())
            .map(|r| {
                let region = nodes.ancestor_at(routes.to[r], WorldLevel::Region);
                let factor = if region >= 0 {
                    region_danger[region as usize]
                } else {
                    config.region_danger.raw()
                };
                let terrain = config.terrain_danger[routes.terrain[r] as usize].raw();
                Fx::round_shift(terrain.wrapping_mul(factor))
            })
            .collect();
        Ok(Self { base_danger })
    }
}

/// Which nodes are ports (DEC-053). Water routes must run between ports.
#[derive(Clone, Debug)]
pub struct NodePorts {
    pub is_port: Vec<bool>,
}

impl NodePorts {
    pub fn new(map: &MapDef, nodes: &NodeTable, routes: &RouteTable) -> Result<Self, SimError> {
        let mut is_port = vec![false; nodes.count()];
        for def in map.nodes.iter().filter(|d| d.port) {
            if let Some(node) = nodes.try_find(&def.key) {
                is_port[node.0 as usize] = true;
            }
        }
        for r in 0..routes.count() {
            let terrain = fes_core::RouteTerrain::from_u8(routes.terrain[r]);
            if !terrain.is_some_and(fes_core::RouteTerrain::is_water) {
                continue;
            }
            let (from, to) = (routes.from[r], routes.to[r]);
            if !is_port[from as usize] || !is_port[to as usize] {
                return Err(invalid(format!(
                    "Water route {}->{} must run between ports.",
                    nodes.key_of(from),
                    nodes.key_of(to)
                )));
            }
        }
        Ok(Self { is_port })
    }

    pub fn contains(&self, node: i32) -> bool {
        self.is_port[node as usize]
    }
}
