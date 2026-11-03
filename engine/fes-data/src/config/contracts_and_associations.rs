//! C# `ApplyContractsAndAssociations`.

use super::reader::Reader;
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.contracts_per_nation = r.int("contract.per_nation", c.contracts_per_nation)?;
    c.contract_min_stock = r.int("contract.min_stock", c.contract_min_stock)?;
    c.contract_stock_weeks = r.int("contract.stock_weeks", c.contract_stock_weeks)?;
    c.contract_demand_share =
        r.permille("contract.demand_share_permille", c.contract_demand_share)?;
    c.contract_credit_weeks = r.int("contract.credit_weeks", c.contract_credit_weeks)?;
    c.contract_min_weeks = r.int("contract.min_weeks", c.contract_min_weeks)?;
    c.contract_max_weeks = r.int("contract.max_weeks", c.contract_max_weeks)?;
    c.contract_deposit_share = r.permille("contract.deposit_permille", c.contract_deposit_share)?;
    c.contract_base_discount =
        r.permille("contract.base_discount_permille", c.contract_base_discount)?;
    c.contract_discount_per_unit = r.permille(
        "contract.discount_per_unit_permille",
        c.contract_discount_per_unit,
    )?;
    c.contract_max_discount =
        r.permille("contract.max_discount_permille", c.contract_max_discount)?;
    c.strategic_subsidy_share = r.permille(
        "state.strategic_subsidy_permille",
        c.strategic_subsidy_share,
    )?;
    c.merchant_join_share = r.permille("merchants.join_permille", c.merchant_join_share)?;
    c.merchant_member_fee = r.permille("merchants.member_fee_permille", c.merchant_member_fee)?;
    c.merchant_support_floor =
        r.permille("merchants.support_floor_permille", c.merchant_support_floor)?;
    c.merchant_support_share =
        r.permille("merchants.support_share_permille", c.merchant_support_share)?;
    c.merchant_member_premium = r.permille(
        "merchants.member_premium_permille",
        c.merchant_member_premium,
    )?;
    Ok(())
}
