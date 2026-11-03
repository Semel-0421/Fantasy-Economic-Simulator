//! C# `ApplyMerchantTraits`.

use super::reader::{bad, Reader};
use super::{SimulationConfig, WorldLevel};
use crate::{try_parse_enum_name, DataError};

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.merchant_generation_share =
        r.permille("merchants.generation_permille", c.merchant_generation_share)?;
    c.merchant_mutation_share =
        r.permille("merchants.mutation_permille", c.merchant_mutation_share)?;
    c.merchant_fitness_capital = r.permille(
        "merchants.fitness_capital_permille",
        c.merchant_fitness_capital,
    )?;
    const PERSONALITIES: [&str; 3] = ["cautious", "normal", "aggressive"];
    for (i, name) in PERSONALITIES.iter().enumerate() {
        c.personality_spend[i] = r.permille(
            &format!("merchants.{name}.spend_permille"),
            c.personality_spend[i],
        )?;
        c.personality_min_margin[i] = r.permille(
            &format!("merchants.{name}.min_margin_permille"),
            c.personality_min_margin[i],
        )?;
        c.personality_loss_aversion[i] = r.int(
            &format!("merchants.{name}.loss_aversion"),
            c.personality_loss_aversion[i],
        )?;
        let range_key = format!("merchants.{name}.range_level");
        if let Some(range) = r.get(&range_key) {
            c.personality_range[i] = try_parse_enum_name::<WorldLevel>(range).ok_or_else(|| {
                bad(&format!(
                    "config.csv: {range_key} '{range}' is not a WorldLevel."
                ))
            })?;
        }
        r.record(&range_key, c.personality_range[i].name());
    }
    const OCCUPATIONS: [&str; 3] = ["local", "expedition", "peddler"];
    for (i, name) in OCCUPATIONS.iter().enumerate() {
        c.occupation_capacity[i] = r.permille(
            &format!("merchants.{name}.capacity_permille"),
            c.occupation_capacity[i],
        )?;
        c.occupation_upkeep[i] = r.permille(
            &format!("merchants.{name}.upkeep_permille"),
            c.occupation_upkeep[i],
        )?;
        c.occupation_distance_power[i] = r.int(
            &format!("merchants.{name}.distance_power"),
            c.occupation_distance_power[i],
        )?;
        if !(0..=2).contains(&c.occupation_distance_power[i]) {
            return Err(bad(&format!(
                "config.csv: merchants.{name}.distance_power must be 0, 1 or 2."
            )));
        }
    }
    Ok(())
}
