//! C# `ApplyDividendsAndCargo`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.dividend_buffer_weeks = r.int("facility.dividend_buffer_weeks", c.dividend_buffer_weeks)?;
    if c.dividend_buffer_weeks < 0 {
        return Err(bad(
            "config.csv: facility.dividend_buffer_weeks must be >= 0.",
        ));
    }
    c.dividend_share = r.permille("facility.dividend_permille", c.dividend_share)?;
    c.group_payout_share = r.permille("groups.payout_permille", c.group_payout_share)?;
    c.state_reserve_target =
        r.permille("bank.state_reserve_target_permille", c.state_reserve_target)?;
    c.state_spending_share = r.permille("bank.state_spending_permille", c.state_spending_share)?;
    if c.facility_bankrupt_weeks < 1 {
        return Err(bad("config.csv: facility.bankrupt_weeks must be >= 1."));
    }
    c.lost_warehouse_weeks = r.int("cargo.lost_warehouse_weeks", c.lost_warehouse_weeks)?;
    if c.lost_warehouse_weeks < 0 {
        return Err(bad("config.csv: cargo.lost_warehouse_weeks must be >= 0."));
    }
    Ok(())
}
