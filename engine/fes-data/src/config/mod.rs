//! `SimulationConfig`: tunable numbers loaded from `config.csv`, matching C# values and hash.
//!
//! Every key has a code default, so the file only needs the values that differ; unknown keys are load
//! errors. The `apply` order follows the C# `SimulationConfigReader` topic by topic.

mod asset_markets;
mod bank_rates_and_finance;
mod contracts_and_associations;
mod cores_hedges_and_forecast;
mod credit;
mod danger_travel_and_roads;
mod defaults;
mod demand_and_households;
mod dividends_and_cargo;
mod fields;
mod grades_vehicles_and_food;
mod groups_and_banks;
mod guidance;
mod labor_tax_and_defense;
mod levels;
mod licenses_and_specialties;
mod merchant_judgement;
mod merchant_traits;
mod migration_and_investment;
#[cfg(feature = "parity")]
mod parity;
mod policy;
mod prices_and_currency;
mod reader;
mod start;
mod state_projects;
mod tiers_and_terrain;
mod traders_and_nobles;

use crate::{CsvTable, DataError};
use fes_core::det::det_hash;
use reader::Reader;
use std::collections::BTreeMap;

pub use fes_core::WorldLevel;
pub use fields::SimulationConfig;

/// The name of the config file in a data source.
pub const CONFIG_FILE: &str = "config.csv";

/// How often a world level's markets are stepped: due when `(turn + offset) % period == 0`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct TierCadence {
    pub period: i32,
    pub offset: i32,
}

impl Default for SimulationConfig {
    /// The code defaults, same as `SimulationConfig::defaults()`.
    fn default() -> Self {
        Self::defaults()
    }
}

impl SimulationConfig {
    /// Code defaults only.
    pub fn defaults() -> SimulationConfig {
        Self::load(None).expect("code defaults are valid")
    }

    /// Applies the table's `key,value` rows over the code defaults; an unknown key is an error.
    pub fn load(table: Option<&CsvTable>) -> Result<SimulationConfig, DataError> {
        let mut pairs = Vec::new();
        if let Some(table) = table {
            for row in 0..table.row_count() {
                pairs.push((
                    table.get_string(row, "key")?,
                    table.get_string(row, "value")?,
                ));
            }
        }
        let mut reader = Reader::new(pairs);
        let mut config = Self::code_defaults();
        config.apply(&mut reader)?;
        if let Some(key) = reader.unknown_key() {
            return Err(DataError::Format(format!(
                "config.csv: unknown key '{key}'."
            )));
        }
        config.effective = reader.into_effective();
        Ok(config)
    }

    fn apply(&mut self, r: &mut Reader) -> Result<(), DataError> {
        levels::apply(self, r)?;
        grades_vehicles_and_food::apply(self, r)?;
        contracts_and_associations::apply(self, r)?;
        cores_hedges_and_forecast::apply(self, r)?;
        tiers_and_terrain::apply(self, r)?;
        danger_travel_and_roads::apply(self, r)?;
        groups_and_banks::apply(self, r)?;
        policy::apply(self, r)?;
        demand_and_households::apply(self, r)?;
        traders_and_nobles::apply(self, r)?;
        migration_and_investment::apply(self, r)?;
        labor_tax_and_defense::apply(self, r)?;
        state_projects::apply(self, r)?;
        bank_rates_and_finance::apply(self, r)?;
        asset_markets::apply(self, r)?;
        licenses_and_specialties::apply(self, r)?;
        merchant_judgement::apply(self, r)?;
        credit::apply(self, r)?;
        merchant_traits::apply(self, r)?;
        dividends_and_cargo::apply(self, r)?;
        prices_and_currency::apply(self, r)?;
        guidance::apply(self, r)?;
        start::apply(self, r)
    }

    /// Every key with its effective value (file or default), sorted by key.
    pub fn effective_values(&self) -> &BTreeMap<String, String> {
        &self.effective
    }

    /// Hash of the effective values, so a changed code default changes the data hash just like an
    /// edited file, while comments, row order and explicitly restated defaults do not.
    pub fn compute_hash(&self) -> u64 {
        let mut hash = u64::from(det_hash::of(CONFIG_FILE));
        for (key, value) in &self.effective {
            let entry =
                det_hash::combine(u64::from(det_hash::of(key)), u64::from(det_hash::of(value)));
            hash = det_hash::combine(hash, entry);
        }
        hash
    }
}
