//! C# `ApplyGroupsAndBanks`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    const GROUP_KEYS: [&str; 4] = ["company", "guild", "state", "private"];
    for (k, key) in GROUP_KEYS.iter().enumerate() {
        c.group_start_cash[k] =
            r.long(&format!("groups.{key}.start_cash"), c.group_start_cash[k])?;
    }
    c.companies_per_nation = r.int("groups.companies_per_nation", c.companies_per_nation)?;
    c.guild_fee = r.long("guild.fee", c.guild_fee)?;
    c.guild_insurance_discount = r.permille(
        "guild.insurance_discount_permille",
        c.guild_insurance_discount,
    )?;
    c.guild_license_discount =
        r.permille("guild.license_discount_permille", c.guild_license_discount)?;
    c.guild_hall_max_weight = r.long("guild.hall_max_weight", c.guild_hall_max_weight)?;
    c.guild_hall_max_size = r.long("guild.hall_max_size", c.guild_hall_max_size)?;
    c.bank_initial_reserve_share = r.permille(
        "bank.initial_reserve_permille",
        c.bank_initial_reserve_share,
    )?;
    c.deposit_interest_per_year = r.permille(
        "bank.deposit_interest_permille",
        c.deposit_interest_per_year,
    )?;
    c.exchange_fee = r.permille("bank.exchange_fee_permille", c.exchange_fee)?;
    c.loan_interest_per_year =
        r.permille("bank.loan_interest_permille", c.loan_interest_per_year)?;
    c.loan_term_weeks = r.int("bank.loan_term_weeks", c.loan_term_weeks)?;
    if c.loan_term_weeks < 1 {
        return Err(bad("config.csv: bank.loan_term_weeks must be >= 1."));
    }
    c.loan_to_value = r.permille("bank.loan_to_value_permille", c.loan_to_value)?;
    c.loan_grace_weeks = r.int("bank.grace_weeks", c.loan_grace_weeks)?;
    c.margin_call_share = r.permille("bank.margin_call_permille", c.margin_call_share)?;
    c.margin_call_weeks = r.int("bank.margin_call_weeks", c.margin_call_weeks)?;
    if c.margin_call_share.raw() < c.loan_to_value.raw() || c.margin_call_weeks < 0 {
        return Err(bad("config.csv: bank.margin_call_permille must be >= bank.loan_to_value_permille, and bank.margin_call_weeks >= 0."));
    }
    c.auction_weeks = r.int("bank.auction_weeks", c.auction_weeks)?;
    if c.auction_weeks < 1 {
        return Err(bad("config.csv: bank.auction_weeks must be >= 1."));
    }
    c.auction_reserve_share =
        r.permille("bank.auction_reserve_permille", c.auction_reserve_share)?;
    Ok(())
}
