//! C# `ApplyAssetMarkets`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;
use fes_core::Fx;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.land_rent_ppm = r.long("land.rent_ppm", c.land_rent_ppm)?;
    c.land_rent_years = r.int("land.rent_years", c.land_rent_years)?;
    c.crunch_lifts_rates = r.flag("finance.crunch_lifts_rates", c.crunch_lifts_rates)?;
    c.group_list_premium = r.permille("share.group_list_premium_permille", c.group_list_premium)?;
    c.group_buyback_premium = r.permille(
        "share.group_buyback_premium_permille",
        c.group_buyback_premium,
    )?;
    if c.land_rent_ppm < 0
        || c.land_rent_years < 0
        || (c.group_list_premium.raw() > 0 && c.group_list_premium <= c.group_buyback_premium)
    {
        return Err(bad("config.csv: land rent must not be negative, and groups list above where they buy back."));
    }
    c.short_licence_fee = r.long("short.licence_fee", c.short_licence_fee)?;
    c.short_licence_deposit = r.long("short.licence_deposit", c.short_licence_deposit)?;
    c.short_licence_weeks = r.int("short.licence_weeks", c.short_licence_weeks)?;
    c.short_open_share = r.permille("short.open_permille", c.short_open_share)?;
    c.short_call_share = r.permille("short.call_permille", c.short_call_share)?;
    c.short_fee = r.permille("short.fee_permille", c.short_fee)?;
    c.short_max_lent_shares = r.int("short.max_lent_shares", c.short_max_lent_shares)?;
    if c.short_licence_fee < 0
        || c.short_licence_deposit < 0
        || c.short_licence_weeks < 0
        || c.short_call_share < Fx::ONE
        || c.short_open_share < c.short_call_share
        || c.short_max_lent_shares < 0
    {
        return Err(bad("config.csv: short selling needs open >= call >= 1000 permille, and no negative values."));
    }
    c.ventures_per_continent = r.int("venture.per_continent", c.ventures_per_continent)?;
    c.venture_shares = r.int("venture.shares", c.venture_shares)?;
    c.venture_capital = r.long("venture.capital", c.venture_capital)?;
    c.venture_voyage_weeks = r.int("venture.voyage_weeks", c.venture_voyage_weeks)?;
    c.venture_loss_permille = r.int("venture.loss_permille", c.venture_loss_permille)?;
    c.venture_min_margin = r.permille("venture.min_margin_permille", c.venture_min_margin)?;
    c.venture_value_years = r.int("venture.value_years", c.venture_value_years)?;
    if c.ventures_per_continent < 0
        || c.venture_shares < 1
        || c.venture_capital < 1
        || c.venture_voyage_weeks < 1
        || c.venture_loss_permille < 0
        || c.venture_loss_permille > 1000
        || c.venture_value_years < 0
    {
        return Err(bad("config.csv: venture values out of range."));
    }
    markets(c, r)
}

fn markets(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.market_credit_share = r.permille("market.credit_share_permille", c.market_credit_share)?;
    c.market_credit_weight = r.permille("market.credit_weight_permille", c.market_credit_weight)?;
    c.market_momentum = r.permille("market.momentum_permille", c.market_momentum)?;
    c.market_reversion = r.permille("market.reversion_permille", c.market_reversion)?;
    c.market_weekly_move = r.permille("market.weekly_move_permille", c.market_weekly_move)?;
    c.market_premium_floor = r.permille("market.premium_floor_permille", c.market_premium_floor)?;
    c.market_premium_cap = r.permille("market.premium_cap_permille", c.market_premium_cap)?;
    c.market_idle_weight = r.permille("market.idle_weight_permille", c.market_idle_weight)?;
    c.market_property_idle_weight = r.permille(
        "market.property_idle_weight_permille",
        c.market_property_idle_weight,
    )?;
    c.market_venture_idle_weight = r.permille(
        "market.venture_idle_weight_permille",
        c.market_venture_idle_weight,
    )?;
    c.market_repay_share = r.permille("market.repay_permille", c.market_repay_share)?;
    c.market_crunch_share =
        r.permille("market.crunch_debt_share_permille", c.market_crunch_share)?;
    c.market_heated_premium = r.permille("market.heated_permille", c.market_heated_premium)?;
    c.market_burst_premium = r.permille("market.burst_permille", c.market_burst_premium)?;
    c.market_leverage_limit =
        r.permille("market.leverage_limit_permille", c.market_leverage_limit)?;
    c.market_burst_drop = r.permille("market.burst_drop_permille", c.market_burst_drop)?;
    if c.market_premium_floor.raw() <= 0
        || c.market_premium_floor > Fx::ONE
        || c.market_premium_cap < c.market_burst_premium
        || c.market_heated_premium > c.market_burst_premium
        || c.market_burst_drop.raw() < 0
        || c.market_burst_drop >= Fx::ONE
    {
        return Err(bad("config.csv: market lines must keep floor <= 1 <= heated <= burst <= cap, and a drop below 1."));
    }
    Ok(())
}
