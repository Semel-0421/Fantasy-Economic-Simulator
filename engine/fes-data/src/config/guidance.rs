//! C# `ApplyGuidance`.

use super::reader::Reader;
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.guidance_decay_per_turn = r.permille("guidance.decay_permille", c.guidance_decay_per_turn)?;
    c.guidance_weight_buy_facility = r.permille(
        "guidance.weight.buy_facility_permille",
        c.guidance_weight_buy_facility,
    )?;
    c.guidance_weight_found_company = r.permille(
        "guidance.weight.found_company_permille",
        c.guidance_weight_found_company,
    )?;
    c.guidance_weight_buy_license = r.permille(
        "guidance.weight.buy_license_permille",
        c.guidance_weight_buy_license,
    )?;
    Ok(())
}
