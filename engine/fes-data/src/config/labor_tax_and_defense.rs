//! C# `ApplyLaborTaxAndDefense`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.labor_base_employment =
        r.permille("labor.base_employment_permille", c.labor_base_employment)?;
    c.labor_min_employment = r.permille("labor.min_employment_permille", c.labor_min_employment)?;
    c.labor_max_employment = r.permille("labor.max_employment_permille", c.labor_max_employment)?;
    c.labor_response = r.permille("labor.response_permille", c.labor_response)?;
    c.labor_long_run_response = r.permille(
        "labor.long_run_response_permille",
        c.labor_long_run_response,
    )?;
    c.labor_merchant_wage = r.permille("labor.merchant_wage_permille", c.labor_merchant_wage)?;
    c.labor_merchant_buffer =
        r.permille("labor.merchant_buffer_permille", c.labor_merchant_buffer)?;
    c.tax_household = r.permille("tax.household_permille", c.tax_household)?;
    c.tax_company = r.permille("tax.company_permille", c.tax_company)?;
    c.defense_share = r.permille("state.defense_permille", c.defense_share)?;
    c.defense_maintenance =
        r.permille("state.defense_maintenance_permille", c.defense_maintenance)?;
    c.defense_interval_weeks = r.int("state.defense_interval_weeks", c.defense_interval_weeks)?;
    c.defense_safety_cost_per_route = r.int(
        "state.defense_safety_cost_per_route",
        c.defense_safety_cost_per_route,
    )?;
    c.defense_max_safety = r.permille("state.defense_max_safety_permille", c.defense_max_safety)?;
    c.subsidy_share = r.permille("state.subsidy_share_permille", c.subsidy_share)?;
    c.subsidy_rate = r.permille("state.subsidy_rate_permille", c.subsidy_rate)?;
    c.subsidy_years = r.int("state.subsidy_years", c.subsidy_years)?;
    c.subsidy_max_per_nation = r.int("state.subsidy_max_per_nation", c.subsidy_max_per_nation)?;
    c.state_max_per_node = r.int("state.max_per_node", c.state_max_per_node)?;
    if c.state_max_per_node < 0 {
        return Err(bad("config.csv: state.max_per_node must be >= 0."));
    }
    c.state_trade_score = r.int("state.trade_score_permille", c.state_trade_score)?;
    if c.defense_interval_weeks < 1 {
        return Err(bad(
            "config.csv: state.defense_interval_weeks must be >= 1.",
        ));
    }
    Ok(())
}
