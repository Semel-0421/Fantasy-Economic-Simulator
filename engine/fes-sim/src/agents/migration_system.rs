//! `agents.migration` (C# `MigrationSystem`, `Agents/Migration.cs`, DEC-079): once a year free
//! people move from villages to towns on the expected wage gap (Harris-Todaro), bound people run
//! away at a small rate, and nations whose towns stay short of workers enact emancipation.
use super::facilities::FacilityTable;
use super::labor::LaborState;
use super::migration::MigrationState;
use super::nobles::NobleState;
use super::purses::PurseState;
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::{HouseholdCash, HouseholdState};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.Migration`.
pub const ORDER: i32 = sim_order::LATE + 17;

/// A nation enacted emancipation (C# `EmancipationEnacted`); `nation` is its node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct EmancipationEnacted {
    pub nation: i32,
    pub year: i32,
}

/// People moved from a village to a town (C# `PeopleMigrated`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PeopleMigrated {
    pub from: i32,
    pub to: i32,
    pub people: i64,
}

/// `agents.migration`. `jobs` is scratch, rebuilt each run.
#[derive(Default)]
pub struct MigrationSystem {
    jobs: Vec<i64>,
}

impl System for MigrationSystem {
    fn id(&self) -> &'static str {
        "agents.migration"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let config = &ctx.services.database.config;
        if !ctx.clock().is_new_year() || ctx.turn() == 0 || config.migration_max_share.raw() <= 0 {
            return Ok(());
        }
        let nodes = ctx.services.get::<NodeTable>();
        self.jobs.clear();
        self.jobs.resize(nodes.count(), 0);
        let facilities = ctx.world.get::<FacilityTable>();
        for f in 0..facilities.count() {
            let jobs = facilities.level[f].wrapping_mul(config.migration_jobs_per_facility_level);
            self.jobs[facilities.node[f] as usize] += i64::from(jobs);
        }
        let (turn, year) = (ctx.turn(), ctx.clock().year());
        let mut m = Migrator {
            world: &mut ctx.world,
            events: &mut ctx.events,
            config,
            nodes,
            banks: ctx.services.get::<NodeBanks>(),
            jobs: &self.jobs,
            turn,
            year,
        };
        for n in 0..m.banks.nation_count(nodes) {
            m.emancipate(n as usize);
            m.migrate(n as usize);
        }
        Ok(())
    }
}

struct Migrator<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    config: &'a SimulationConfig,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    jobs: &'a [i64],
    turn: i32,
    year: i32,
}

impl Migrator<'_> {
    fn nation_nodes(&self, nation: usize) -> Vec<i32> {
        self.world.get::<PurseState>().nation_nodes[nation].clone()
    }

    fn emancipate(&mut self, nation: usize) {
        let enacted = self.world.get::<MigrationState>().enacted_year[nation];
        if enacted < 0 {
            self.try_enact(nation);
            return;
        }
        let freed_from = enacted + self.config.migration_emancipation_lead_years;
        if self.year < freed_from {
            return;
        }
        for node in self.nation_nodes(nation) {
            if self.is_town(node) {
                continue;
            }
            let n = node as usize;
            let state = self.world.get_mut::<MigrationState>();
            state.free_share[n] =
                Fx::ONE_RAW.min(state.free_share[n] + self.config.migration_freed_per_year.raw());
            if self.year >= freed_from + self.config.migration_redemption_years {
                continue;
            }
            let cash = self.world.get_mut::<HouseholdCash>();
            let paid = self
                .config
                .migration_redemption_share
                .mul_int(cash.year_income(n).max(0))
                .min(cash.cash[n].max(0));
            cash.cash[n] -= paid;
            self.world.get_mut::<NobleState>().cash[n] += paid;
        }
    }

    fn try_enact(&mut self, nation: usize) {
        let (mut jobs, mut people, mut bound) = (0i64, 0i64, 0i64);
        let population = &self.world.get::<HouseholdState>().population;
        for &node in &self.world.get::<PurseState>().nation_nodes[nation] {
            let p = population[node as usize];
            if self.is_town(node) {
                jobs += self.jobs[node as usize];
                people += p;
            } else {
                bound += p - self.free(node, p);
            }
        }
        if self.turn < self.config.migration_emancipation_earliest_years * WEEKS_PER_YEAR {
            return;
        }
        if bound <= 0
            || jobs <= 0
            || jobs - people < self.config.migration_vacancy_share.mul_int(jobs)
        {
            return;
        }
        self.world.get_mut::<MigrationState>().enacted_year[nation] = self.year;
        self.events.publish(EmancipationEnacted {
            nation: self.banks.nation_node(self.nodes, nation as i32),
            year: self.year,
        });
    }

    fn migrate(&mut self, nation: usize) {
        let nodes = self.nation_nodes(nation);
        let rate = self.world.get::<LaborState>().rate(nation);
        for &village in &nodes {
            let people = self.world.get::<HouseholdState>().population[village as usize];
            if !self.is_town(village) && people > 0 {
                self.move_from(village, &nodes, rate);
            }
        }
    }

    fn move_from(&mut self, village: i32, nodes: &[i32], rate: Fx) {
        let v = village as usize;
        let people = self.world.get::<HouseholdState>().population[v];
        let here = self.per_head(village);
        // Towns by expected gain; the best sets how many want to go, each takes its part.
        let (mut best, mut weights) = (0i64, 0i64);
        for &town in nodes {
            if !self.is_town(town) {
                continue;
            }
            let gain = self.expected(town, rate) - here;
            if gain <= 0 {
                continue;
            }
            best = best.max(gain);
            weights += self.weight(town, gain);
        }
        if best <= 0 || weights <= 0 {
            return;
        }
        let config = self.config;
        let free = self.free(village, people);
        let bound = people - free;
        let wish = Fx::from_ratio(best, best.max(here) + best)
            .mul_int(config.migration_max_share.mul_int(people) * 2);
        let free_moving = free.min(wish);
        let flee = if self.world.get::<NobleState>().contract_holds[v] {
            config.migration_flee_share
        } else {
            config.migration_flee_share
                * Fx::from_int(i64::from(config.migration_broken_contract_factor))
        };
        let moving = free_moving + flee.mul_int(bound);
        if moving <= 0 {
            return;
        }
        let held = self.world.get::<HouseholdCash>().cash[v].max(0);
        let cash_moving = held / people * moving + held % people * moving / people;
        self.send_to_towns(village, nodes, rate, moving, cash_moving, weights);
        let cash = self.world.get_mut::<HouseholdCash>();
        cash.cash[v] -= cash_moving;
        cash.saved_year[v] += cash_moving;
        self.world.get_mut::<HouseholdState>().population[v] = people - moving;
        let left_free = free - free_moving;
        self.world.get_mut::<MigrationState>().free_share[v] = if people - moving > 0 {
            Fx::from_ratio(left_free, people - moving).raw()
        } else {
            Fx::ONE_RAW
        };
    }

    fn send_to_towns(
        &mut self,
        village: i32,
        nodes: &[i32],
        rate: Fx,
        mut left_people: i64,
        mut left_cash: i64,
        mut left_weights: i64,
    ) {
        let here = self.per_head(village);
        for &town in nodes {
            if !self.is_town(town) {
                continue;
            }
            let gain = self.expected(town, rate) - here;
            if gain <= 0 {
                continue;
            }
            let weight = self.weight(town, gain);
            let to_people = left_people * weight / left_weights;
            let to_cash = left_cash * weight / left_weights;
            left_weights -= weight;
            left_people -= to_people;
            left_cash -= to_cash;
            let t = town as usize;
            self.world.get_mut::<HouseholdState>().population[t] += to_people;
            let cash = self.world.get_mut::<HouseholdCash>();
            cash.cash[t] += to_cash;
            // Savings that move are no income, and no loss where they left.
            cash.saved_year[t] -= to_cash;
            if to_people > 0 {
                self.events.publish(PeopleMigrated {
                    from: village,
                    to: town,
                    people: to_people,
                });
            }
        }
    }

    fn is_town(&self, node: i32) -> bool {
        self.nodes.level_of(node) != WorldLevel::Village
    }

    fn free(&self, node: i32, people: i64) -> i64 {
        Fx::from_raw(self.world.get::<MigrationState>().free_share[node as usize]).mul_int(people)
    }

    fn per_head(&self, node: i32) -> i64 {
        let people = self.world.get::<HouseholdState>().population[node as usize];
        if people <= 0 {
            return 0;
        }
        let income = self.world.get::<HouseholdCash>().year_income(node as usize);
        Fx::from_ratio(income.max(0), people).raw()
    }

    /// The wage here times the chance of a job: the jobs per head, at most one, times the nation's
    /// employment rate.
    fn expected(&self, town: i32, rate: Fx) -> i64 {
        let people = self.world.get::<HouseholdState>().population[town as usize].max(1);
        let chance = Fx::min(Fx::ONE, Fx::from_ratio(self.jobs[town as usize], people));
        (Fx::from_raw(self.per_head(town)) * chance * rate).raw()
    }

    /// Open jobs draw people; a full town still draws a little by its gain.
    fn weight(&self, town: i32, gain: i64) -> i64 {
        let open =
            self.jobs[town as usize] - self.world.get::<HouseholdState>().population[town as usize];
        (gain >> 6) * (1 + open.max(0) / 1000) + 1
    }
}
