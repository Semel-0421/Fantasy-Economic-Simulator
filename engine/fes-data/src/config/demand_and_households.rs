//! C# `ApplyDemandAndHouseholds`.

use super::reader::{bad, Reader, GRADES};
use super::SimulationConfig;
use crate::DataError;
use fes_core::Fx;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.necessity_elasticity = r.permille(
        "demand.necessity_elasticity_permille",
        c.necessity_elasticity,
    )?;
    c.necessity_choke = r.permille("demand.necessity_choke_permille", c.necessity_choke)?;
    if c.necessity_choke.raw() <= Fx::ONE_RAW {
        return Err(bad(
            "config.csv: demand.necessity_choke_permille must be above 1000.",
        ));
    }
    c.luxury_elasticity = r.permille("demand.luxury_elasticity_permille", c.luxury_elasticity)?;
    c.max_cheap_demand = r.permille("demand.max_cheap_permille", c.max_cheap_demand)?;
    if c.max_cheap_demand.raw() < Fx::ONE.raw() {
        return Err(bad(
            "config.csv: demand.max_cheap_permille must be >= 1000.",
        ));
    }
    for (g, grade) in GRADES.iter().enumerate() {
        c.grade_luxury_share[g] = r.permille(
            &format!("demand.{grade}.luxury_share_permille"),
            c.grade_luxury_share[g],
        )?;
        c.semi_grade_luxury_share[g] = r.permille(
            &format!("demand.semi.{grade}.luxury_share_permille"),
            c.semi_grade_luxury_share[g],
        )?;
    }
    let mut shares: i64 = 0;
    for q in 0..c.quintile_income_share.len() {
        c.quintile_income_share[q] = r.permille(
            &format!("households.q{}.income_share_permille", q + 1),
            c.quintile_income_share[q],
        )?;
        if c.quintile_income_share[q].raw() <= 0 {
            return Err(bad("config.csv: household income shares must be positive."));
        }
        shares += c.quintile_income_share[q].raw();
    }
    if (shares - Fx::ONE_RAW).unsigned_abs() > c.quintile_income_share.len() as u64 {
        return Err(bad(
            "config.csv: households.q1..q5.income_share_permille must sum to 1000.",
        ));
    }
    c.population_base_growth =
        r.permille("households.base_growth_permille", c.population_base_growth)?;
    c.population_growth_response = r.permille(
        "households.growth_response_permille",
        c.population_growth_response,
    )?;
    c.population_max_growth =
        r.permille("households.max_growth_permille", c.population_max_growth)?;
    c.population_max_decline =
        r.permille("households.max_decline_permille", c.population_max_decline)?;
    c.income_response = r.permille("households.income_response_permille", c.income_response)?;
    c.income_max_change =
        r.permille("households.income_max_change_permille", c.income_max_change)?;
    c.need_memory_weeks = r.int("households.need_memory_weeks", c.need_memory_weeks)?;
    if c.need_memory_weeks < 0 {
        return Err(bad(
            "config.csv: households.need_memory_weeks must be >= 0.",
        ));
    }
    c.household_budget_weeks = r.int("households.budget_weeks", c.household_budget_weeks)?;
    if c.household_budget_weeks < 1 {
        return Err(bad("config.csv: households.budget_weeks must be >= 1."));
    }
    c.household_alms_share = r.permille("households.alms_permille", c.household_alms_share)?;
    c.luxury_wealth_per_year = r.permille(
        "households.luxury_wealth_permille",
        c.luxury_wealth_per_year,
    )?;
    c.luxury_wealth_cap =
        r.permille("households.luxury_wealth_cap_permille", c.luxury_wealth_cap)?;
    if c.luxury_wealth_cap.raw() < Fx::ONE_RAW {
        return Err(bad(
            "config.csv: households.luxury_wealth_cap_permille must be at least 1000.",
        ));
    }
    c.luxury_tax = r.permille("households.luxury_tax_permille", c.luxury_tax)?;
    Ok(())
}
