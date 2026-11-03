//! C# `ApplyLevels`.

use super::reader::{bad, Reader, LEVELS, TIERS};
use super::{SimulationConfig, TierCadence};
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    for (l, level) in LEVELS.iter().enumerate() {
        c.tier_cadences[l] = TierCadence {
            period: r.int(&format!("tier.{level}.period"), c.tier_cadences[l].period)?,
            offset: r.int(&format!("tier.{level}.offset"), c.tier_cadences[l].offset)?,
        };
        c.level_demand_weight[l] = r.permille(
            &format!("demand.{level}.weight_permille"),
            c.level_demand_weight[l],
        )?;
        c.default_population[l] = r.long(
            &format!("households.{level}.population"),
            c.default_population[l],
        )?;
        c.default_income_per_head[l] = r.permille(
            &format!("households.{level}.income_permille"),
            c.default_income_per_head[l],
        )?;
        if c.default_population[l] <= 0 || c.default_income_per_head[l].raw() <= 0 {
            return Err(bad(&format!(
                "config.csv: households.{level}.population and income must be positive."
            )));
        }
        c.level_wage_weight[l] = r.permille(
            &format!("labor.{level}.wage_weight_permille"),
            c.level_wage_weight[l],
        )?;
        c.merchants_per_node[l] = r.int(&format!("merchants.{level}"), c.merchants_per_node[l])?;
        c.merchant_max_weight[l] = r.long(
            &format!("merchants.{level}.max_weight"),
            c.merchant_max_weight[l],
        )?;
        c.merchant_max_size[l] = r.long(
            &format!("merchants.{level}.max_size"),
            c.merchant_max_size[l],
        )?;
        c.merchant_upkeep[l] =
            r.long(&format!("merchants.{level}.upkeep"), c.merchant_upkeep[l])?;
        for (t, tier) in TIERS.iter().enumerate() {
            c.producers_per_node[l][t] = r.int(
                &format!("producers.{level}.{tier}"),
                c.producers_per_node[l][t],
            )?;
        }
    }
    Ok(())
}
