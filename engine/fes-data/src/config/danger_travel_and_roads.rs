//! C# `ApplyDangerTravelAndRoads`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.region_danger = r.permille("danger.region_permille", c.region_danger)?;
    c.danger_value_scale = r.long("danger.value_scale", c.danger_value_scale)?;
    if c.danger_value_scale < 1 {
        return Err(bad("config.csv: danger.value_scale must be >= 1."));
    }
    c.encounter_cash_loss = r.permille("encounter.cash_loss_permille", c.encounter_cash_loss)?;
    c.encounter_cargo_loss = r.permille("encounter.cargo_loss_permille", c.encounter_cargo_loss)?;
    c.guard_base_cost = r.long("guard.base_cost", c.guard_base_cost)?;
    c.guard_cost_share = r.permille("guard.cost_permille", c.guard_cost_share)?;
    c.guard_reduction = r.permille("guard.reduction_permille", c.guard_reduction)?;
    c.insurance_premium = r.permille("insurance.premium_permille", c.insurance_premium)?;
    c.insurance_refund = r.permille("insurance.refund_permille", c.insurance_refund)?;
    c.passage_base_fee = r.long("passage.base_fee", c.passage_base_fee)?;
    c.passage_per_unit = r.long("passage.per_unit", c.passage_per_unit)?;
    c.ship_price = r.long("ship.price", c.ship_price)?;
    c.ship_max_weight = r.long("ship.max_weight", c.ship_max_weight)?;
    c.ship_max_size = r.long("ship.max_size", c.ship_max_size)?;
    c.ship_resale_share = r.permille("ship.resale_permille", c.ship_resale_share)?;
    c.road_cost_reduction = r.permille("road.cost_reduction_permille", c.road_cost_reduction)?;
    c.road_danger_reduction =
        r.permille("road.danger_reduction_permille", c.road_danger_reduction)?;
    c.road_upkeep = r.long("road.upkeep", c.road_upkeep)?;
    c.road_decay_permille = r.int("road.decay_permille", c.road_decay_permille)?;
    c.road_build_cost_per_turn = r.long("road.build_cost_per_turn", c.road_build_cost_per_turn)?;
    c.road_build_weeks = r.int("road.build_weeks", c.road_build_weeks)?;
    if c.road_build_weeks < 1 {
        return Err(bad("config.csv: road.build_weeks must be >= 1."));
    }
    Ok(())
}
