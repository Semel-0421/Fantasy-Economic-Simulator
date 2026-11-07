//! Per-continent coinage (C# `CurrencyState`, section `economy.currency`, version 3) and the
//! gold-standard model's turn-0 initialisation and rate update (C# `GoldStandardCurrencyModel`).
//! The minting step and the `ICurrencyModel` service seam arrive with the currency system port.
use crate::world::Component;
use fes_core::det::det_hash;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{DetRandom, Fx};
use fes_data::SimulationConfig;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CurrencyState {
    pub gold_reserve: Vec<i64>,
    pub silver_reserve: Vec<i64>,
    pub coin_supply: Vec<i64>,
    /// Coin supply at world start; the inflation baseline.
    pub baseline_coin_supply: Vec<i64>,
    /// The continent's money total as last published (DEC-062).
    pub money_total: Vec<i64>,
    pub baseline_money_total: Vec<i64>,
    /// Price level relative to the baseline (1.0 = no inflation).
    pub price_index: Vec<Fx>,
    /// Value of one local coin in reference units.
    pub exchange_rate: Vec<Fx>,
    /// Multiplier on the config minting rate (DEC-058).
    pub mint_rate_multiplier: Vec<Fx>,
}

impl CurrencyState {
    pub fn new(continent_count: usize) -> Self {
        Self {
            gold_reserve: vec![0; continent_count],
            silver_reserve: vec![0; continent_count],
            coin_supply: vec![0; continent_count],
            baseline_coin_supply: vec![0; continent_count],
            money_total: vec![0; continent_count],
            baseline_money_total: vec![0; continent_count],
            price_index: vec![Fx::ONE; continent_count],
            exchange_rate: vec![Fx::ONE; continent_count],
            mint_rate_multiplier: vec![Fx::ONE; continent_count],
        }
    }

    pub fn continent_count(&self) -> usize {
        self.gold_reserve.len()
    }

    /// Converts an amount of `from` continent coins into `to` continent coins.
    pub fn convert(&self, amount: i64, from: usize, to: usize) -> i64 {
        if from == to {
            amount
        } else {
            (self.exchange_rate[from] / self.exchange_rate[to]).mul_int(amount)
        }
    }
}

impl Component for CurrencyState {
    fn key(&self) -> &'static str {
        "economy.currency"
    }

    fn version(&self) -> i32 {
        3
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let n = self.continent_count();
        let _ = w.write_i32(n as i32);
        for c in 0..n {
            let _ = w.write_i64(self.gold_reserve[c]);
            let _ = w.write_i64(self.silver_reserve[c]);
            let _ = w.write_i64(self.coin_supply[c]);
            let _ = w.write_i64(self.baseline_coin_supply[c]);
            let _ = w.write_i64(self.price_index[c].raw());
            let _ = w.write_i64(self.exchange_rate[c].raw());
        }
        for c in 0..n {
            let _ = w.write_i64(self.mint_rate_multiplier[c].raw());
        }
        for c in 0..n {
            let _ = w.write_i64(self.money_total[c]);
            let _ = w.write_i64(self.baseline_money_total[c]);
        }
    }

    /// A saved continent count other than this state's is `IoError::InvalidLength`.
    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        let n = r.read_i32()?;
        if n < 0 || n as usize != self.continent_count() {
            return Err(IoError::InvalidLength);
        }
        for c in 0..n as usize {
            self.gold_reserve[c] = r.read_i64()?;
            self.silver_reserve[c] = r.read_i64()?;
            self.coin_supply[c] = r.read_i64()?;
            self.baseline_coin_supply[c] = r.read_i64()?;
            self.price_index[c] = Fx::from_raw(r.read_i64()?);
            self.exchange_rate[c] = Fx::from_raw(r.read_i64()?);
        }
        for c in 0..n as usize {
            self.mint_rate_multiplier[c] = Fx::from_raw(r.read_i64()?);
        }
        for c in 0..n as usize {
            self.money_total[c] = r.read_i64()?;
            self.baseline_money_total[c] = r.read_i64()?;
        }
        Ok(())
    }
}

/// Gold/silver standard: continents start with different metal endowments and equal coin supply.
pub struct GoldStandardCurrencyModel;

impl GoldStandardCurrencyModel {
    pub fn initialize(state: &mut CurrencyState, config: &SimulationConfig, seed: u64) {
        let stream = det_hash::of("economy.currency.init");
        for c in 0..state.continent_count() {
            let mut rng = DetRandom::for_key(seed, 0, stream, c as i32, 0);
            let gold_scale = rng.next_spread(config.currency_endowment_spread_permille);
            let silver_scale = rng.next_spread(config.currency_endowment_spread_permille);
            state.gold_reserve[c] = gold_scale.mul_int(config.initial_gold_reserve);
            state.silver_reserve[c] = silver_scale.mul_int(config.initial_silver_reserve);
            state.coin_supply[c] = config.initial_coin_supply;
            state.baseline_coin_supply[c] = config.initial_coin_supply;
            state.money_total[c] = config.initial_coin_supply;
            state.baseline_money_total[c] = config.initial_coin_supply;
        }
        Self::update_rates(state);
    }

    pub fn update_rates(state: &mut CurrencyState) {
        let n = state.continent_count();
        let mut sum_index_raw: i64 = 0;
        for c in 0..n {
            state.price_index[c] =
                Fx::from_ratio(state.money_total[c], state.baseline_money_total[c]);
            sum_index_raw += state.price_index[c].raw();
        }
        if n == 0 {
            return;
        }
        let average = Fx::from_raw(sum_index_raw / n as i64);
        for c in 0..n {
            state.exchange_rate[c] = average / state.price_index[c];
        }
    }
}
