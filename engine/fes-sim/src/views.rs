//! Plain-copy views of the player's position for the front end (spec 7.2): each is `Clone + Send`,
//! built from the components that own the data, and holds no reference into the world.
use crate::agents::asset_prices::{share_price, venture_price, Refs, PLAYER_HOLDER};
use crate::agents::banks::{BankState, LoanTable};
use crate::agents::cargo::{warehouse_holder, CargoTable, PLAYER_CART};
use crate::agents::contracts::ContractTable;
use crate::agents::credit::BondTable;
use crate::agents::facilities::{FacilityTable, OWNER_PLAYER};
use crate::agents::finance::FinanceState;
use crate::agents::licenses::LicenseTable;
use crate::agents::player::PlayerState;
use crate::agents::securities::{ShareTable, ShortTable, VentureTable};
use crate::agents::ship::PlayerShip;
use crate::commands::bonds::BOND_PLAYER_HOLDER;
use crate::context::SimContext;
use crate::world::{NodeBanks, NodeTable};

/// One loan of the player (C# `LoanTable` row).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LoanView {
    pub nation: i32,
    pub balance: i64,
    pub weekly_payment: i64,
    pub due: i64,
    pub overdue_weeks: i32,
}

/// Coins, deposits, loans and bonds of the player.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PurseView {
    pub node: i32,
    pub local_continent: i32,
    /// Coins per continent currency.
    pub coins: Vec<i64>,
    pub travel_destination: i32,
    pub arrival_turn: i32,
    /// Deposit per nation's bank.
    pub deposits: Vec<i64>,
    pub loans: Vec<LoanView>,
    /// (issuer, face) of the bonds the player holds.
    pub bonds: Vec<(i32, i64)>,
}

/// Goods in the cart and in the warehouses of the player's facilities.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CargoView {
    /// (good, quantity) in the cart.
    pub cart: Vec<(i32, i32)>,
    /// (facility, good, quantity) in warehouses the player owns or may still empty.
    pub warehouses: Vec<(i32, i32, i32)>,
    pub ship_owned: bool,
    pub ship_docked_at: i32,
}

/// One supply contract (C# `ContractTable` row).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ContractView {
    pub seller: i32,
    pub buyer: i32,
    pub from_node: i32,
    pub to_node: i32,
    pub good: i32,
    pub quantity: i32,
    pub unit_price: i64,
    pub end_turn: i32,
    pub prepaid: i64,
}

/// One holding of facility or venture shares, or one short position.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct HoldingView {
    pub venture: bool,
    pub asset: i32,
    pub count: i32,
    pub price: i64,
}

/// The player's securities.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SharesView {
    pub shares: Vec<HoldingView>,
    pub ventures: Vec<HoldingView>,
    /// Shorts, with the nation index of the bank holding the margin in `bank`.
    pub shorts: Vec<(HoldingView, i32)>,
}

/// One license (C# `LicenseInfo`); a negative `base_good` is a trade licence kind.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LicenceView {
    pub base_good: i32,
    pub nation: i32,
    pub expires_turn: i32,
}

/// One nation's or continent's published yearly figures, for the results screen.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct StatementView {
    pub turn: i32,
    pub published_year: i32,
    pub nation_total: Vec<i64>,
    /// Raw Fx per nation.
    pub reserve_ratio: Vec<i64>,
    pub loan_rate: Vec<i64>,
    pub continent_total: Vec<i64>,
    pub price_index: Vec<i64>,
    pub inflation: Vec<i64>,
    /// Weekly money records: (turn, per-nation totals).
    pub records: Vec<(i32, Vec<i64>)>,
}

pub fn purse(ctx: &SimContext) -> PurseView {
    let p = ctx.world.get::<PlayerState>();
    let nodes = ctx.services.get::<NodeTable>();
    let loans = ctx.world.get::<LoanTable>();
    let bonds = ctx.world.get::<BondTable>();
    PurseView {
        node: p.node,
        local_continent: nodes.continent[p.node as usize],
        coins: p.coins.clone(),
        travel_destination: p.travel_destination,
        arrival_turn: p.arrival_turn,
        deposits: ctx.world.get::<BankState>().player_deposit.clone(),
        loans: (0..loans.count())
            .map(|r| LoanView {
                nation: loans.nation[r],
                balance: loans.balance[r],
                weekly_payment: loans.weekly_payment[r],
                due: loans.due[r],
                overdue_weeks: loans.overdue_weeks[r],
            })
            .collect(),
        bonds: (0..bonds.count())
            .filter(|&r| bonds.holder[r] == BOND_PLAYER_HOLDER && bonds.face[r] != 0)
            .map(|r| (bonds.issuer[r], bonds.face[r]))
            .collect(),
    }
}

pub fn cargo(ctx: &SimContext) -> CargoView {
    let cargo = ctx.world.get::<CargoTable>();
    let facilities = ctx.world.get::<FacilityTable>();
    let mut warehouses = Vec::new();
    for f in 0..facilities.count() {
        if facilities.owner[f] != OWNER_PLAYER && facilities.warehouse_claim_until[f] < 0 {
            continue;
        }
        for (good, quantity) in cargo.collect(warehouse_holder(f as i32)) {
            warehouses.push((f as i32, good, quantity));
        }
    }
    let ship = ctx.world.get::<PlayerShip>();
    CargoView {
        cart: cargo.collect(PLAYER_CART),
        warehouses,
        ship_owned: ship.owned,
        ship_docked_at: ship.docked_at,
    }
}

/// Every standing supply contract.
pub fn contracts(ctx: &SimContext) -> Vec<ContractView> {
    let c = ctx.world.get::<ContractTable>();
    let markets = ctx.world.get::<crate::economy::MarketStore>();
    (0..c.count())
        .map(|r| ContractView {
            seller: c.seller[r],
            buyer: c.buyer[r],
            from_node: c.from_node[r],
            to_node: c.to_node[r],
            good: markets.cell_good[c.from_cell[r] as usize],
            quantity: c.quantity[r],
            unit_price: c.unit_price[r],
            end_turn: c.end_turn[r],
            prepaid: c.prepaid[r],
        })
        .collect()
}

pub fn shares(ctx: &SimContext) -> SharesView {
    let r = Refs {
        db: &ctx.services.database,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    };
    let world = &ctx.world;
    let table = world.get::<ShareTable>();
    let shares = (0..table.count())
        .filter(|&s| table.holder[s] == PLAYER_HOLDER && table.count[s] > 0)
        .map(|s| HoldingView {
            venture: false,
            asset: table.facility[s],
            count: table.count[s],
            price: share_price(world, r, table.facility[s] as usize),
        })
        .collect();
    let v = world.get::<VentureTable>();
    let ventures = (0..v.count())
        .filter(|&i| v.player_shares[i] > 0)
        .map(|i| HoldingView {
            venture: true,
            asset: i as i32,
            count: v.player_shares[i],
            price: venture_price(world, &r.db.config, i),
        })
        .collect();
    let s = world.get::<ShortTable>();
    let shorts = (0..s.count())
        .map(|i| {
            let venture = s.venture[i] != 0;
            let price = if venture {
                venture_price(world, &r.db.config, s.asset[i] as usize)
            } else {
                share_price(world, r, s.asset[i] as usize)
            };
            let holding = HoldingView {
                venture,
                asset: s.asset[i],
                count: s.shares[i],
                price,
            };
            (holding, s.bank[i])
        })
        .collect();
    SharesView {
        shares,
        ventures,
        shorts,
    }
}

/// The player's licenses, goods and trade licences alike (C# `LicenseSystem.Collect` keeps only
/// goods; filter on `base_good >= 0` for that list).
pub fn licences(ctx: &SimContext) -> Vec<LicenceView> {
    let l = ctx.world.get::<LicenseTable>();
    (0..l.count())
        .map(|r| LicenceView {
            base_good: l.base_good[r],
            nation: l.nation[r],
            expires_turn: l.expires_turn[r],
        })
        .collect()
}

pub fn statement(ctx: &SimContext) -> StatementView {
    let f = ctx.world.get::<FinanceState>();
    StatementView {
        turn: ctx.turn(),
        published_year: f.published_year,
        nation_total: f.published_nation_total.clone(),
        reserve_ratio: f.published_reserve_ratio.clone(),
        loan_rate: f.published_loan_rate.clone(),
        continent_total: f.published_continent_total.clone(),
        price_index: f.published_price_index.clone(),
        inflation: f.published_inflation.clone(),
        records: f
            .records
            .iter()
            .map(|r| (r.turn, r.nation_total.clone()))
            .collect(),
    }
}
