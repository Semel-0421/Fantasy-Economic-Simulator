//! C# `ApplyPolicy`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.policy_step = r.permille("policy.step_permille", c.policy_step)?;
    c.policy_max_steps = r.int("policy.max_steps", c.policy_max_steps)?;
    if c.policy_max_steps < 0 || c.policy_step.mul_int(i64::from(c.policy_max_steps) * 1000) >= 1000
    {
        return Err(bad(
            "config.csv: policy.step_permille x policy.max_steps must stay below 1000.",
        ));
    }
    c.policy_move_threshold =
        r.permille("policy.move_threshold_permille", c.policy_move_threshold)?;
    c.policy_resistance = r.permille("policy.resistance_permille", c.policy_resistance)?;
    c.influence_asset_weight =
        r.permille("policy.asset_weight_permille", c.influence_asset_weight)?;
    c.influence_lobby_weight =
        r.permille("policy.lobby_weight_permille", c.influence_lobby_weight)?;
    c.influence_per_job = r.long("policy.influence_per_job", c.influence_per_job)?;
    c.jobs_per_facility_level =
        r.int("policy.jobs_per_facility_level", c.jobs_per_facility_level)?;
    c.jobs_per_merchant_group =
        r.int("policy.jobs_per_merchant_group", c.jobs_per_merchant_group)?;
    c.npc_lobby_share = r.permille("policy.npc_lobby_share_permille", c.npc_lobby_share)?;
    c.policy_loss_tilt = r.permille("policy.loss_tilt_permille", c.policy_loss_tilt)?;
    c.inflation_opposition = r.permille(
        "policy.inflation_opposition_permille",
        c.inflation_opposition,
    )?;
    Ok(())
}
