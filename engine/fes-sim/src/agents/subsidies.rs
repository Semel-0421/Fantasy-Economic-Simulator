//! `agents.subsidies` (C# `SubsidySystem`, DEC-074, DEC-128, DEC-135): with money above its reserve
//! target a nation subsidises the dearest good it can make, and each year of a policy one more
//! producer opens where necessities are shortest, staked from the reserve.
use super::asset_prices::land_price;
use super::banks::BankState;
use super::development::{DevelopmentState, SubsidyPolicy};
use super::facilities::{FacilityTable, MODE_AUTOMATED, OWNER_NPC};
use super::groups::{
    city_of, private_enterprise_at, GroupTable, KIND_COMPANY, KIND_GUILD, KIND_PRIVATE_ENTERPRISE,
};
use super::land::LandState;
use super::money_ledger::{pay_households, state_excess};
use super::places::{add_prices, allowed, is_land};
use super::production::refresh_subsidy_rates;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::{HouseholdState, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{Fx, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::FacilityDef;

/// C# `FinanceOrder.Subsidies`.
pub const ORDER: i32 = sim_order::LATE + 19;

/// A nation enacted a subsidy (C# `SubsidyEnacted`); `nation` is its node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SubsidyEnacted {
    pub nation: i32,
    pub good: i32,
    pub turn: i32,
}

/// C# `GroupDirectory.GuildOf` on a fresh directory: the last guild of the trade in the city.
pub fn guild_of(groups: &GroupTable, city: i32, category: i32) -> i32 {
    (0..groups.count())
        .rposition(|g| {
            groups.kind[g] == KIND_GUILD && groups.node[g] == city && groups.category[g] == category
        })
        .map_or(-1, |g| g as i32)
}

/// `agents.subsidies`. `count` is scratch: live facilities per node.
#[derive(Default)]
pub struct SubsidySystem {
    count: Vec<i32>,
}

impl System for SubsidySystem {
    fn id(&self) -> &'static str {
        "agents.subsidies"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if !ctx.clock().is_new_year() || ctx.turn() == 0 {
            // C# leaves the rates as production set them this turn; the policies have not changed
            // since, so refreshing is the same and keeps the rates valid at this slot.
            let goods = ctx.world.get::<MarketStore>().good_base_price.len();
            let raw = ctx.services.database.config.subsidy_rate.raw();
            refresh_subsidy_rates(ctx.world.get_mut::<DevelopmentState>(), goods, raw);
            return Ok(());
        }
        let (year, turn) = (ctx.clock().year(), ctx.turn());
        let s = &ctx.services;
        let mut o = Opener {
            world: &mut ctx.world,
            db: &s.database,
            nodes: s.get::<NodeTable>(),
            banks: s.get::<NodeBanks>(),
            count: &mut self.count,
        };
        let config = &o.db.config;
        o.world
            .get_mut::<DevelopmentState>()
            .subsidies
            .retain(|p| p.end_year > year);
        for n in 0..o.banks.nation_count(o.nodes) {
            if state_excess(o.world, o.nodes, o.banks, config, n) <= 0 {
                continue;
            }
            let policies = &o.world.get::<DevelopmentState>().subsidies;
            let active = policies.iter().filter(|p| p.nation == n).count() as i32;
            if active >= config.subsidy_max_per_nation {
                continue;
            }
            let good = o.dearest_good(n);
            if good < 0 {
                continue;
            }
            o.world
                .get_mut::<DevelopmentState>()
                .subsidies
                .push(SubsidyPolicy {
                    nation: n,
                    good,
                    end_year: year + config.subsidy_years,
                });
            ctx.events.publish(SubsidyEnacted {
                nation: o.banks.nation_node(o.nodes, n),
                good,
                turn,
            });
        }
        let goods = o.world.get::<MarketStore>().good_base_price.len();
        refresh_subsidy_rates(
            o.world.get_mut::<DevelopmentState>(),
            goods,
            config.subsidy_rate.raw(),
        );
        o.count_facilities();
        let policies = o.world.get::<DevelopmentState>().subsidies.clone();
        for p in &policies {
            o.open_producer(p);
        }
        Ok(())
    }
}

struct Opener<'a> {
    world: &'a mut WorldState,
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    count: &'a mut Vec<i32>,
}

impl Opener<'_> {
    fn subsidized(&self, nation: i32, good: i32) -> bool {
        self.world
            .get::<DevelopmentState>()
            .subsidies
            .iter()
            .any(|p| p.nation == nation && p.good == good)
    }

    /// Only a good dearer than its anchor across the nation calls for a subsidy.
    fn dearest_good(&self, nation: i32) -> i32 {
        let nation_node = self.banks.nation_node(self.nodes, nation);
        let recipes = &self.db.recipes;
        let lines = self.db.facilities.iter().flat_map(|def| {
            recipes[def.recipe]
                .outputs
                .iter()
                .map(move |line| (def, line.good.0))
        });
        let mut best = -1;
        let mut best_ratio = 1000i64;
        for (def, good) in lines {
            if self.subsidized(nation, good) || !allowed(def, self.nodes, nation_node) {
                continue;
            }
            let Some(ratio) = self.dearness(nation, good) else {
                continue;
            };
            if ratio > best_ratio {
                best_ratio = ratio;
                best = good;
            }
        }
        best
    }

    /// The good's price over its anchor across the nation, in permille; none where no market carries it.
    fn dearness(&self, nation: i32, good: i32) -> Option<i64> {
        let markets = self.world.get::<MarketStore>();
        let nodes = &self.world.get::<PurseState>().nation_nodes[nation as usize];
        let (mut price, mut anchor) = (0i64, 0i64);
        add_prices(markets, nodes, good, &mut price, &mut anchor);
        (anchor > 0).then(|| price * 1000 / anchor)
    }

    fn producer_of(db: &GameDatabase, good: i32) -> Option<&FacilityDef> {
        let recipes = &db.recipes;
        db.facilities
            .iter()
            .find(|d| recipes[d.recipe].outputs.iter().any(|l| l.good.0 == good))
    }

    fn open_producer(&mut self, policy: &SubsidyPolicy) {
        let db = self.db;
        let config = &db.config;
        let Some(def) = Self::producer_of(db, policy.good) else {
            return;
        };
        let facility_cash = config.facility_start_cash[def.tier as usize];
        let start = &config.group_start_cash;
        let most_group_cash = start[KIND_COMPANY as usize]
            + start[KIND_GUILD as usize]
            + start[KIND_PRIVATE_ENTERPRISE as usize];
        let site = self.site(policy.nation, def, facility_cash + most_group_cash);
        if site < 0 {
            return;
        }
        let plot = land_price(self.world, self.nodes, config, site as usize);
        let nation_node = self.banks.nation_node(self.nodes, policy.nation);
        let existing = self.existing_producer(policy.nation, def.index);
        let category = self.world.get::<MarketStore>().good_category[policy.good as usize];
        let groups = self.world.get_mut::<GroupTable>();
        let before = groups.count();
        let group = if existing >= 0 {
            existing
        } else {
            groups.add(
                KIND_COMPANY,
                nation_node,
                -1,
                start[KIND_COMPANY as usize],
                false,
            )
        };
        let city = city_of(self.nodes, site);
        if city >= 0 && guild_of(groups, city, category) < 0 {
            groups.add(
                KIND_GUILD,
                city,
                category,
                start[KIND_GUILD as usize],
                false,
            );
        }
        // A family must exist to buy the facility at auction, as for the state's own (DEC-072).
        private_enterprise_at(groups, site, config);
        let staked: i64 = groups.cash[before..].iter().sum();
        let reserve = &mut self.world.get_mut::<BankState>().reserve[policy.nation as usize];
        *reserve -= staked;
        *reserve -= facility_cash;
        *reserve -= plot; // the state buys the plot from the households there
        let land = self.world.get_mut::<LandState>();
        if land.free(site as usize) > 0 {
            land.used[site as usize] += 1;
        }
        pay_households(self.world, self.nodes, site, plot);
        let facilities = self.world.get_mut::<FacilityTable>();
        let id = facilities.add(
            site,
            def.index,
            OWNER_NPC,
            MODE_AUTOMATED,
            1,
            Fx::ONE,
            facility_cash,
        );
        facilities.group[id] = group;
        self.count[site as usize] += 1;
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

    /// A free plot to build on (DEC-128) and the money for it all above the reserve's target.
    fn can_build(&self, site: i32, nation: i32, cost: i64) -> bool {
        self.world.get::<LandState>().free(site as usize) > 0
            && state_excess(self.world, self.nodes, self.banks, &self.db.config, nation) >= cost
    }

    fn existing_producer(&self, nation: i32, def_index: i32) -> i32 {
        let facilities = self.world.get::<FacilityTable>();
        let float = &self.world.get::<PurseState>().node_float;
        (0..facilities.count())
            .find(|&f| {
                facilities.def_index[f] == def_index
                    && facilities.group[f] >= 0
                    && float[facilities.node[f] as usize] == nation
                    && facilities.owner[f] == OWNER_NPC
            })
            .map_or(-1, |f| facilities.group[f])
    }

    /// DEC-135: the node shortest of necessities where the good can be made, with a free plot,
    /// room under the cap and the money for it all; -1 for none.
    fn site(&self, nation: i32, def: &FacilityDef, cost: i64) -> i32 {
        let config = &self.db.config;
        let development = self.world.get::<DevelopmentState>();
        let population = self.world.get::<HouseholdState>();
        let mut site = -1;
        let mut lowest = i64::MAX;
        for &node in &self.world.get::<PurseState>().nation_nodes[nation as usize] {
            let n = node as usize;
            if self.nodes.level[n] <= WorldLevel::Nation as u8
                || !is_land(self.nodes, node)
                || !allowed(def, self.nodes, node)
                || self.count[n] + development.land_projects_at(node) >= config.state_max_per_node
            {
                continue;
            }
            let supplied = population.necessity_supplied_permille(n);
            if supplied >= lowest {
                continue;
            }
            let plot = land_price(self.world, self.nodes, config, n);
            if !self.can_build(node, nation, cost + plot) {
                continue;
            }
            lowest = supplied;
            site = node;
        }
        site
    }
}
