//! `agents.investment` (C# `InvestmentSystem`, `FacilityExpansion`, `FacilityRepurpose` in
//! `FacilityInvestment.cs`, DEC-080, DEC-097, DEC-125, DEC-128, DEC-135): once a year the richest
//! group that may act at each node takes the projects paying back fastest, an expansion, a change of
//! use or a new facility, raising corporate bonds for what its own money lacks.
use super::asset_prices::land_price;
use super::bond_desk::{offered, raise, CompanyRooms};
use super::credit::{BondOffers, CreditStats};
use super::development::DevelopmentState;
use super::facilities::{FacilityTable, MODE_AUTOMATED, OWNER_NPC};
use super::groups::{
    private_enterprise_at, GroupTable, KIND_COMPANY, KIND_PRIVATE_ENTERPRISE, KIND_STATE_ENTERPRISE,
};
use super::land::LandState;
use super::money_ledger::pay_households;
use super::orders::DemandForecast;
use super::places::{allowed, is_land};
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::MarketStore;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{Fx, IndustryTier, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::FacilityDef;
use fes_data::SimulationConfig;

/// C# `FinanceOrder.Investment`.
pub const ORDER: i32 = sim_order::LATE + 21;
const STOCK_WEEKS_ENOUGH: i64 = 26;
const INPUT_WEEKS_AT_HAND: i64 = 4;

/// A facility grew a level (C# `FacilityExpanded`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct FacilityExpanded {
    pub facility: i32,
    pub level: i32,
}

/// A group founded a facility (C# `FacilityFounded`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct FacilityFounded {
    pub facility: i32,
    pub group: i32,
}

/// A facility changed its use (C# `FacilityRepurposed`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct FacilityRepurposed {
    pub facility: i32,
    pub def_index: i32,
}

/// C# `FacilityExpansion.Possible`: primary producers are bound by their land.
pub fn expansion_possible(def: &FacilityDef, level: i32, config: &SimulationConfig) -> bool {
    def.tier != IndustryTier::Primary && level < config.facility_max_level
}

/// C# `FacilityExpansion.Cost`.
pub fn expansion_cost(def: &FacilityDef, level: i32, config: &SimulationConfig) -> i64 {
    config.facility_value[def.tier as usize] * i64::from(level)
}

/// C# `FacilityExpansion.OutputScale`: output at a level, as a multiple of level one.
pub fn output_scale(level: i32, config: &SimulationConfig) -> Fx {
    Fx::from_int(i64::from(level))
        * (Fx::ONE + config.facility_expansion_bonus * Fx::from_int(i64::from(level - 1)))
}

/// C# `FacilityRepurpose.MayStand`: whether a group could found this type at the node.
pub fn may_stand(def: &FacilityDef, nodes: &NodeTable, node: i32) -> bool {
    !def.state_owned
        && (def.tier != IndustryTier::Primary || nodes.level_of(node) == WorldLevel::Village)
        && allowed(def, nodes, node)
}

/// C# `FacilityRepurpose.Cost`.
pub fn repurpose_cost(def: &FacilityDef, config: &SimulationConfig) -> i64 {
    config
        .facility_repurpose_share
        .mul_int(config.facility_value[def.tier as usize])
}

impl FacilityTable {
    /// C# `FacilityTable.ChangeUse` (the unsaved `UseChanges` counter is not needed: Rust
    /// production rebuilds its lines every turn).
    pub fn change_use(&mut self, f: usize, def_index: i32) {
        self.def_index[f] = def_index;
        self.level[f] = 1;
        self.repurposable[f] = 0;
    }
}

/// The best use of a group's money so far: a facility to expand, or else a type to found.
#[derive(Clone, Copy, Default)]
struct Project {
    facility: i32,
    def: i32,
    cost: i64,
    gain: i64,
    /// DEC-125: the facility changes its use to `def` rather than growing.
    repurpose: bool,
}

/// `agents.investment`. The node lists and rooms are rebuilt at the start of each run.
#[derive(Default)]
pub struct InvestmentSystem {
    facilities_at: Vec<Vec<i32>>,
    groups_at: Vec<Vec<i32>>,
    groups_listed: usize,
    rooms: CompanyRooms,
}

impl System for InvestmentSystem {
    fn id(&self) -> &'static str {
        "agents.investment"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = &ctx.services.database;
        if !ctx.clock().is_new_year() || ctx.turn() == 0 || db.config.investment_payback_years <= 0
        {
            return Ok(());
        }
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let s = &ctx.services;
        let nodes = s.get::<NodeTable>();
        let banks = s.get::<NodeBanks>();
        self.list_by_node(&ctx.world, &s.database, nodes.count());
        let mut i = Investor {
            sys: self,
            world: &mut ctx.world,
            events: &mut ctx.events,
            offers: &mut offers,
            db: &s.database,
            nodes,
            turn,
            raised: 0,
        };
        for n in 0..banks.nation_count(nodes) {
            let nation_node = banks.nation_node(nodes, n);
            let towns = i.world.get::<PurseState>().nation_nodes[n as usize].clone();
            for node in towns {
                i.invest_at(node, nation_node);
            }
        }
        let raised = i.raised;
        *ctx.services.get_mut::<BondOffers>() = offers;
        ctx.services.get_mut::<CreditStats>().company_raised += raised;
        Ok(())
    }
}

impl InvestmentSystem {
    fn list_by_node(&mut self, world: &WorldState, db: &GameDatabase, nodes: usize) {
        self.facilities_at.resize_with(nodes, Vec::new);
        self.groups_at.resize_with(nodes, Vec::new);
        self.facilities_at.iter_mut().for_each(Vec::clear);
        self.groups_at.iter_mut().for_each(Vec::clear);
        let facilities = world.get::<FacilityTable>();
        for f in 0..facilities.count() {
            self.facilities_at[facilities.node[f] as usize].push(f as i32);
        }
        self.groups_listed = 0;
        self.list_new_groups(world.get::<GroupTable>());
        self.rooms.refresh(world, db);
    }

    fn list_new_groups(&mut self, groups: &GroupTable) {
        while self.groups_listed < groups.count() {
            let g = self.groups_listed;
            self.groups_at[groups.node[g] as usize].push(g as i32);
            self.groups_listed += 1;
        }
    }
}

struct Investor<'a> {
    sys: &'a mut InvestmentSystem,
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    offers: &'a mut BondOffers,
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    turn: i32,
    raised: i64,
}

impl Investor<'_> {
    fn invest_at(&mut self, node: i32, nation_node: i32) {
        for _ in 0..self.db.config.investment_projects_per_node {
            if !self.invest(node, nation_node) {
                break;
            }
        }
    }

    fn invest(&mut self, node: i32, nation_node: i32) -> bool {
        if !is_land(self.nodes, node) {
            return false;
        }
        let group = self.richest_at(node, nation_node);
        if group < 0 {
            return false;
        }
        let g = group as usize;
        let db = self.db;
        let config = &db.config;
        // Keeps back what staking the node's family could take; corporate bonds may add to it (DEC-097).
        let groups = self.world.get::<GroupTable>();
        let own = groups.cash[g] - config.group_start_cash[KIND_PRIVATE_ENTERPRISE as usize];
        let purses = self.world.get::<PurseState>();
        let nation = purses.node_float[groups.node[g] as usize];
        let mut cash = own;
        if config.company_debt_share.raw() > 0 && (nation as usize) < purses.nation_count {
            let room = self.sys.rooms.room(config, g);
            let offer = offered(self.offers, self.world, config, self.turn, nation, group);
            cash += room.min(offer);
        }
        let mut best = Project {
            facility: -1,
            def: -1,
            ..Project::default()
        };
        let count = self.weigh_expansions(node, group, cash, &mut best);
        self.weigh_new_uses(node, group, cash, &mut best);
        let planned = self.world.get::<DevelopmentState>().land_projects_at(node);
        if count + planned < config.investment_max_per_node {
            self.weigh_new_facilities(node, cash, &mut best); // planned land projects hold their places (DEC-135)
        }
        if best.def < 0 {
            return false;
        }
        if best.cost > own {
            let need = best.cost - own;
            let raised = raise(
                self.world,
                self.offers,
                config,
                self.turn,
                group,
                nation,
                need,
            );
            self.sys.rooms.add_debt(g, raised);
            self.world.get_mut::<GroupTable>().cash[g] += raised;
            self.raised += raised;
            if raised < need {
                return false;
            }
        }
        self.world.get_mut::<GroupTable>().cash[g] -= best.cost;
        if best.repurpose {
            self.repurpose(node, &best);
        } else if best.facility >= 0 {
            self.expand(node, &best);
        } else {
            self.found(node, group, &best);
        }
        let tier = db.facilities[best.def as usize].tier as usize;
        self.sys.rooms.add_value(g, config.facility_value[tier]);
        true
    }

    /// Returns how many facilities stand at the node.
    fn weigh_expansions(&self, node: i32, group: i32, cash: i64, best: &mut Project) -> i32 {
        let config = &self.db.config;
        let facilities = self.world.get::<FacilityTable>();
        let mut count = 0;
        for &f in &self.sys.facilities_at[node as usize] {
            let f = f as usize;
            if facilities.level[f] == 0 {
                continue; // closed: its place is free
            }
            count += 1;
            if facilities.group[f] != group || facilities.owner[f] != OWNER_NPC {
                continue;
            }
            let def = &self.db.facilities[facilities.def_index[f] as usize];
            let at = facilities.level[f];
            let cost = expansion_cost(def, at, config);
            if cost > cash
                || !expansion_possible(def, at, config)
                || !self.wanted(node, def, output_scale(at + 1, config))
            {
                continue;
            }
            let step = output_scale(at + 1, config) - output_scale(at, config);
            let gain = step.mul_int(self.unit_margin(node, def))
                - config.facility_upkeep[def.tier as usize];
            if self.better(gain, cost, best, cash) {
                *best = Project {
                    facility: f as i32,
                    def: def.index,
                    cost,
                    gain,
                    repurpose: false,
                };
            }
        }
        count
    }

    /// DEC-125: a facility the group took over after a bankruptcy may become a type that earns
    /// more, for what it gains over what it earns now.
    fn weigh_new_uses(&self, node: i32, group: i32, cash: i64, best: &mut Project) {
        let facilities = self.world.get::<FacilityTable>();
        for &f in &self.sys.facilities_at[node as usize] {
            let f = f as usize;
            if facilities.repurposable[f] == 0
                || facilities.level[f] == 0
                || facilities.group[f] != group
                || facilities.owner[f] != OWNER_NPC
            {
                continue;
            }
            let current = &self.db.facilities[facilities.def_index[f] as usize];
            let now = self
                .facility_gain(node, current, facilities.level[f])
                .max(0);
            self.weigh_uses_of(node, f, now, cash, best);
        }
    }

    /// The other types facility `f`, now earning `now`, could become.
    fn weigh_uses_of(&self, node: i32, f: usize, now: i64, cash: i64, best: &mut Project) {
        let current = self.world.get::<FacilityTable>().def_index[f];
        for def in &self.db.facilities {
            if def.index == current
                || !may_stand(def, self.nodes, node)
                || !self.wanted(node, def, Fx::ONE)
            {
                continue;
            }
            let cost = repurpose_cost(def, &self.db.config);
            let gain = self.facility_gain(node, def, 1) - now;
            if self.better(gain, cost, best, cash) {
                *best = Project {
                    facility: f as i32,
                    def: def.index,
                    cost,
                    gain,
                    repurpose: true,
                };
            }
        }
    }

    fn facility_gain(&self, node: i32, def: &FacilityDef, level: i32) -> i64 {
        let config = &self.db.config;
        output_scale(level, config).mul_int(self.unit_margin(node, def))
            - config.facility_upkeep[def.tier as usize] * i64::from(level)
    }

    fn weigh_new_facilities(&self, node: i32, cash: i64, best: &mut Project) {
        let config = &self.db.config;
        if self.world.get::<LandState>().free(node as usize) == 0 {
            return; // DEC-128: no plot to build on
        }
        let plot = land_price(self.world, self.nodes, config, node as usize);
        let village = self.nodes.level_of(node) == WorldLevel::Village;
        for def in &self.db.facilities {
            let tier = def.tier as usize;
            let cost = config.facility_value[tier] + config.facility_start_cash[tier] + plot;
            // Any node may work goods; only villages have the land for primary producers.
            let fits = def.tier != IndustryTier::Primary || village;
            if cost > cash
                || def.state_owned
                || !fits
                || !allowed(def, self.nodes, node)
                || !self.wanted(node, def, Fx::ONE)
            {
                continue;
            }
            let gain = self.unit_margin(node, def) - config.facility_upkeep[tier];
            if self.better(gain, cost, best, cash) {
                *best = Project {
                    facility: -1,
                    def: def.index,
                    cost,
                    gain,
                    repurpose: false,
                };
            }
        }
    }

    fn repurpose(&mut self, node: i32, project: &Project) {
        pay_households(self.world, self.nodes, node, project.cost);
        let f = project.facility as usize;
        self.world
            .get_mut::<FacilityTable>()
            .change_use(f, project.def);
        self.events.publish(FacilityRepurposed {
            facility: project.facility,
            def_index: project.def,
        });
    }

    fn expand(&mut self, node: i32, project: &Project) {
        pay_households(self.world, self.nodes, node, project.cost);
        let facilities = self.world.get_mut::<FacilityTable>();
        let f = project.facility as usize;
        facilities.level[f] += 1;
        self.events.publish(FacilityExpanded {
            facility: project.facility,
            level: facilities.level[f],
        });
    }

    fn found(&mut self, node: i32, group: i32, project: &Project) {
        let db = self.db;
        let config = &db.config;
        // A family must exist to buy it at auction; the investor stakes it, so no coins appear (DEC-072).
        let groups = self.world.get_mut::<GroupTable>();
        let before = groups.count();
        private_enterprise_at(groups, node, config);
        for g in before..groups.count() {
            groups.cash[group as usize] -= groups.cash[g];
        }
        let tier = db.facilities[project.def as usize].tier as usize;
        let start = config.facility_start_cash[tier];
        let land = self.world.get_mut::<LandState>();
        if land.free(node as usize) > 0 {
            land.used[node as usize] += 1;
        }
        // The builders, and the plot's price to the households who sold it.
        pay_households(self.world, self.nodes, node, project.cost - start);
        let facilities = self.world.get_mut::<FacilityTable>();
        let id = facilities.add(
            node,
            project.def,
            OWNER_NPC,
            MODE_AUTOMATED,
            1,
            Fx::ONE,
            start,
        );
        facilities.group[id] = group;
        self.sys.facilities_at[node as usize].push(id as i32);
        self.events.publish(FacilityFounded {
            facility: id as i32,
            group,
        });
    }

    fn richest_at(&mut self, node: i32, nation_node: i32) -> i32 {
        self.sys.list_new_groups(self.world.get::<GroupTable>());
        let groups = self.world.get::<GroupTable>();
        let mut best = -1;
        for &g in &self.sys.groups_at[node as usize] {
            if groups.kind[g as usize] == KIND_PRIVATE_ENTERPRISE {
                best = richer_of(groups, best, g);
            }
        }
        for &g in &self.sys.groups_at[nation_node as usize] {
            let kind = groups.kind[g as usize];
            if kind == KIND_COMPANY || kind == KIND_STATE_ENTERPRISE {
                best = richer_of(groups, best, g);
            }
        }
        best
    }

    /// Within the group's cash and the payback limit, and a greater gain per coin than the best so far.
    fn better(&self, gain: i64, cost: i64, best: &Project, cash: i64) -> bool {
        if gain <= 0 || cost > cash {
            return false;
        }
        let years = i64::from(self.db.config.investment_payback_years);
        if gain
            .wrapping_mul(i64::from(WEEKS_PER_YEAR))
            .wrapping_mul(years)
            < cost
        {
            return false;
        }
        best.cost == 0 || gain.wrapping_mul(best.cost) > best.gain.wrapping_mul(cost)
    }

    /// A week's output of one level one facility at what traders pay for it, less its inputs and
    /// tools at the nearest prices.
    fn unit_margin(&self, node: i32, def: &FacilityDef) -> i64 {
        let config = &self.db.config;
        let markets = self.world.get::<MarketStore>();
        let recipe = &self.db.recipes[def.recipe];
        let mut margin = 0i64;
        for line in &recipe.outputs {
            let worth = i64::from(line.quantity) * self.price_of(node, line.good.0);
            margin += worth - config.trader_spread.mul_int(worth);
            let base = line
                .quantity
                .wrapping_mul(markets.good_base_price[line.good.0 as usize]);
            margin -= config.facility_output_wage.mul_int(i64::from(base));
        }
        for line in recipe.inputs.iter().chain(&recipe.aids) {
            margin -= i64::from(line.quantity) * self.price_of(node, line.good.0);
        }
        margin
    }

    fn price_of(&self, node: i32, good: i32) -> i64 {
        let markets = self.world.get::<MarketStore>();
        find_nearest_market(self.nodes, markets, node, good)
            .map_or(0, |(_, cell)| i64::from(markets.price[cell]))
    }

    fn wanted(&self, node: i32, def: &FacilityDef, scale: Fx) -> bool {
        let markets = self.world.get::<MarketStore>();
        let recipe = &self.db.recipes[def.recipe];
        let nearest = |good: i32| find_nearest_market(self.nodes, markets, node, good).map(|m| m.1);
        let planned = recipe
            .outputs
            .first()
            .and_then(|first| nearest(first.good.0));
        let min_plan = self.db.config.investment_min_plan.raw();
        if planned.is_some_and(|cell| self.world.get::<DemandForecast>().plan[cell] < min_plan) {
            return false;
        }
        for line in &recipe.outputs {
            let Some(cell) = nearest(line.good.0) else {
                return false;
            };
            if markets.price[cell] < markets.good_base_price[line.good.0 as usize] {
                return false;
            }
            let enough = scale.mul_int(i64::from(line.quantity)) * STOCK_WEEKS_ENOUGH;
            if i64::from(markets.stock[cell]) > enough {
                return false;
            }
        }
        for line in &recipe.inputs {
            let Some(cell) = nearest(line.good.0) else {
                return false;
            };
            let at_hand = scale.mul_int(i64::from(line.quantity)) * INPUT_WEEKS_AT_HAND;
            if i64::from(markets.stock[cell]) < at_hand {
                return false;
            }
        }
        true
    }
}

/// The first group in table order wins a tie, whichever list it came from.
fn richer_of(groups: &GroupTable, best: i32, g: i32) -> i32 {
    let gi = g as usize;
    if groups.is_player[gi] != 0 {
        return best;
    }
    if best < 0 || groups.cash[gi] > groups.cash[best as usize] {
        return g;
    }
    if groups.cash[gi] == groups.cash[best as usize] && g < best {
        g
    } else {
        best
    }
}
