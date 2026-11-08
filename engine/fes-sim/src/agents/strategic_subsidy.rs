//! `agents.strategic_subsidy` (C# `StrategicSubsidySystem`, DEC-087): once a year each nation
//! grants a share of its reserve above target to the groups running its strategic producers, by
//! their facility levels. Off unless `state.strategic_subsidy_permille` is set.
use super::banks::BankState;
use super::facilities::{FacilityTable, OWNER_NPC};
use super::groups::GroupTable;
use super::money_ledger::state_excess;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_data::database::GameDatabase;

/// C# `FinanceOrder.StrategicSubsidy`.
pub const ORDER: i32 = sim_order::LATE + 27;

/// `agents.strategic_subsidy`. `levels` is scratch.
#[derive(Default)]
pub struct StrategicSubsidySystem {
    levels: Vec<i64>,
}

impl System for StrategicSubsidySystem {
    fn id(&self) -> &'static str {
        "agents.strategic_subsidy"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = &ctx.services.database;
        if !ctx.clock().is_new_year()
            || ctx.turn() == 0
            || db.config.strategic_subsidy_share.raw() <= 0
        {
            return Ok(());
        }
        let nodes = ctx.services.get::<NodeTable>();
        let banks = ctx.services.get::<NodeBanks>();
        let world = &mut ctx.world;
        self.levels.resize(world.get::<GroupTable>().count(), 0);
        for n in 0..banks.nation_count(nodes) {
            self.grant(world, db, nodes, banks, n);
        }
        Ok(())
    }
}

impl StrategicSubsidySystem {
    fn grant(
        &mut self,
        world: &mut WorldState,
        db: &GameDatabase,
        nodes: &NodeTable,
        banks: &NodeBanks,
        nation: i32,
    ) {
        let total = self.count_levels(world, db, nation);
        if total <= 0 {
            return;
        }
        let excess = state_excess(world, nodes, banks, &db.config, nation).max(0);
        let budget = db.config.strategic_subsidy_share.mul_int(excess);
        let groups = world.get_mut::<GroupTable>();
        let mut paid = 0i64;
        for (g, &level) in self.levels.iter().enumerate() {
            let grant = budget / total * level + budget % total * level / total;
            groups.cash[g] += grant;
            paid += grant;
        }
        world.get_mut::<BankState>().reserve[nation as usize] -= paid;
    }

    fn count_levels(&mut self, world: &WorldState, db: &GameDatabase, nation: i32) -> i64 {
        self.levels.fill(0);
        let f = world.get::<FacilityTable>();
        let purses = world.get::<PurseState>();
        let mut total = 0i64;
        for i in 0..f.count() {
            let g = f.group[i];
            if g < 0 || f.owner[i] != OWNER_NPC || purses.node_float[f.node[i] as usize] != nation {
                continue;
            }
            if !db.facilities[f.def_index[i] as usize].strategic {
                continue;
            }
            self.levels[g as usize] += i64::from(f.level[i]);
            total += i64::from(f.level[i]);
        }
        total
    }
}
