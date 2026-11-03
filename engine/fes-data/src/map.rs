//! World layout from `map_nodes.csv` and `map_routes.csv`, matching C# `MapDef` and `CsvMapSource`.
use crate::{CsvTable, DataError, DataSource};
use fes_core::det::det_hash::{combine, of};
use fes_core::{RouteTerrain, WorldLevel};

pub const NODES_FILE: &str = "map_nodes.csv";
pub const ROUTES_FILE: &str = "map_routes.csv";

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MapNodeDef {
    pub key: String,
    pub name: String,
    pub level: WorldLevel,
    /// Parent node key; empty for continents.
    pub parent_key: String,
    /// The node at the heart of this class: where its seat stands. Empty: the first town in it.
    pub core_key: String,
    /// Terrain tags; specialties follow from them.
    pub tags: Vec<String>,
    /// Toll on entering the node, in permille of the cargo's value; -1 = config default.
    pub tariff_permille: i32,
    /// Extra toll on goods in the node's specialty categories; -1 = config default.
    pub specialty_surcharge_permille: i32,
    pub international_bank: bool,
    /// Road danger of a region, in permille of the normal level; -1 = config default.
    pub danger_permille: i32,
    pub port: bool,
    /// Starting population; -1 = the config default for the node's level.
    pub population: i32,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RouteDef {
    pub from_key: String,
    pub to_key: String,
    pub travel_turns: i32,
    pub cost_per_unit: i32,
    /// Paid once per trip, whatever the cargo.
    pub base_fee: i32,
    pub terrain: RouteTerrain,
    pub paved: bool,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MapDef {
    pub nodes: Vec<MapNodeDef>,
    pub routes: Vec<RouteDef>,
}

impl MapDef {
    /// Loads the nodes file (required) and the routes file (optional).
    pub fn load(source: &dyn DataSource) -> Result<MapDef, DataError> {
        let mut map = MapDef::default();
        let nodes = CsvTable::parse(NODES_FILE, &source.read_text(NODES_FILE)?)?;
        for r in 0..nodes.row_count() {
            map.nodes.push(read_node(&nodes, r)?);
        }
        if source.exists(ROUTES_FILE) {
            let routes = CsvTable::parse(ROUTES_FILE, &source.read_text(ROUTES_FILE)?)?;
            for r in 0..routes.row_count() {
                map.routes.push(read_route(&routes, r)?);
            }
        }
        Ok(map)
    }

    /// Structural hash (keys, levels, parents, routes) so saves can detect a changed map.
    pub fn compute_hash(&self) -> u64 {
        let mut h = u64::from(of("map"));
        for n in &self.nodes {
            h = hash_node(h, n);
        }
        for r in &self.routes {
            h = hash_route(h, r);
        }
        h
    }
}

fn u32_bits(value: i32) -> u64 {
    u64::from(value as u32)
}

fn hash_node(mut h: u64, n: &MapNodeDef) -> u64 {
    h = combine(
        h,
        combine(
            u64::from(of(&n.key)),
            combine(u64::from(n.level as u8), u64::from(of(&n.parent_key))),
        ),
    );
    for tag in &n.tags {
        h = combine(h, u64::from(of(tag)));
    }
    if !n.core_key.is_empty() {
        h = combine(h, u64::from(of(&n.core_key)));
    }
    h = combine(
        h,
        combine(
            u32_bits(n.tariff_permille),
            u32_bits(n.specialty_surcharge_permille),
        ),
    );
    if n.international_bank {
        h = combine(h, u64::from(of("international_bank")));
    }
    h = combine(h, u32_bits(n.danger_permille));
    if n.port {
        h = combine(h, u64::from(of("port")));
    }
    if n.population >= 0 {
        h = combine(h, combine(u64::from(of("population")), n.population as u64));
    }
    h
}

fn hash_route(h: u64, r: &RouteDef) -> u64 {
    let tail = combine(u64::from(r.terrain as u8), u64::from(r.paved));
    let tail = combine(u32_bits(r.base_fee), tail);
    let tail = combine(u32_bits(r.cost_per_unit), tail);
    let tail = combine(u32_bits(r.travel_turns), tail);
    let tail = combine(u64::from(of(&r.to_key)), tail);
    combine(h, combine(u64::from(of(&r.from_key)), tail))
}

fn read_node(t: &CsvTable, r: usize) -> Result<MapNodeDef, DataError> {
    let key = t.get_string(r, "key")?;
    let name = t.get_string_or(r, "name", &t.get_string(r, "key")?)?;
    Ok(MapNodeDef {
        key,
        name,
        level: t.get_enum(r, "level")?,
        parent_key: t.get_string(r, "parent")?,
        core_key: t.get_string_or(r, "core", "")?,
        tags: parse_tags(&t.get_string_or(r, "tags", "")?),
        tariff_permille: t.get_int_or(r, "tariff_permille", -1)?,
        specialty_surcharge_permille: t.get_int_or(r, "specialty_surcharge_permille", -1)?,
        international_bank: t.get_int_or(r, "international_bank", 0)? != 0,
        danger_permille: t.get_int_or(r, "danger_permille", -1)?,
        port: t.get_int_or(r, "port", 0)? != 0,
        population: t.get_int_or(r, "population", -1)?,
    })
}

fn read_route(t: &CsvTable, r: usize) -> Result<RouteDef, DataError> {
    Ok(RouteDef {
        from_key: t.get_string(r, "from")?,
        to_key: t.get_string(r, "to")?,
        travel_turns: t.get_int(r, "travel_turns")?,
        cost_per_unit: t.get_int(r, "cost_per_unit")?,
        base_fee: t.get_int_or(r, "base_fee", 0)?,
        terrain: if t.get_string_or(r, "terrain", "")?.is_empty() {
            RouteTerrain::Road
        } else {
            t.get_enum(r, "terrain")?
        },
        paved: t.get_int_or(r, "paved", 0)? != 0,
    })
}

/// Tags are separated by '|'; blanks are ignored.
fn parse_tags(text: &str) -> Vec<String> {
    text.split('|')
        .map(str::trim)
        .filter(|part| !part.is_empty())
        .map(str::to_string)
        .collect()
}
