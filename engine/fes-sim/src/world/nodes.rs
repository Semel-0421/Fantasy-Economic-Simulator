//! The world hierarchy as one flat table (C# `NodeTable` and `WorldBuilder.BuildNodes`).
use crate::error::SimError;
use fes_core::{NodeId, WorldLevel};
use fes_data::map::MapNodeDef;
use fes_data::{MapDef, SimulationConfig};
use std::collections::HashMap;

const LEVELS: usize = WorldLevel::COUNT;

/// The world hierarchy (continent > nation > region > city > village), sorted by level, then parent,
/// then key. Static after build: rebuilt from the map, not saved.
#[derive(Clone, Debug)]
pub struct NodeTable {
    pub level: Vec<u8>,
    /// Parent node index, -1 for continents.
    pub parent: Vec<i32>,
    /// Ordinal of the continent this node belongs to.
    pub continent: Vec<i32>,
    /// The town or village at the heart of this node's class; a town or village is its own.
    pub core: Vec<i32>,
    /// What a town's being a core is worth; 0 unless classes are absorbed.
    pub core_strength: Vec<i32>,
    /// CSR child list: children of n are `children[child_start[n]..child_start[n] + child_count[n]]`.
    pub child_start: Vec<i32>,
    pub child_count: Vec<i32>,
    pub children: Vec<i32>,
    level_start: [i32; LEVELS],
    level_count: [i32; LEVELS],
    keys: Vec<String>,
    names: Vec<String>,
    index_by_key: HashMap<String, i32>,
    absorbed: bool,
}

impl NodeTable {
    pub fn count(&self) -> usize {
        self.keys.len()
    }

    pub fn level_start(&self, level: WorldLevel) -> i32 {
        self.level_start[level as usize]
    }

    pub fn level_count(&self, level: WorldLevel) -> i32 {
        self.level_count[level as usize]
    }

    pub fn continent_count(&self) -> i32 {
        self.level_count[WorldLevel::Continent as usize]
    }

    pub fn level_of(&self, node: i32) -> WorldLevel {
        WorldLevel::from_u8(self.level[node as usize]).expect("stored level is valid")
    }

    pub fn key_of(&self, node: i32) -> &str {
        &self.keys[node as usize]
    }

    pub fn name_of(&self, node: i32) -> &str {
        &self.names[node as usize]
    }

    pub fn absorbed(&self) -> bool {
        self.absorbed
    }

    pub fn is_seat(&self, node: i32) -> bool {
        self.core_strength[node as usize] > 0
    }

    /// Classes hold no market of their own now; each core town takes on its strength (DEC-104).
    pub fn absorb(&mut self, weights: &[i32], stacking: bool) {
        self.absorbed = true;
        for n in 0..self.count() {
            let level = self.level[n] as usize;
            if level >= weights.len() {
                continue;
            }
            let core = self.core[n] as usize;
            let weight = weights[level];
            self.core_strength[core] = if stacking {
                self.core_strength[core].wrapping_add(weight)
            } else {
                self.core_strength[core].max(weight)
            };
        }
    }

    pub fn try_find(&self, key: &str) -> Option<NodeId> {
        self.index_by_key.get(key).map(|&i| NodeId(i))
    }

    /// True when `ancestor` is `node` or one of its ancestors.
    pub fn is_ancestor_or_self(&self, ancestor: i32, node: i32) -> bool {
        let mut n = node;
        while n >= 0 {
            if n == ancestor {
                return true;
            }
            n = self.parent[n as usize];
        }
        false
    }

    /// Appends `root` and all its descendants to `into` (breadth first).
    pub fn collect_subtree(&self, root: i32, into: &mut Vec<i32>) {
        let first = into.len();
        into.push(root);
        let mut i = first;
        while i < into.len() {
            let n = into[i] as usize;
            let start = self.child_start[n] as usize;
            let end = start + self.child_count[n] as usize;
            into.extend_from_slice(&self.children[start..end]);
            i += 1;
        }
    }

    /// The ancestor of `node` at `level` (or the node itself), -1 if none.
    pub fn ancestor_at(&self, node: i32, level: WorldLevel) -> i32 {
        let mut n = node;
        while n >= 0 {
            if self.level[n as usize] == level as u8 {
                return n;
            }
            n = self.parent[n as usize];
        }
        -1
    }
}

/// Compressed-sparse-row grouping: items of owner o end up in `items[start[o]..start[o] + count[o]]`,
/// in ascending item order. Items with a negative owner are skipped. `items` has `item_len` slots.
/// Returns `(start, count, items)`.
pub(crate) fn build_csr(
    owner_of_item: &[i32],
    owners: usize,
    item_len: usize,
) -> (Vec<i32>, Vec<i32>, Vec<i32>) {
    let mut sizes = vec![0i32; owners];
    for &owner in owner_of_item {
        if owner >= 0 {
            sizes[owner as usize] += 1;
        }
    }
    let mut start = vec![0i32; owners];
    let mut offset = 0;
    for (o, size) in sizes.iter().enumerate() {
        start[o] = offset;
        offset += size;
    }
    let mut cursor = vec![0i32; owners];
    let mut items = vec![0i32; item_len];
    for (i, &owner) in owner_of_item.iter().enumerate() {
        if owner < 0 {
            continue;
        }
        let o = owner as usize;
        items[(start[o] + cursor[o]) as usize] = i as i32;
        cursor[o] += 1;
    }
    (start, sizes, items)
}

/// UTF-16 ordinal comparison, like C# `string.CompareOrdinal`.
fn compare_ordinal(a: &str, b: &str) -> std::cmp::Ordering {
    a.encode_utf16().cmp(b.encode_utf16())
}

fn invalid(message: String) -> SimError {
    SimError::Invalid(message)
}

fn check_nodes(map: &MapDef) -> Result<(), SimError> {
    let mut by_key: HashMap<&str, &MapNodeDef> = HashMap::new();
    for n in &map.nodes {
        if n.key.is_empty() {
            return Err(invalid("Map node with empty key.".into()));
        }
        if by_key.insert(&n.key, n).is_some() {
            return Err(invalid(format!("Duplicate map node key '{}'.", n.key)));
        }
    }
    for n in &map.nodes {
        validate(n, &by_key)?;
    }
    Ok(())
}

fn validate(n: &MapNodeDef, by_key: &HashMap<&str, &MapNodeDef>) -> Result<(), SimError> {
    if n.level == WorldLevel::Continent {
        if !n.parent_key.is_empty() {
            return Err(invalid(format!(
                "Continent '{}' must not have a parent.",
                n.key
            )));
        }
        return Ok(());
    }
    let parent = by_key
        .get(n.parent_key.as_str())
        .filter(|_| !n.parent_key.is_empty());
    let Some(parent) = parent else {
        return Err(invalid(format!(
            "Node '{}' has unknown parent '{}'.",
            n.key, n.parent_key
        )));
    };
    let ok = if n.level == WorldLevel::Village {
        matches!(parent.level, WorldLevel::City | WorldLevel::Region)
    } else {
        parent.level as u8 + 1 == n.level as u8
    };
    if !ok {
        return Err(invalid(format!(
            "Node '{}' ({}) cannot be a child of '{}' ({}).",
            n.key,
            n.level.name(),
            parent.key,
            parent.level.name()
        )));
    }
    Ok(())
}

/// One level's nodes ordered by (parent's new index, key); appends them to `ordered`.
fn add_level_in_order<'a>(
    map: &'a MapDef,
    level: WorldLevel,
    ordered: &mut Vec<&'a MapNodeDef>,
    new_index: &mut HashMap<&'a str, i32>,
) -> i32 {
    let parent_of = |n: &MapNodeDef, idx: &HashMap<&str, i32>| {
        if n.level == WorldLevel::Continent {
            -1
        } else {
            idx[n.parent_key.as_str()]
        }
    };
    let mut nodes: Vec<&MapNodeDef> = map.nodes.iter().filter(|n| n.level == level).collect();
    nodes.sort_by(|a, b| {
        let (pa, pb) = (parent_of(a, new_index), parent_of(b, new_index));
        pa.cmp(&pb).then_with(|| compare_ordinal(&a.key, &b.key))
    });
    let count = nodes.len() as i32;
    for n in nodes {
        new_index.insert(&n.key, ordered.len() as i32);
        ordered.push(n);
    }
    count
}

/// Turns the map's nodes into the table, with each level a contiguous range.
pub fn build_nodes(map: &MapDef, _config: &SimulationConfig) -> Result<NodeTable, SimError> {
    check_nodes(map)?;
    let mut ordered: Vec<&MapNodeDef> = Vec::with_capacity(map.nodes.len());
    let mut new_index: HashMap<&str, i32> = HashMap::new();
    let mut level_start = [0i32; LEVELS];
    let mut level_count = [0i32; LEVELS];
    for l in 0..LEVELS {
        level_start[l] = ordered.len() as i32;
        let level = WorldLevel::from_u8(l as u8).expect("level index");
        level_count[l] = add_level_in_order(map, level, &mut ordered, &mut new_index);
    }
    let count = ordered.len();
    let mut table = new_table(&ordered, level_start, level_count);
    let mut parents = vec![0i32; count];
    for (i, n) in ordered.iter().enumerate() {
        table.level[i] = n.level as u8;
        let parent = if n.level == WorldLevel::Continent {
            -1
        } else {
            new_index[n.parent_key.as_str()]
        };
        parents[i] = parent;
        table.parent[i] = parent;
        table.continent[i] = if parent < 0 {
            i as i32 - level_start[0]
        } else {
            table.continent[parent as usize]
        };
    }
    // Children of a parent may sit in different levels; CSR fills in item order, which keeps each
    // child list sorted by index.
    let (start, counts, items) = build_csr(&parents, count, count);
    table.child_start = start;
    table.child_count = counts;
    table.children = items;
    assign_cores(&mut table, &ordered, &new_index)?;
    Ok(table)
}

fn new_table(
    ordered: &[&MapNodeDef],
    level_start: [i32; LEVELS],
    level_count: [i32; LEVELS],
) -> NodeTable {
    let count = ordered.len();
    let keys: Vec<String> = ordered.iter().map(|n| n.key.clone()).collect();
    let names = ordered
        .iter()
        .map(|n| {
            if n.name.is_empty() {
                n.key.clone()
            } else {
                n.name.clone()
            }
        })
        .collect();
    let index_by_key = keys
        .iter()
        .enumerate()
        .map(|(i, k)| (k.clone(), i as i32))
        .collect();
    NodeTable {
        level: vec![0; count],
        parent: vec![0; count],
        continent: vec![0; count],
        core: vec![0; count],
        core_strength: vec![0; count],
        child_start: vec![0; count],
        child_count: vec![0; count],
        children: vec![0; count],
        level_start,
        level_count,
        keys,
        names,
        index_by_key,
        absorbed: false,
    }
}

fn assign_cores(
    table: &mut NodeTable,
    ordered: &[&MapNodeDef],
    new_index: &HashMap<&str, i32>,
) -> Result<(), SimError> {
    for (i, node) in ordered.iter().enumerate() {
        table.core[i] = core_of(table, node, i as i32, new_index)?;
    }
    Ok(())
}

// A town or village is its own core. A larger class names a town or village inside it, or takes its
// first town, or else its first village (DEC-101).
fn core_of(
    table: &NodeTable,
    node: &MapNodeDef,
    index: i32,
    new_index: &HashMap<&str, i32>,
) -> Result<i32, SimError> {
    let named = node.core_key.as_str();
    if table.level[index as usize] >= WorldLevel::City as u8 {
        if !named.is_empty() && named != node.key {
            return Err(invalid(format!(
                "Node '{}': a {} is its own core.",
                node.key,
                table.level_of(index).name()
            )));
        }
        return Ok(index);
    }
    if named.is_empty() {
        return Ok(first_place_inside(table, index));
    }
    let found = new_index.get(named).copied().filter(|&c| {
        table.is_ancestor_or_self(index, c) && table.level[c as usize] >= WorldLevel::City as u8
    });
    found.ok_or_else(|| {
        invalid(format!(
            "Node '{}': core '{named}' is not a town or village inside it.",
            node.key
        ))
    })
}

fn first_place_inside(table: &NodeTable, index: i32) -> i32 {
    for level in [WorldLevel::City, WorldLevel::Village] {
        let start = table.level_start(level);
        let end = start + table.level_count(level);
        if let Some(n) = (start..end).find(|&n| table.is_ancestor_or_self(index, n)) {
            return n;
        }
    }
    index
}
