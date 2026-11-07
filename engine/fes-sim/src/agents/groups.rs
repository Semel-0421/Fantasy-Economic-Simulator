//! Interest groups and the player's guild memberships, and the starting groups and owners (C#
//! `Groups.cs`: `GroupTable`, `GuildMembershipTable`, `GroupDirectory`, `GroupSetup`).
use super::columns::native_table;
use super::facilities::{FacilityTable, OWNER_BANK, OWNER_PLAYER};
use super::merchants::AgentTable;
use crate::world::NodeTable;
use fes_core::{IndustryTier, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::FacilityDef;
use fes_data::SimulationConfig;
use std::collections::{BTreeSet, HashMap};

/// C# `GroupKind`.
pub const KIND_COMPANY: u8 = 0;
pub const KIND_GUILD: u8 = 1;
pub const KIND_STATE_ENTERPRISE: u8 = 2;
pub const KIND_PRIVATE_ENTERPRISE: u8 = 3;

native_table! {
    /// Economic interest groups (C# `GroupTable`, version 1). Rows are never removed.
    GroupTable, "agents.groups", 1,
    {
        kind: u8,
        node: i32,
        category: i32,
        cash: i64,
        is_player: u8,
    }
}

native_table! {
    /// The player's guild memberships (C# `GuildMembershipTable`, version 1).
    GuildMembershipTable, "agents.guild_memberships", 1,
    {
        guild: i32,
        expires_turn: i32,
    }
}

impl GroupTable {
    /// C# `GroupTable.Add`; returns the new row.
    pub fn add(&mut self, kind: u8, node: i32, category: i32, cash: i64, player: bool) -> i32 {
        self.kind.push(kind);
        self.node.push(node);
        self.category.push(category);
        self.cash.push(cash);
        self.is_player.push(u8::from(player));
        self.count() as i32 - 1
    }

    /// C# `GroupSetup.Found`: each nation's state enterprise and companies, then one guild per city
    /// and trade its (and its villages') facilities produce.
    pub fn found(nodes: &NodeTable, db: &GameDatabase, facilities: &FacilityTable) -> Self {
        let config = &db.config;
        let mut groups = Self::default();
        let start = nodes.level_start(WorldLevel::Nation);
        for n in start..start + nodes.level_count(WorldLevel::Nation) {
            groups.add(
                KIND_STATE_ENTERPRISE,
                n,
                -1,
                config.group_start_cash[KIND_STATE_ENTERPRISE as usize],
                false,
            );
            for _ in 0..config.companies_per_nation {
                groups.add(
                    KIND_COMPANY,
                    n,
                    -1,
                    config.group_start_cash[KIND_COMPANY as usize],
                    false,
                );
            }
        }
        let mut guilds = BTreeSet::new();
        for f in 0..facilities.count() {
            let city = city_of(nodes, facilities.node[f]);
            if city < 0 {
                continue;
            }
            let def = &db.facilities[facilities.def_index[f] as usize];
            for line in &db.recipes[def.recipe].outputs {
                guilds.insert((city, db.good(line.good).category_index));
            }
        }
        for (city, category) in guilds {
            groups.add(
                KIND_GUILD,
                city,
                category,
                config.group_start_cash[KIND_GUILD as usize],
                false,
            );
        }
        groups
    }

    /// C# `GroupSetup.AssignOwners`: an owner for every NPC facility and merchant group without one.
    pub fn assign_owners(
        &mut self,
        nodes: &NodeTable,
        db: &GameDatabase,
        facilities: &mut FacilityTable,
        agents: &mut AgentTable,
    ) {
        let mut directory = GroupDirectory::new(self);
        for f in 0..facilities.count() {
            let owner = facilities.owner[f];
            if facilities.group[f] >= 0 || owner == OWNER_PLAYER || owner == OWNER_BANK {
                continue;
            }
            let node = facilities.node[f];
            let def = &db.facilities[facilities.def_index[f] as usize];
            let nation = nodes.ancestor_at(node, WorldLevel::Nation);
            let group = directory.facility_owner(def, nation, f);
            facilities.group[f] = if group >= 0 {
                group
            } else {
                directory.private_at(self, node, &db.config)
            };
        }
        for a in 0..agents.count() {
            if agents.group[a] >= 0 || agents.owner[a] == OWNER_PLAYER {
                continue;
            }
            let node = agents.home[a];
            let nation = nodes.ancestor_at(node, WorldLevel::Nation);
            let mut group = -1;
            if nation >= 0 && agents.scale[a] <= WorldLevel::Region as u8 {
                group = pick(directory.companies_of(nation), a);
            }
            agents.group[a] = if group >= 0 {
                group
            } else {
                directory.private_at(self, node, &db.config)
            };
        }
    }
}

/// The city a node's guilds sit in: the node itself if a city, its city if a village, else -1.
pub fn city_of(nodes: &NodeTable, node: i32) -> i32 {
    match nodes.level_of(node) {
        WorldLevel::City => node,
        WorldLevel::Village => {
            let parent = nodes.parent[node as usize];
            if nodes.level_of(parent) == WorldLevel::City {
                parent
            } else {
                -1
            }
        }
        _ => -1,
    }
}

/// The group row of the guild that insures trips from `node` (its city's), or -1 (C#
/// `MoneyLedger.InsuranceGuild`).
pub fn insurance_guild(groups: &GroupTable, nodes: &NodeTable, node: i32) -> i32 {
    let city = city_of(nodes, node);
    if city < 0 {
        return -1;
    }
    (0..groups.count())
        .find(|&g| groups.kind[g] == KIND_GUILD && groups.node[g] == city)
        .map_or(-1, |g| g as i32)
}

fn pick(candidates: &[i32], index: usize) -> i32 {
    if candidates.is_empty() {
        -1
    } else {
        candidates[index % candidates.len()]
    }
}

/// Lookups over the groups (C# `GroupDirectory`), kept current as private enterprises are added.
struct GroupDirectory {
    private_by_node: HashMap<i32, i32>,
    state_by_nation: HashMap<i32, i32>,
    companies_by_nation: HashMap<i32, Vec<i32>>,
}

impl GroupDirectory {
    fn new(groups: &GroupTable) -> Self {
        let mut d = Self {
            private_by_node: HashMap::new(),
            state_by_nation: HashMap::new(),
            companies_by_nation: HashMap::new(),
        };
        for g in 0..groups.count() {
            let node = groups.node[g];
            match groups.kind[g] {
                KIND_PRIVATE_ENTERPRISE => {
                    d.private_by_node.insert(node, g as i32);
                }
                KIND_STATE_ENTERPRISE => {
                    d.state_by_nation.insert(node, g as i32);
                }
                KIND_COMPANY if groups.is_player[g] == 0 => {
                    d.companies_by_nation
                        .entry(node)
                        .or_default()
                        .push(g as i32);
                }
                _ => {}
            }
        }
        d
    }

    fn companies_of(&self, nation: i32) -> &[i32] {
        self.companies_by_nation
            .get(&nation)
            .map_or(&[], Vec::as_slice)
    }

    fn facility_owner(&self, def: &FacilityDef, nation: i32, f: usize) -> i32 {
        if nation >= 0 && def.state_owned {
            return self.state_by_nation.get(&nation).copied().unwrap_or(-1);
        }
        if nation >= 0 && def.tier == IndustryTier::Secondary {
            return pick(self.companies_of(nation), f);
        }
        -1
    }

    /// The node's private enterprise, founded when the node has none yet.
    fn private_at(&mut self, groups: &mut GroupTable, node: i32, config: &SimulationConfig) -> i32 {
        if let Some(g) = self.private_by_node.get(&node) {
            return *g;
        }
        let cash = config.group_start_cash[KIND_PRIVATE_ENTERPRISE as usize];
        let g = groups.add(KIND_PRIVATE_ENTERPRISE, node, -1, cash, false);
        self.private_by_node.insert(node, g);
        g
    }
}

/// The node's private enterprise, founded when the node has none (C# `GroupDirectory.PrivateAt`
/// on a freshly built directory: the last such row wins).
pub fn private_enterprise_at(groups: &mut GroupTable, node: i32, config: &SimulationConfig) -> i32 {
    if let Some(g) = (0..groups.count())
        .rposition(|g| groups.kind[g] == KIND_PRIVATE_ENTERPRISE && groups.node[g] == node)
    {
        return g as i32;
    }
    let cash = config.group_start_cash[KIND_PRIVATE_ENTERPRISE as usize];
    groups.add(KIND_PRIVATE_ENTERPRISE, node, -1, cash, false)
}
