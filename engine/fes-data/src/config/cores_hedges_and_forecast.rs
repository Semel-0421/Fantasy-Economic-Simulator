//! C# `ApplyCoresHedgesAndForecast`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.lod_mode = r.int("lod.mode", c.lod_mode)?;
    c.core_absorb = r.flag("core.absorb", c.core_absorb)?;
    for k in 0..c.merchant_class_cash.len() {
        c.merchant_class_cash[k] = r.long(
            &format!("merchants.class{k}.start_cash"),
            c.merchant_class_cash[k],
        )?;
        c.merchant_class_share_permille[k] = r.int(
            &format!("merchants.class{k}.share_permille"),
            c.merchant_class_share_permille[k],
        )?;
    }
    for k in 0..c.merchant_class_vehicle.len() {
        let vehicle = r.int(
            &format!("merchants.class{k}.vehicle"),
            i32::from(c.merchant_class_vehicle[k]),
        )?;
        let Ok(vehicle @ 0..=2) = u8::try_from(vehicle) else {
            return Err(bad(&format!(
                "config.csv: merchants.class{k}.vehicle must be 0, 1 or 2."
            )));
        };
        c.merchant_class_vehicle[k] = vehicle;
    }
    c.facility_close_loss_years =
        r.int("facility.close_loss_years", c.facility_close_loss_years)?;
    c.facility_repurpose_share = r.permille(
        "facility.repurpose_cost_permille",
        c.facility_repurpose_share,
    )?;
    c.facility_shares = r.int("facility.shares", c.facility_shares)?;
    c.facility_max_listed_shares =
        r.int("facility.max_listed_shares", c.facility_max_listed_shares)?;
    if c.facility_shares < 1
        || c.facility_max_listed_shares < 0
        || c.facility_max_listed_shares >= c.facility_shares
    {
        return Err(bad(
            "config.csv: facility.shares must be positive and facility.max_listed_shares below it.",
        ));
    }
    c.core_stacking = r.flag("core.stacking", c.core_stacking)?;
    c.core_demand_weighted = r.flag("core.demand_weighted", c.core_demand_weighted)?;
    c.core_weight[0] = r.int("core.continent.weight", c.core_weight[0])?;
    c.core_weight[1] = r.int("core.nation.weight", c.core_weight[1])?;
    c.core_weight[2] = r.int("core.region.weight", c.core_weight[2])?;
    c.core_wage_per_point = r.permille("core.wage_permille", c.core_wage_per_point)?;
    c.core_demand_per_point = r.permille("core.demand_permille", c.core_demand_per_point)?;
    c.merchant_risk_aversion = r.int("merchants.risk_aversion", c.merchant_risk_aversion)?;
    c.merchant_count_trip_costs =
        r.flag("merchants.count_trip_costs", c.merchant_count_trip_costs)?;
    c.merchant_must_act = r.flag("merchants.must_act", c.merchant_must_act)?;
    c.hedge_floor = r.permille("merchants.hedge_floor_permille", c.hedge_floor)?;
    c.hedge_fee = r.permille("merchants.hedge_fee_permille", c.hedge_fee)?;
    c.hedge_fall = r.permille("merchants.hedge_fall_permille", c.hedge_fall)?;
    c.hedge_fall_chance = r.permille("merchants.hedge_chance_permille", c.hedge_fall_chance)?;
    c.hedge_bank_share = r.permille("merchants.hedge_bank_share_permille", c.hedge_bank_share)?;
    c.order_expiry_weeks = r.int("orders.expiry_weeks", c.order_expiry_weeks)?;
    c.forecast_enabled = r.flag("production.forecast_enabled", c.forecast_enabled)?;
    c.forecast_min_plan = r.permille("production.forecast_min_permille", c.forecast_min_plan)?;
    c.forecast_household_weight = r.permille(
        "production.forecast_household_permille",
        c.forecast_household_weight,
    )?;
    c.forecast_cover = r.permille("production.forecast_cover_permille", c.forecast_cover)?;
    c.forecast_primary = r.flag("production.forecast_primary", c.forecast_primary)?;
    c.investment_min_plan = r.permille("investment.min_plan_permille", c.investment_min_plan)?;
    c.merchant_vehicle_wear =
        r.permille("merchants.vehicle_wear_permille", c.merchant_vehicle_wear)?;
    Ok(())
}
