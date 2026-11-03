//! C# `ApplyTiersAndTerrain`.

use super::reader::{bad, Reader, LEVELS, TERRAINS, TIERS};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    for (t, tier) in TIERS.iter().enumerate() {
        c.facility_start_cash[t] = r.long(
            &format!("facility.{tier}.start_cash"),
            c.facility_start_cash[t],
        )?;
        c.facility_upkeep[t] = r.long(&format!("facility.{tier}.upkeep"), c.facility_upkeep[t])?;
        c.facility_value[t] = r.long(&format!("facility.{tier}.value"), c.facility_value[t])?;
    }
    for (l, level) in LEVELS.iter().enumerate() {
        c.land_plots[l] = r.int(&format!("land.{level}.plots"), c.land_plots[l])?;
        c.land_value[l] = r.long(&format!("land.{level}.value"), c.land_value[l])?;
    }
    for (t, terrain) in TERRAINS.iter().enumerate() {
        c.terrain_extra_turns[t] = r.int(
            &format!("terrain.{terrain}.extra_turns"),
            c.terrain_extra_turns[t],
        )?;
        c.terrain_cost_multiplier[t] = r.permille(
            &format!("terrain.{terrain}.cost_permille"),
            c.terrain_cost_multiplier[t],
        )?;
        c.terrain_danger[t] = r.permille(
            &format!("terrain.{terrain}.danger_permille"),
            c.terrain_danger[t],
        )?;
        if c.terrain_extra_turns[t] < 0 {
            return Err(bad(&format!(
                "config.csv: terrain.{terrain}.extra_turns must not be negative."
            )));
        }
    }
    Ok(())
}
