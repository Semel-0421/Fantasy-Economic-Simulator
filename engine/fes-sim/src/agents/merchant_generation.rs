//! `agents.merchant_generation` (C# `MerchantGenerationSystem`): each new year, with
//! `merchants.traits`, every continent's trait weights are set by the fitness of the active groups
//! holding each trait (capital and survival), and a share of NPC groups is replaced by a new
//! generation drawn from those weights.
use super::facilities::OWNER_NPC;
use super::genetics::MerchantGenetics;
use super::lod::LodState;
use super::merchants::{AgentTable, NodeMemory, TRAIT_COUNT};
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.MerchantGenerations`.
pub const ORDER: i32 = sim_order::LATE + 40;
const TRAITS: usize = TRAIT_COUNT as usize;
const DETAILED: u8 = 1;

/// `agents.merchant_generation`. `capital` and `count` are scratch.
#[derive(Default)]
pub struct MerchantGenerationSystem {
    capital: [i64; TRAITS],
    count: [i64; TRAITS],
}

impl System for MerchantGenerationSystem {
    fn id(&self) -> &'static str {
        "agents.merchant_generation"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let config = &ctx.services.database.config;
        if !ctx.clock().is_new_year() || ctx.turn() == 0 || !config.merchant_traits_enabled {
            return Ok(());
        }
        let (seed, turn) = (ctx.seed(), ctx.turn());
        let nodes = ctx.services.get::<NodeTable>();
        let detail = &ctx.services.get::<LodState>().detail;
        let (agents, genetics) = ctx.world.get2_mut::<AgentTable, MerchantGenetics>();
        for c in 0..genetics.continent_count {
            self.score(config, nodes, detail, agents, genetics, c);
        }
        if config.merchant_generation_share.raw() <= 0 {
            return Ok(());
        }
        let stream = det_hash::of("agents.merchant_generation");
        for a in 0..agents.count() {
            if agents.owner[a] != OWNER_NPC {
                continue;
            }
            let mut rng = DetRandom::for_key(seed, turn, stream, a as i32, 0);
            if rng.next_fx01().raw() >= config.merchant_generation_share.raw() {
                continue;
            }
            let continent = nodes.continent[agents.home[a] as usize];
            agents.trait_id[a] = genetics.draw(&mut rng, continent, config.merchant_mutation_share);
            agents.memory[a] = NodeMemory::EMPTY;
        }
        Ok(())
    }
}

impl MerchantGenerationSystem {
    fn score(
        &mut self,
        config: &SimulationConfig,
        nodes: &NodeTable,
        detail: &[u8],
        agents: &AgentTable,
        genetics: &mut MerchantGenetics,
        continent: i32,
    ) {
        self.capital = [0; TRAITS];
        self.count = [0; TRAITS];
        let (mut capital, mut count) = (0i64, 0i64);
        for a in 0..agents.count() {
            if nodes.continent[agents.home[a] as usize] != continent {
                continue;
            }
            let travelling = agents.destination[a] >= 0;
            if !travelling
                && detail[agents.node[a] as usize] != DETAILED
                && detail[agents.home[a] as usize] != DETAILED
            {
                continue;
            }
            let cash = agents.cash[a].max(0);
            let t = agents.trait_id[a] as usize;
            self.capital[t] += cash;
            self.count[t] += 1;
            capital += cash;
            count += 1;
        }
        let first = continent as usize * TRAITS;
        if count > 0 && capital > 0 {
            self.set_weights(config, genetics, first, capital / count);
        }
        genetics.year_bankruptcies[first..first + TRAITS].fill(0);
    }

    fn set_weights(
        &self,
        config: &SimulationConfig,
        genetics: &mut MerchantGenetics,
        first: usize,
        average: i64,
    ) {
        let fitness: Vec<i64> = (0..TRAITS)
            .map(|t| self.fitness(config, genetics, t, first, average))
            .collect();
        let total: i64 = fitness.iter().sum();
        if total <= 0 {
            return;
        }
        for (t, f) in fitness.iter().enumerate() {
            genetics.weight[first + t] = (f << Fx::FRACTION_BITS) / total;
        }
    }

    /// A trait nobody active has keeps an average score, so it can come back.
    fn fitness(
        &self,
        config: &SimulationConfig,
        genetics: &MerchantGenetics,
        t: usize,
        first: usize,
        average: i64,
    ) -> i64 {
        let (mut relative, mut survival) = (Fx::ONE_RAW / 2, Fx::ONE_RAW);
        if self.count[t] > 0 {
            relative = Fx::ONE_RAW
                .min(((self.capital[t] / self.count[t]) << Fx::FRACTION_BITS) / average.max(1) / 2);
            let lost = i64::from(genetics.year_bankruptcies[first + t]) << Fx::FRACTION_BITS;
            survival = (Fx::ONE_RAW - lost / self.count[t]).max(0);
        }
        let share = config.merchant_fitness_capital.raw();
        ((share * relative) >> Fx::FRACTION_BITS)
            + (((Fx::ONE_RAW - share) * survival) >> Fx::FRACTION_BITS)
    }
}
