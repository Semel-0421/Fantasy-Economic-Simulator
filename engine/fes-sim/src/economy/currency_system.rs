//! `economy.currency` (C# `CurrencySystem`, `ICurrencyModel` in `Economy/Currency.cs`): yearly
//! minting at the first week of each year, plus a rate update every turn. The model is a service,
//! so the rules can be replaced.
use super::currency::{CurrencyState, GoldStandardCurrencyModel};
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use fes_data::SimulationConfig;

/// Monetary rules. Replace the service to change how minting, inflation and exchange rates work.
pub trait CurrencyModel: Send + Sync {
    fn initialize(&self, state: &mut CurrencyState, config: &SimulationConfig, seed: u64);
    fn mint_year(&self, state: &mut CurrencyState, continent: usize, config: &SimulationConfig);
    fn update_rates(&self, state: &mut CurrencyState);
}

/// The registered `ICurrencyModel` service.
pub struct CurrencyModelService(pub Box<dyn CurrencyModel>);

impl CurrencyModel for GoldStandardCurrencyModel {
    fn initialize(&self, state: &mut CurrencyState, config: &SimulationConfig, seed: u64) {
        GoldStandardCurrencyModel::initialize(state, config, seed);
    }

    /// Each year a share of the remaining metal reserves is mined and minted.
    fn mint_year(&self, state: &mut CurrencyState, c: usize, config: &SimulationConfig) {
        let rate = config.mint_rate_per_year * state.mint_rate_multiplier[c];
        let gold = rate.mul_int(state.gold_reserve[c]);
        let silver = rate.mul_int(state.silver_reserve[c]);
        state.gold_reserve[c] -= gold;
        state.silver_reserve[c] -= silver;
        state.coin_supply[c] +=
            gold * config.coins_per_gold_unit + silver * config.coins_per_silver_unit;
    }

    fn update_rates(&self, state: &mut CurrencyState) {
        GoldStandardCurrencyModel::update_rates(state);
    }
}

/// Published once per continent when it mints.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct CurrencyMinted {
    pub continent: i32,
    pub year: i32,
}

pub struct CurrencySystem;

impl System for CurrencySystem {
    fn id(&self) -> &'static str {
        "economy.currency"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = std::sync::Arc::clone(&ctx.services.database);
        let year = ctx.clock().year();
        let mint = ctx.clock().is_new_year() && ctx.turn() > 0;
        let mut state =
            std::mem::replace(ctx.world.get_mut::<CurrencyState>(), CurrencyState::new(0));
        let model = &ctx.services.get::<CurrencyModelService>().0;
        if mint {
            for c in 0..state.continent_count() {
                model.mint_year(&mut state, c, &db.config);
                ctx.events.publish(CurrencyMinted {
                    continent: c as i32,
                    year,
                });
            }
        }
        model.update_rates(&mut state);
        *ctx.world.get_mut::<CurrencyState>() = state;
        Ok(())
    }
}
