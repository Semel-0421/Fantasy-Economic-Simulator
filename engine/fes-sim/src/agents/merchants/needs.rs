//! C# `HouseholdNeeds` (DEC-124): per market cell, the necessities its households wish to buy this
//! week at the price that nothing at hand covers (their stores, the stock, this week's supply).
//! Arriving merchants sell into it first. A pure function of saved state, refreshed every turn.
use crate::context::SimContext;
use crate::economy::pricing::demand::{cell_base, weekly, DemandParams};
use crate::economy::pricing::luxury_share;
use crate::economy::{CurrencyState, HouseholdCash, HouseholdState, MarketStore};
use crate::world::NodeTable;
use fes_core::Fx;

/// Fills `units` (one entry per market cell).
pub fn refresh_needs(ctx: &SimContext, units: &mut Vec<i64>) {
    let db = &ctx.services.database;
    let config = &db.config;
    let markets = ctx.world.get::<MarketStore>();
    let nodes = ctx.services.get::<NodeTable>();
    let population = ctx.world.get::<HouseholdState>();
    let home_grown = &ctx.world.get::<HouseholdCash>().home_grown;
    let currency = ctx.world.get::<CurrencyState>();
    let demand = DemandParams::from_config(config);
    units.clear();
    units.resize(markets.cell_count(), 0);
    for n in 0..markets.market_count() {
        let level = usize::from(nodes.level[n]);
        let start = markets.market_cell_start[n] as usize;
        let end = start + markets.market_cell_count[n] as usize;
        let index = currency.price_index[nodes.continent[n] as usize].raw();
        let necessity = population.necessity_weight(n, level, config).raw();
        let luxury = population.luxury_weight(n, level, config).raw();
        for c in start..end {
            if !config.merchant_sell_to_households {
                continue;
            }
            let good = markets.cell_good[c] as usize;
            let share = luxury_share(config, &db.goods[good]).raw();
            if share >= Fx::ONE_RAW {
                continue;
            }
            let def = &db.goods[good];
            let level_weight = config.level_demand_weight[level].raw();
            let (base, anchor) = cell_base(
                def.base_demand,
                def.base_price,
                level_weight,
                Fx::ONE_RAW,
                index,
            );
            let wished = weekly(
                &demand,
                base,
                markets.price[c],
                anchor,
                share,
                necessity,
                luxury,
            )
            .met;
            let at_hand = i64::from(home_grown[c])
                + i64::from(markets.stock[c])
                + i64::from(markets.supply[c]);
            units[c] = (wished - at_hand).max(0);
        }
    }
}
