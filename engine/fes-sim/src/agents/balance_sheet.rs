//! The player's collateral and debt, converted into one continent's coin (C# `PlayerBalanceSheet`,
//! DEC-037, DEC-038, DEC-130, DEC-132).
use super::asset_prices::{
    facility_value, land_price, share_price, short_required, venture_price, Refs, PLAYER_HOLDER,
};
use super::banks::{BankState, LoanTable};
use super::cargo::{guild_hall_of, warehouse_holder, CargoTable, PLAYER_CART};
use super::facilities::{FacilityTable, OWNER_PLAYER};
use super::groups::GroupTable;
use super::land::LandState;
use super::player::PlayerState;
use super::production::lines::find_nearest_market;
use super::securities::{ShareTable, VentureTable};
use super::ship::PlayerShip;
use crate::economy::{CurrencyState, MarketStore};
use crate::world::WorldState;

/// C# `NationContinent`.
pub fn nation_continent(r: Refs, nation: i32) -> i32 {
    r.nodes.continent[r.banks.nation_node(r.nodes, nation) as usize]
}

/// C# `Convert`: `amount` of `from` coins in `to` coins.
pub fn convert(world: &WorldState, amount: i64, from: i32, to: i32) -> i64 {
    world
        .get::<CurrencyState>()
        .convert(amount, from as usize, to as usize)
}

/// C# `CargoValue`: a holder's cargo at the prices of the market nearest to `node`.
pub fn cargo_value(world: &WorldState, r: Refs, holder: i32, node: i32) -> i64 {
    let markets = world.get::<MarketStore>();
    world
        .get::<CargoTable>()
        .collect(holder)
        .iter()
        .filter_map(|&(good, quantity)| {
            find_nearest_market(r.nodes, markets, node, good)
                .map(|(_, cell)| i64::from(quantity) * i64::from(markets.price[cell]))
        })
        .sum()
}

/// C# `CollateralValue`: deposits beyond the short margin, player facilities and their warehouses,
/// the cart, the ship, guild-hall goods, shares, land and venture shares.
pub fn collateral_value(world: &WorldState, r: Refs, continent: i32) -> i64 {
    let mut value = 0;
    let deposits = world.get::<BankState>().player_deposit.clone();
    for (n, deposit) in deposits.iter().enumerate() {
        let required = short_required(world, r, n as i32, r.db.config.short_open_share);
        let from = nation_continent(r, n as i32);
        value += convert(world, (deposit - required).max(0), from, continent);
    }
    let facilities = world.get::<FacilityTable>();
    for f in 0..facilities.count() {
        if facilities.owner[f] != OWNER_PLAYER {
            continue;
        }
        let at = facilities.node[f];
        let from = r.nodes.continent[at as usize];
        value += convert(world, facility_value(r.db, facilities, f), from, continent);
        let stored = cargo_value(world, r, warehouse_holder(f as i32), at);
        value += convert(world, stored, from, continent);
    }
    let player = world.get::<PlayerState>();
    let local = r.nodes.continent[player.node as usize];
    let cart = cargo_value(world, r, PLAYER_CART, player.node);
    value += convert(world, cart, local, continent);
    let ship = world.get::<PlayerShip>();
    if ship.owned {
        value += convert(
            world,
            r.db.config.ship_price,
            ship.bought_continent,
            continent,
        );
    }
    for holder in world.get::<CargoTable>().collect_holders() {
        let Some(guild) = guild_hall_of(holder) else {
            continue;
        };
        let city = world.get::<GroupTable>().node[guild as usize];
        let goods = cargo_value(world, r, holder, city);
        value += convert(world, goods, r.nodes.continent[city as usize], continent);
    }
    value + asset_value(world, r, continent)
}

fn asset_value(world: &WorldState, r: Refs, continent: i32) -> i64 {
    let mut value = 0;
    let shares = world.get::<ShareTable>();
    let facilities = world.get::<FacilityTable>();
    for s in 0..shares.count() {
        if shares.holder[s] != PLAYER_HOLDER {
            continue;
        }
        let f = shares.facility[s] as usize;
        let worth = i64::from(shares.count[s]) * share_price(world, r, f);
        let from = r.nodes.continent[facilities.node[f] as usize];
        value += convert(world, worth, from, continent);
    }
    let land = world.get::<LandState>();
    for n in 0..land.player_plots.len() {
        if land.player_plots[n] > 0 {
            let worth =
                i64::from(land.player_plots[n]) * land_price(world, r.nodes, &r.db.config, n);
            value += convert(world, worth, r.nodes.continent[n], continent);
        }
    }
    let ventures = world.get::<VentureTable>();
    for v in 0..ventures.count() {
        if ventures.player_shares[v] > 0 {
            let worth =
                i64::from(ventures.player_shares[v]) * venture_price(world, &r.db.config, v);
            value += convert(world, worth, ventures.continent[v], continent);
        }
    }
    value
}

/// C# `Debt`: all loan balances.
pub fn debt(world: &WorldState, r: Refs, continent: i32) -> i64 {
    let loans = world.get::<LoanTable>();
    (0..loans.count())
        .map(|l| {
            let from = nation_continent(r, loans.nation[l]);
            convert(world, loans.balance[l], from, continent)
        })
        .sum()
}

/// C# `MarginShortfall`: how far the debt is above the loan-to-value of the collateral.
pub fn margin_shortfall(world: &WorldState, r: Refs, continent: i32) -> i64 {
    debt(world, r, continent)
        - r.db
            .config
            .loan_to_value
            .mul_int(collateral_value(world, r, continent))
}

/// C# `CreditLimit`: how much more the player may borrow.
pub fn credit_limit(world: &WorldState, r: Refs, continent: i32) -> i64 {
    (-margin_shortfall(world, r, continent)).max(0)
}
