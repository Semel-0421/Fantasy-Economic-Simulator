//! C# `ApplyTradersAndNobles`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.trader_float_share = r.permille("markets.trader_float_permille", c.trader_float_share)?;
    c.trader_loan_share = r.permille("traders.loan_share_permille", c.trader_loan_share)?;
    c.trader_loan_keep_weeks = r.int("traders.loan_keep_weeks", c.trader_loan_keep_weeks)?;
    c.trader_bank_loan_share =
        r.permille("traders.bank_loan_share_permille", c.trader_bank_loan_share)?;
    c.trader_loan_interest =
        r.permille("traders.loan_interest_permille", c.trader_loan_interest)?;
    c.trader_loan_weeks = r.int("traders.loan_weeks", c.trader_loan_weeks)?;
    if c.trader_loan_weeks < 1 {
        return Err(bad("config.csv: traders.loan_weeks must be >= 1."));
    }
    c.trader_loan_repay_share =
        r.permille("traders.loan_repay_permille", c.trader_loan_repay_share)?;
    c.producer_float_share =
        r.permille("markets.producer_float_permille", c.producer_float_share)?;
    c.noble_royal_share = r.permille("noble.royal_share_permille", c.noble_royal_share)?;
    c.noble_city_share = r.permille("noble.city_share_permille", c.noble_city_share)?;
    c.noble_village_share = r.permille("noble.village_share_permille", c.noble_village_share)?;
    c.noble_aid_share = r.permille("noble.aid_permille", c.noble_aid_share)?;
    c.noble_protection_share = r.permille("noble.protection_permille", c.noble_protection_share)?;
    c.noble_spend_share = r.permille("noble.spend_permille", c.noble_spend_share)?;
    c.village_wage_away_share = r.permille(
        "labor.village_wage_away_permille",
        c.village_wage_away_share,
    )?;
    Ok(())
}
