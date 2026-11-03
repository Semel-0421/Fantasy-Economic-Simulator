//! C# `ApplyPricesAndCurrency`.

use super::reader::Reader;
use super::SimulationConfig;
use crate::DataError;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.price_adjust_rate = r.permille("price.adjust_permille", c.price_adjust_rate)?;
    c.price_max_step = r.permille("price.max_step_permille", c.price_max_step)?;
    c.price_anchor_rate = r.permille("price.anchor_permille", c.price_anchor_rate)?;
    c.stock_pressure = r.permille("price.stock_pressure_permille", c.stock_pressure)?;
    c.min_price = r.int("price.min", c.min_price)?;
    c.mint_rate_per_year = r.permille("currency.mint_permille", c.mint_rate_per_year)?;
    c.initial_gold_reserve = r.long("currency.gold_reserve", c.initial_gold_reserve)?;
    c.initial_silver_reserve = r.long("currency.silver_reserve", c.initial_silver_reserve)?;
    c.initial_coin_supply = r.long("currency.coin_supply", c.initial_coin_supply)?;
    c.coins_per_gold_unit = r.long("currency.coins_per_gold", c.coins_per_gold_unit)?;
    c.coins_per_silver_unit = r.long("currency.coins_per_silver", c.coins_per_silver_unit)?;
    c.currency_endowment_spread_permille = r.int(
        "currency.endowment_spread_permille",
        c.currency_endowment_spread_permille,
    )?;
    Ok(())
}
