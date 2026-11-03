//! C# `ApplyGradesVehiclesAndFood`.

use super::reader::{bad, Reader, GRADES};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    for (g, grade) in GRADES.iter().enumerate() {
        c.grade_price_multiplier[g] = r.permille(
            &format!("grade.{grade}.price_permille"),
            c.grade_price_multiplier[g],
        )?;
    }
    c.production_variance_permille = r.int(
        "production.variance_permille",
        c.production_variance_permille,
    )?;
    c.merchant_bankrupt_weeks = r.int("merchants.bankrupt_weeks", c.merchant_bankrupt_weeks)?;
    if c.merchant_bankrupt_weeks < 1 {
        return Err(bad("config.csv: merchants.bankrupt_weeks must be >= 1."));
    }
    c.merchant_reserve_upkeep_weeks = r.int(
        "merchants.reserve_upkeep_weeks",
        c.merchant_reserve_upkeep_weeks,
    )?;
    if c.merchant_reserve_upkeep_weeks < 0 {
        return Err(bad(
            "config.csv: merchants.reserve_upkeep_weeks must be >= 0.",
        ));
    }
    c.merchant_sell_to_households = r.flag(
        "merchants.sell_to_households",
        c.merchant_sell_to_households,
    )?;
    c.merchant_flow_share = r.permille("merchants.flow_permille", c.merchant_flow_share)?;
    for k in 0..c.merchant_vehicle_capacity.len() {
        c.merchant_vehicle_capacity[k] = r.permille(
            &format!("merchants.vehicle{k}.capacity_permille"),
            c.merchant_vehicle_capacity[k],
        )?;
        c.merchant_vehicle_cash_times[k] = r.int(
            &format!("merchants.vehicle{k}.cash_times"),
            c.merchant_vehicle_cash_times[k],
        )?;
        c.merchant_vehicle_cost[k] = r.permille(
            &format!("merchants.vehicle{k}.cost_permille"),
            c.merchant_vehicle_cost[k],
        )?;
        c.merchant_vehicle_danger[k] = r.permille(
            &format!("merchants.vehicle{k}.danger_permille"),
            c.merchant_vehicle_danger[k],
        )?;
        c.merchant_vehicle_premium[k] = r.permille(
            &format!("merchants.vehicle{k}.premium_permille"),
            c.merchant_vehicle_premium[k],
        )?;
    }
    c.order_lead_weeks = r.int("orders.lead_weeks", c.order_lead_weeks)?;
    for (g, grade) in GRADES.iter().enumerate() {
        c.food_grade_price[g] = r.permille(
            &format!("food.{grade}.price_permille"),
            c.food_grade_price[g],
        )?;
        c.food_grade_demand[g] = r.permille(
            &format!("food.{grade}.demand_permille"),
            c.food_grade_demand[g],
        )?;
    }
    Ok(())
}
