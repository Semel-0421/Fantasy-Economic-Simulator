//! C# `ApplyMigrationAndInvestment`.

use super::reader::Reader;
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.migration_max_share = r.permille("migration.max_share_permille", c.migration_max_share)?;
    c.migration_emancipation_earliest_years = r.int(
        "migration.emancipation_earliest_years",
        c.migration_emancipation_earliest_years,
    )?;
    c.facility_expansion_bonus = r.permille(
        "facility.expansion_bonus_permille",
        c.facility_expansion_bonus,
    )?;
    c.facility_output_wage = r.permille("facility.output_wage_permille", c.facility_output_wage)?;
    c.state_public_wage_share =
        r.permille("state.public_wage_permille", c.state_public_wage_share)?;
    c.facility_max_level = r.int("facility.max_level", c.facility_max_level)?;
    c.facility_unaided_output = r.permille(
        "facility.unaided_output_permille",
        c.facility_unaided_output,
    )?;
    c.investment_payback_years = r.int("investment.payback_years", c.investment_payback_years)?;
    c.investment_max_per_node = r.int("investment.max_per_node", c.investment_max_per_node)?;
    c.investment_projects_per_node = r.int(
        "investment.projects_per_node",
        c.investment_projects_per_node,
    )?;
    c.migration_start_free_share = r.permille(
        "migration.start_free_permille",
        c.migration_start_free_share,
    )?;
    c.migration_flee_share = r.permille("migration.flee_permille", c.migration_flee_share)?;
    c.migration_broken_contract_factor = r.int(
        "migration.broken_contract_factor",
        c.migration_broken_contract_factor,
    )?;
    c.migration_jobs_per_facility_level = r.int(
        "migration.jobs_per_facility_level",
        c.migration_jobs_per_facility_level,
    )?;
    c.migration_vacancy_share =
        r.permille("migration.vacancy_permille", c.migration_vacancy_share)?;
    c.migration_emancipation_lead_years = r.int(
        "migration.emancipation_lead_years",
        c.migration_emancipation_lead_years,
    )?;
    c.migration_freed_per_year = r.permille(
        "migration.freed_per_year_permille",
        c.migration_freed_per_year,
    )?;
    c.migration_redemption_years =
        r.int("migration.redemption_years", c.migration_redemption_years)?;
    c.migration_redemption_share = r.permille(
        "migration.redemption_permille",
        c.migration_redemption_share,
    )?;
    c.population_urban_penalty = r.permille(
        "households.urban_penalty_permille",
        c.population_urban_penalty,
    )?;
    Ok(())
}
