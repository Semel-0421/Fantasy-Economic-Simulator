//! C# `ApplyStateProjects`.

use super::reader::Reader;
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.state_project_share = r.permille("state.project_share_permille", c.state_project_share)?;
    c.state_projects_per_year = r.int("state.projects_per_year", c.state_projects_per_year)?;
    c.state_project_weeks = r.int("state.project_weeks", c.state_project_weeks)?;
    c.state_road_score = r.int("state.road_score_permille", c.state_road_score)?;
    c.state_farmland_cost = r.long("state.farmland_cost", c.state_farmland_cost)?;
    c.state_mine_cost = r.long("state.mine_cost", c.state_mine_cost)?;
    c.state_harbor_cost = r.long("state.harbor_cost", c.state_harbor_cost)?;
    c.state_highway_cost_per_turn =
        r.long("state.highway_cost_per_turn", c.state_highway_cost_per_turn)?;
    c.state_land_bonus = r.permille("state.land_bonus_permille", c.state_land_bonus)?;
    c.state_land_max = r.int("state.land_max", c.state_land_max)?;
    c.state_harbor_bonus = r.permille("state.harbor_bonus_permille", c.state_harbor_bonus)?;
    c.state_harbor_max = r.int("state.harbor_max", c.state_harbor_max)?;
    c.state_highway_bonus = r.permille("state.highway_bonus_permille", c.state_highway_bonus)?;
    c.state_facility_levy = r.permille("state.facility_levy_permille", c.state_facility_levy)?;
    Ok(())
}
