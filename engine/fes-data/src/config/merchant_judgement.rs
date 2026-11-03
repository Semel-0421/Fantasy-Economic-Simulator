//! C# `ApplyMerchantJudgement`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.merchant_traits_enabled = r.flag("merchants.traits", c.merchant_traits_enabled)?;
    c.merchant_rumors_enabled = r.flag("merchants.rumors", c.merchant_rumors_enabled)?;
    c.merchant_weigh_sales = r.flag("merchants.weigh_sales", c.merchant_weigh_sales)?;
    c.merchant_judge_hops = r.int("merchants.judge_hops", c.merchant_judge_hops)?;
    if c.merchant_judge_hops < 1 {
        return Err(bad("config.csv: merchants.judge_hops must be >= 1."));
    }
    c.merchant_bids = r.flag("merchants.bids", c.merchant_bids)?;
    c.merchant_bids_while_loaded = r.int("merchants.bids", i32::from(c.merchant_bids))? >= 2;
    c.merchant_return_home = r.flag("merchants.return_home", c.merchant_return_home)?;
    c.merchant_judge_coin_units =
        r.int("merchants.judge_coin_units", c.merchant_judge_coin_units)?;
    c.merchant_port_crossers = r.flag("merchants.port_crossers", c.merchant_port_crossers)?;
    c.international_balance = r.flag("bank.international_balance", c.international_balance)?;
    c.trader_spread = r.permille("markets.trader_spread_permille", c.trader_spread)?;
    c.trader_float_keep_weeks = r.int("traders.float_keep_weeks", c.trader_float_keep_weeks)?;
    c.trader_float_payout = r.permille("traders.float_payout_permille", c.trader_float_payout)?;
    c.merchant_flow_wage = r.permille("merchants.flow_wage_permille", c.merchant_flow_wage)?;
    c.merchant_cross_level = r.int("merchants.cross_level", c.merchant_cross_level)?;
    c.merchant_cross_flow = r.permille("merchants.cross_flow_permille", c.merchant_cross_flow)?;
    c.continental_producers = r.int("producers.per_continent", c.continental_producers)?;
    Ok(())
}
