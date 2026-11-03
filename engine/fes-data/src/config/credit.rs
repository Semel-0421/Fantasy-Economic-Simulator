//! C# `ApplyCredit`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.bill_cap_weeks = r.int("credit.bill_cap_weeks", c.bill_cap_weeks)?;
    c.bill_weeks = r.int("credit.bill_weeks", c.bill_weeks)?;
    c.bill_discount = r.permille("credit.bill_discount_permille", c.bill_discount)?;
    c.bill_repay_share = r.permille("credit.bill_repay_permille", c.bill_repay_share)?;
    c.bill_default_weeks = r.int("credit.bill_default_weeks", c.bill_default_weeks)?;
    c.bill_bank_floor = r.permille("credit.bill_bank_floor_permille", c.bill_bank_floor)?;
    c.bill_sale_discount =
        r.permille("credit.bill_sale_discount_permille", c.bill_sale_discount)?;
    c.bill_producers = r.flag("credit.bill_producers", c.bill_producers)?;
    c.bond_buy_share = r.permille("credit.bond_buy_permille", c.bond_buy_share)?;
    c.bond_keep_weeks = r.int("credit.bond_keep_weeks", c.bond_keep_weeks)?;
    c.bond_group_keep_times = r.int("credit.bond_group_keep_times", c.bond_group_keep_times)?;
    c.state_debt_share = r.permille("credit.state_debt_permille", c.state_debt_share)?;
    c.state_bond_interest =
        r.permille("credit.state_bond_interest_permille", c.state_bond_interest)?;
    c.state_bond_weeks = r.int("credit.state_bond_weeks", c.state_bond_weeks)?;
    c.company_debt_share = r.permille("credit.company_debt_permille", c.company_debt_share)?;
    c.company_bond_interest = r.permille(
        "credit.company_bond_interest_permille",
        c.company_bond_interest,
    )?;
    c.company_bond_weeks = r.int("credit.company_bond_weeks", c.company_bond_weeks)?;
    c.company_default_weeks = r.int("credit.company_default_weeks", c.company_default_weeks)?;
    c.working_weeks = r.int("credit.working_weeks", c.working_weeks)?;
    if c.bill_weeks < 1 || c.state_bond_weeks < 1 || c.company_bond_weeks < 1 {
        return Err(bad("config.csv: credit weeks must be >= 1."));
    }
    Ok(())
}
