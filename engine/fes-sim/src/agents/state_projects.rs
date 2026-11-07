//! `agents.state_projects` (C# `StateProjectSystem`, DEC-072, DEC-128, DEC-135): once a year each
//! nation plans land development from its reserve above target, by what holds it back; one project
//! at a time starts as the reserve can pay, and farmland and mines open state-enterprise facilities.
use super::banks::BankState;
use super::development::{DevelopmentState, StateProject};
use super::facilities::{FacilityTable, MODE_AUTOMATED, OWNER_NPC};
use super::groups::{private_enterprise_at, GroupTable};
use super::land::LandState;
use super::money_ledger::{pay_households, state_excess};
use super::places::{
    add_prices, allowed, find_route, is_land, is_water, project_kind as kind, state_enterprise_of,
};
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::{HouseholdCash, HouseholdState, MarketStore};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::roads::MAX_CONDITION;
use crate::world::WorldState;
use crate::world::{NodeBanks, NodePorts, NodeSpecialties, NodeTable, RoadState, RouteTable};
use fes_core::{Fx, IndustryTier, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::FacilityDef;

/// C# `FinanceOrder.StateProjects`.
pub const ORDER: i32 = sim_order::LATE + 18;

/// A state project was finished (C# `StateProjectFinished`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct StateProjectFinished {
    pub kind: u8,
    pub node: i32,
    pub turn: i32,
}

/// In the data, state-owned primary producers are the mines.
pub fn is_mine(def: &FacilityDef) -> bool {
    def.tier == IndustryTier::Primary && def.state_owned
}

/// Other primary producers work farmland.
pub fn is_farm(def: &FacilityDef) -> bool {
    def.tier == IndustryTier::Primary && !def.state_owned
}

/// `agents.state_projects`. `count` is scratch: live facilities per node.
#[derive(Default)]
pub struct StateProjectSystem {
    count: Vec<i32>,
}

impl System for StateProjectSystem {
    fn id(&self) -> &'static str {
        "agents.state_projects"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let new_year = ctx.clock().is_new_year() && turn > 0;
        let s = &ctx.services;
        let mut p = Planner {
            world: &mut ctx.world,
            events: &mut ctx.events,
            db: &s.database,
            nodes: s.get::<NodeTable>(),
            banks: s.get::<NodeBanks>(),
            routes: s.get::<RouteTable>(),
            ports: s.get::<NodePorts>(),
            specialties: s.get::<NodeSpecialties>(),
            count: &mut self.count,
            turn,
        };
        p.finish();
        let nations = p.banks.nation_count(p.nodes);
        if new_year {
            for n in 0..nations {
                p.plan(n);
            }
            p.world.get_mut::<DevelopmentState>().imports.fill(0);
        }
        p.start_next(nations);
        Ok(())
    }
}

struct Planner<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    routes: &'a RouteTable,
    ports: &'a NodePorts,
    specialties: &'a NodeSpecialties,
    count: &'a mut Vec<i32>,
    turn: i32,
}

impl Planner<'_> {
    fn development(&self) -> &DevelopmentState {
        self.world.get::<DevelopmentState>()
    }

    fn nation_nodes(&self, nation: i32) -> &[i32] {
        &self.world.get::<PurseState>().nation_nodes[nation as usize]
    }

    /// In each nation with nothing under way, its next reserved project, as soon as the reserve
    /// can pay for it.
    fn start_next(&mut self, nations: i32) {
        for n in 0..nations as usize {
            let Some(next) = self.next_to_start(n as i32) else {
                continue;
            };
            let project = self.development().projects[next];
            let cost = self.cost_of(&project);
            let bank = self.world.get_mut::<BankState>();
            if bank.reserve[n] - bank.player_deposit[n] < cost {
                continue;
            }
            bank.reserve[n] -= cost;
            let site = if project.route >= 0 {
                self.routes.from[project.route as usize]
            } else {
                project.node
            };
            pay_households(self.world, self.nodes, site, cost);
            let done = self.turn + self.db.config.state_project_weeks;
            self.world.get_mut::<DevelopmentState>().projects[next].done_turn = done;
        }
    }

    /// The nation's next project not begun, or none while one is under way.
    fn next_to_start(&self, nation: i32) -> Option<usize> {
        let mut next = None;
        for (i, p) in self.development().projects.iter().enumerate() {
            if p.nation != nation {
                continue;
            }
            if p.done_turn >= 0 {
                return None;
            }
            next = next.or(Some(i));
        }
        next
    }

    fn plan(&mut self, nation: i32) {
        let config = &self.db.config;
        self.world
            .get_mut::<DevelopmentState>()
            .projects
            .retain(|p| !(p.nation == nation && p.done_turn < 0));
        let excess = state_excess(self.world, self.nodes, self.banks, config, nation);
        let mut budget = config.state_project_share.mul_int(excess.max(0));
        let mut scores = [0i64; 5];
        scores[kind::FARMLAND as usize] = self.necessity_gap_permille(nation);
        scores[kind::MINE as usize] = self.mined_goods_dearness_permille(nation);
        let imports = self.imports_permille(nation);
        let trade = i64::from(config.state_trade_score);
        scores[kind::HARBOR as usize] = imports + trade;
        scores[kind::HIGHWAY as usize] = imports + trade;
        scores[kind::ROAD as usize] = i64::from(config.state_road_score);
        self.count_facilities();
        for _ in 0..config.state_projects_per_year {
            let Some(best) = best_score(&scores) else {
                return;
            };
            let mut project = self.site(best as u8, nation);
            scores[best] = 0; // the next project of the year looks at what else holds it back
            if project.node < 0 && project.route < 0 {
                continue;
            }
            let cost = self.cost_of(&project);
            if cost > budget {
                continue;
            }
            budget -= cost;
            project.nation = nation;
            project.done_turn = -1;
            self.world
                .get_mut::<DevelopmentState>()
                .projects
                .push(project);
        }
    }

    fn count_facilities(&mut self) {
        self.count.clear();
        self.count.resize(self.nodes.count(), 0);
        let facilities = self.world.get::<FacilityTable>();
        for f in 0..facilities.count() {
            if facilities.level[f] > 0 {
                self.count[facilities.node[f] as usize] += 1;
            }
        }
    }

    fn necessity_gap_permille(&self, nation: i32) -> i64 {
        let population = self.world.get::<HouseholdState>();
        let (mut wanted, mut met) = (0i64, 0i64);
        for &node in self.nation_nodes(nation) {
            wanted += population.necessity_wanted[node as usize];
            met += population.necessity_met[node as usize];
        }
        if wanted <= 0 || met >= wanted {
            return 0;
        }
        (wanted - met) * 1000 / wanted
    }

    fn mined_goods_dearness_permille(&self, nation: i32) -> i64 {
        let markets = self.world.get::<MarketStore>();
        let nodes = self.nation_nodes(nation);
        let (mut price, mut anchor) = (0i64, 0i64);
        for def in self.db.facilities.iter().filter(|d| is_mine(d)) {
            for line in &self.db.recipes[def.recipe].outputs {
                add_prices(markets, nodes, line.good.0, &mut price, &mut anchor);
            }
        }
        if anchor > 0 && price > anchor {
            (price - anchor) * 1000 / anchor
        } else {
            0
        }
    }

    fn imports_permille(&self, nation: i32) -> i64 {
        let households = self.world.get::<HouseholdCash>();
        let spent: i64 = self
            .nation_nodes(nation)
            .iter()
            .map(|&n| households.spent_year[n as usize])
            .sum();
        self.development().imports[nation as usize] * 1000 / spent.max(1)
    }

    fn site(&self, kind: u8, nation: i32) -> StateProject {
        let mut project = StateProject {
            kind,
            nation: 0,
            node: -1,
            route: -1,
            def_index: -1,
            done_turn: 0,
        };
        match kind {
            kind::FARMLAND | kind::MINE => self.site_on_land(&mut project, nation),
            kind::HARBOR => {
                let development = self.development();
                project.node = self
                    .nation_nodes(nation)
                    .iter()
                    .copied()
                    .find(|&node| {
                        self.ports.contains(node)
                            && development.harbor_level[node as usize]
                                < self.db.config.state_harbor_max
                            && !self.underway(kind, node, -1)
                    })
                    .unwrap_or(-1);
            }
            kind::HIGHWAY => project.route = self.highway_site(nation),
            _ => project.route = self.road_site(nation),
        }
        project
    }

    /// The undeveloped node whose households went shortest of necessities, for a type whose
    /// output is dearest there, preferring land with a specialty in it.
    fn site_on_land(&self, project: &mut StateProject, nation: i32) {
        project.node = self.neediest_land(project.kind, nation);
        if project.node < 0 {
            return;
        }
        let mut best_ratio = i64::MIN;
        for def in &self.db.facilities {
            let fits = if project.kind == kind::FARMLAND {
                is_farm(def)
            } else {
                is_mine(def)
            };
            if !fits || !allowed(def, self.nodes, project.node) {
                continue;
            }
            let ratio = self.output_dearness(project.node, def);
            if ratio <= best_ratio {
                continue;
            }
            best_ratio = ratio;
            project.def_index = def.index;
        }
        if project.def_index < 0 {
            project.node = -1;
        }
    }

    fn neediest_land(&self, kind: u8, nation: i32) -> i32 {
        let config = &self.db.config;
        let development = self.development();
        let population = self.world.get::<HouseholdState>();
        let mut neediest = -1;
        let mut lowest = i64::MAX;
        for &node in self.nation_nodes(nation) {
            let n = node as usize;
            if development.land_level[n] >= config.state_land_max
                || self.nodes.level[n] <= WorldLevel::Nation as u8
                || !is_land(self.nodes, node)
            {
                continue;
            }
            if self.underway(kind, node, -1)
                || self.count[n] + development.land_projects_at(node) >= config.state_max_per_node
            {
                continue; // DEC-135
            }
            let supplied = population.necessity_supplied_permille(n);
            if supplied >= lowest {
                continue;
            }
            lowest = supplied;
            neediest = node;
        }
        neediest
    }

    fn output_dearness(&self, node: i32, def: &FacilityDef) -> i64 {
        let markets = self.world.get::<MarketStore>();
        let mut ratio = 0i64;
        for line in &self.db.recipes[def.recipe].outputs {
            if self.specialties.is_specialty(node, line.good.base_index()) {
                ratio += 1000;
            }
            if let Some(cell) = markets.try_find_cell(node as usize, line.good.0) {
                let base = i64::from(markets.good_base_price[line.good.0 as usize]).max(1);
                ratio += i64::from(markets.price[cell]) * 1000 / base;
            }
        }
        ratio
    }

    /// A land route out of a port or the end of a highway, so highways grow inland from the coast.
    fn highway_site(&self, nation: i32) -> i32 {
        let development = self.development();
        for r in 0..self.routes.count() {
            if !self.open_for_work(r, nation) || development.route_grade[r] > 0 {
                continue;
            }
            let from = self.routes.from[r];
            if !self.ports.contains(from) && !self.ends_highway(from) {
                continue;
            }
            let underway = self.underway(kind::HIGHWAY, -1, r as i32);
            return if underway { -1 } else { r as i32 };
        }
        -1
    }

    fn ends_highway(&self, node: i32) -> bool {
        let grade = &self.development().route_grade;
        (0..self.routes.count()).any(|r| self.routes.to[r] == node && grade[r] > 0)
    }

    /// An unpaved land route between two of the nation's towns.
    fn road_site(&self, nation: i32) -> i32 {
        let roads = self.world.get::<RoadState>();
        let city = WorldLevel::City as u8;
        for r in 0..self.routes.count() {
            if !self.open_for_work(r, nation) || roads.is_paved(r) {
                continue;
            }
            let (from, to) = (self.routes.from[r] as usize, self.routes.to[r] as usize);
            if self.nodes.level[from] > city || self.nodes.level[to] > city {
                continue;
            }
            if !self.underway(kind::ROAD, -1, r as i32) {
                return r as i32;
            }
        }
        -1
    }

    fn open_for_work(&self, r: usize, nation: i32) -> bool {
        let float = &self.world.get::<PurseState>().node_float;
        !is_water(self.routes, r)
            && self.world.get::<RoadState>().build_done_turn[r] < 0
            && float[self.routes.from[r] as usize] == nation
            && float[self.routes.to[r] as usize] == nation
    }

    fn underway(&self, kind: u8, node: i32, route: i32) -> bool {
        self.development()
            .projects
            .iter()
            .any(|p| p.kind == kind && p.node == node && p.route == route)
    }

    fn cost_of(&self, project: &StateProject) -> i64 {
        let config = &self.db.config;
        let turns = || i64::from(self.routes.travel_turns[project.route as usize]);
        match project.kind {
            kind::FARMLAND => config.state_farmland_cost,
            kind::MINE => config.state_mine_cost,
            kind::HARBOR => config.state_harbor_cost,
            kind::HIGHWAY => config.state_highway_cost_per_turn * turns(),
            _ => config.road_build_cost_per_turn * turns(),
        }
    }

    fn finish(&mut self) {
        let mut i = 0;
        while i < self.development().projects.len() {
            let p = self.development().projects[i];
            if p.done_turn < 0 || self.turn < p.done_turn {
                i += 1;
                continue;
            }
            self.world.get_mut::<DevelopmentState>().projects.remove(i);
            match p.kind {
                kind::FARMLAND | kind::MINE => self.open_facility(&p),
                kind::HARBOR => {
                    self.world.get_mut::<DevelopmentState>().harbor_level[p.node as usize] += 1;
                }
                kind::HIGHWAY => self.pave(&p, 1),
                _ => self.pave(&p, 0),
            }
            let node = if p.route >= 0 {
                self.routes.from[p.route as usize]
            } else {
                p.node
            };
            self.events.publish(StateProjectFinished {
                kind: p.kind,
                node,
                turn: self.turn,
            });
        }
    }

    /// The state enterprise opens it, with its starting cash from the reserve.
    fn open_facility(&mut self, p: &StateProject) {
        let (node, nation) = (p.node as usize, p.nation as usize);
        self.world.get_mut::<DevelopmentState>().land_level[node] += 1;
        let land = self.world.get_mut::<LandState>();
        land.plots[node] += 1; // the state opens new land for it (DEC-128)
        land.used[node] += 1;
        let config = &self.db.config;
        let def = &self.db.facilities[p.def_index as usize];
        // A family must exist to buy it at auction; the state stakes it, as nothing else made its coins.
        let (groups, bank) = self.world.get2_mut::<GroupTable, BankState>();
        let before = groups.count();
        let family = private_enterprise_at(groups, p.node, config);
        if groups.count() > before {
            bank.reserve[nation] -= groups.cash[family as usize];
        }
        let cash = config.facility_start_cash[def.tier as usize].min(bank.reserve[nation].max(0));
        bank.reserve[nation] -= cash;
        let state = state_enterprise_of(groups, self.banks.nation_node(self.nodes, p.nation));
        let facilities = self.world.get_mut::<FacilityTable>();
        let id = facilities.add(
            p.node,
            p.def_index,
            OWNER_NPC,
            MODE_AUTOMATED,
            1,
            Fx::ONE,
            cash,
        );
        facilities.group[id] = state;
    }

    fn pave(&mut self, p: &StateProject, grade: i32) {
        let route = p.route as usize;
        let back = find_route(self.routes, self.routes.to[route], self.routes.from[route]);
        for r in [p.route, back] {
            if r < 0 {
                continue;
            }
            let roads = self.world.get_mut::<RoadState>();
            roads.condition[r as usize] = MAX_CONDITION;
            roads.owner[r as usize] = p.nation;
            if grade > 0 {
                self.world.get_mut::<DevelopmentState>().route_grade[r as usize] = grade;
            }
        }
    }
}

fn best_score(scores: &[i64; 5]) -> Option<usize> {
    let mut best: Option<usize> = None;
    for (k, &s) in scores.iter().enumerate() {
        if s > 0 && best.is_none_or(|b| s > scores[b]) {
            best = Some(k);
        }
    }
    best
}
