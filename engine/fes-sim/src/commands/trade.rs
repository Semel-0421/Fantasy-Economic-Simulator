//! Buying and selling at the player's market and moving cargo (C# `TradeCommands.cs`).
use super::support::{
    change_local_coins, fail, known_good, load_capacity, local_coins, needs_license, nodes, player,
    site_check, withdrawable,
};
use crate::agents::bank_seizure::trader_offer;
use crate::agents::banks::BankState;
use crate::agents::cargo::{warehouse_holder, CargoCapacity, CargoTable, PLAYER_CART};
use crate::agents::finance::FinanceState;
use crate::agents::licenses::LicenseTable;
use crate::agents::purses::PurseState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::economy::MarketStore;
use crate::world::NodeBanks;
use fes_core::WorldLevel;

/// A buy or sell the player made (C# `GoodsTraded`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct GoodsTraded {
    pub market: i32,
    pub good: i32,
    pub bought: bool,
    pub requested: i32,
    pub filled: i32,
    pub price: i32,
    pub turn: i32,
}

/// Goods moved between the cart and a facility's warehouse (C# `CargoMoved`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct CargoMoved {
    pub facility: i32,
    pub good: i32,
    pub to_warehouse: bool,
    pub requested: i32,
    pub moved: i32,
}

/// C# `MarketFlows.Buy`.
pub fn market_buy(markets: &mut MarketStore, cell: usize, quantity: i32) {
    assert!(
        quantity >= 0 && quantity <= markets.stock[cell],
        "Cannot buy {quantity} of stock {}.",
        markets.stock[cell]
    );
    markets.stock[cell] -= quantity;
    markets.supply[cell] = MarketStore::clamp_to_non_negative_int(
        i64::from(markets.supply[cell]) + i64::from(quantity),
    );
    markets.demand[cell] = MarketStore::clamp_to_non_negative_int(
        i64::from(markets.demand[cell]) + i64::from(quantity),
    );
}

/// C# `MarketFlows.Sell`.
pub fn market_sell(markets: &mut MarketStore, cell: usize, quantity: i32) {
    assert!(quantity >= 0, "negative sale");
    markets.supply[cell] = MarketStore::clamp_to_non_negative_int(
        i64::from(markets.supply[cell]) + i64::from(quantity),
    );
}

/// C# `PlayerTrade.Check`: the market cell, or an error.
fn trade_check(ctx: &SimContext, good: i32, quantity: i32) -> Result<usize, &'static str> {
    if quantity <= 0 {
        return Err("Quantity must be positive.");
    }
    let p = player(ctx);
    if p.is_travelling() {
        return Err("Cannot trade while travelling.");
    }
    if !known_good(ctx, good) {
        return Err("Unknown good.");
    }
    ctx.world
        .get::<MarketStore>()
        .try_find_cell(p.node as usize, good)
        .ok_or("This market does not trade that good.")
}

/// Buys goods at the player's market into the cart (C# `BuyGoodsCommand`, DEC-020, DEC-036).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyGoods {
    pub good: i32,
    pub quantity: i32,
    pub pay_from_deposit: bool,
}

impl BuyGoods {
    fn funds(&self, ctx: &SimContext) -> i64 {
        let funds = local_coins(ctx);
        let nation = if self.pay_from_deposit {
            ctx.services
                .get::<NodeBanks>()
                .branch_nation(nodes(ctx), player(ctx).node)
        } else {
            -1
        };
        if nation < 0 {
            return funds;
        }
        let n = nation as usize;
        funds + withdrawable(ctx, n).min(ctx.world.get::<BankState>().reserve[n])
    }

    fn fill(&self, ctx: &SimContext, cell: usize) -> i32 {
        let markets = ctx.world.get::<MarketStore>();
        let load = ctx.world.get::<CargoTable>().load_of(
            PLAYER_CART,
            &markets.good_weight,
            &markets.good_size,
        );
        let g = self.good as usize;
        let mut fill = i64::from(self.quantity).min(i64::from(markets.stock[cell]));
        fill = fill.min(i64::from(load_capacity(ctx).max_additional(
            load,
            markets.good_weight[g],
            markets.good_size[g],
        )));
        fill = fill.min(self.funds(ctx) / i64::from(markets.price[cell]));
        fill.max(0) as i32
    }
}

impl Command for BuyGoods {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        match trade_check(ctx, self.good, self.quantity) {
            Err(e) => fail(e),
            Ok(cell) if self.fill(ctx, cell) > 0 => CommandResult::Ok,
            Ok(_) => fail("Nothing can be bought: no stock, room or cash."),
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Ok(cell) = trade_check(ctx, self.good, self.quantity) else {
            return;
        };
        let filled = self.fill(ctx, cell);
        let node = player(ctx).node;
        let price = ctx.world.get::<MarketStore>().price[cell];
        let cost = i64::from(filled) * i64::from(price);
        let from_coins = cost.min(local_coins(ctx));
        change_local_coins(ctx, -from_coins);
        if cost > from_coins {
            let nation = ctx
                .services
                .get::<NodeBanks>()
                .branch_nation(nodes(ctx), node) as usize;
            let bank = ctx.world.get_mut::<BankState>();
            bank.player_deposit[nation] -= cost - from_coins;
            bank.reserve[nation] -= cost - from_coins;
            let turn = ctx.turn();
            ctx.world.get_mut::<FinanceState>().last_withdraw_turn[nation] = turn;
        }
        ctx.world.get_mut::<PurseState>().change_float(node, cost);
        ctx.world
            .get_mut::<CargoTable>()
            .change(PLAYER_CART, self.good, filled);
        market_buy(ctx.world.get_mut::<MarketStore>(), cell, filled);
        if filled > 0 {
            crate::scenario::rules::note_purchase(&mut ctx.services, self.good, price, filled);
        }
        let turn = ctx.turn();
        ctx.events.publish(GoodsTraded {
            market: node,
            good: self.good,
            bought: true,
            requested: self.quantity,
            filled,
            price,
            turn,
        });
    }
}

/// C# `LicenseRules.PlayerMaySell`.
pub fn player_may_sell(ctx: &SimContext, good: i32, node: i32) -> bool {
    let n = nodes(ctx);
    if !needs_license(&ctx.world, n, ctx.services.get::<NodeBanks>(), good, node) {
        return true;
    }
    let nation = n.ancestor_at(node, WorldLevel::Nation);
    nation >= 0
        && ctx.world.get::<LicenseTable>().is_valid(
            good / fes_core::QualityGrade::COUNT as i32,
            nation,
            ctx.turn(),
        )
}

/// Sells goods from the cart at the player's market (C# `SellGoodsCommand`, DEC-061, DEC-098).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellGoods {
    pub good: i32,
    pub quantity: i32,
}

impl SellGoods {
    /// The market's offer.
    fn market_offer(&self, ctx: &SimContext, cell: usize) -> i32 {
        trader_offer(
            ctx.services.database.config.trader_spread,
            ctx.world.get::<MarketStore>().price[cell],
        )
    }

    /// The best offer: the protected opening's, while it lasts, else the market's.
    fn offer(&self, ctx: &SimContext, cell: usize) -> i32 {
        let offer = self.market_offer(ctx, cell);
        crate::scenario::rules::opening_offer(&ctx.services, self.good, offer)
    }

    fn fill(&self, ctx: &SimContext, cell: usize) -> i32 {
        let held = ctx
            .world
            .get::<CargoTable>()
            .quantity_of(PLAYER_CART, self.good);
        ctx.world.get::<PurseState>().sellable(
            player(ctx).node,
            self.quantity.min(held),
            i64::from(self.offer(ctx, cell)),
        )
    }
}

impl Command for SellGoods {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let cell = match trade_check(ctx, self.good, self.quantity) {
            Err(e) => return fail(e),
            Ok(c) => c,
        };
        if !player_may_sell(ctx, self.good, player(ctx).node) {
            return fail("Selling this good here needs this nation's license.");
        }
        if ctx
            .world
            .get::<CargoTable>()
            .quantity_of(PLAYER_CART, self.good)
            <= 0
        {
            return fail("The cart holds none of this good.");
        }
        if self.fill(ctx, cell) > 0 {
            CommandResult::Ok
        } else {
            fail("The traders here have no money to pay.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Ok(cell) = trade_check(ctx, self.good, self.quantity) else {
            return;
        };
        let filled = self.fill(ctx, cell);
        let node = player(ctx).node;
        // A protected opening pays its offer for the first purchase's units only (spec 5.3).
        let protected = filled.min(crate::scenario::rules::opening_quantity(
            &ctx.services,
            self.good,
        ));
        let parts = [
            (protected, self.offer(ctx, cell)),
            (filled - protected, self.market_offer(ctx, cell)),
        ];
        if filled > 0 {
            crate::scenario::rules::note_sale(&mut ctx.services);
        }
        let paid: i64 = parts
            .iter()
            .map(|&(n, p)| i64::from(n) * i64::from(p))
            .sum();
        change_local_coins(ctx, paid);
        ctx.world.get_mut::<PurseState>().change_float(node, -paid);
        ctx.world
            .get_mut::<CargoTable>()
            .change(PLAYER_CART, self.good, -filled);
        market_sell(ctx.world.get_mut::<MarketStore>(), cell, filled);
        let turn = ctx.turn();
        // One trade per price paid; a sale that filled nothing still reports the best offer.
        let shown = if filled == 0 { &parts[..1] } else { &parts[..] };
        for &(part, price) in shown.iter().filter(|p| p.0 > 0 || filled == 0) {
            ctx.events.publish(GoodsTraded {
                market: node,
                good: self.good,
                bought: false,
                requested: self.quantity,
                filled: part,
                price,
                turn,
            });
        }
    }
}

/// Moves goods between the cart and a player facility's warehouse (C# `MoveCargoCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MoveCargo {
    pub facility: i32,
    pub good: i32,
    pub quantity: i32,
    pub to_warehouse: bool,
}

impl MoveCargo {
    fn fill(&self, ctx: &SimContext) -> i32 {
        let cargo = ctx.world.get::<CargoTable>();
        let markets = ctx.world.get::<MarketStore>();
        let config = &ctx.services.database.config;
        let warehouse = warehouse_holder(self.facility);
        let (from, to) = if self.to_warehouse {
            (PLAYER_CART, warehouse)
        } else {
            (warehouse, PLAYER_CART)
        };
        let capacity = if self.to_warehouse {
            CargoCapacity {
                max_weight: config.warehouse_max_weight,
                max_size: config.warehouse_max_size,
            }
        } else {
            load_capacity(ctx)
        };
        let held = cargo.quantity_of(from, self.good);
        let g = self.good as usize;
        let room = capacity.max_additional(
            cargo.load_of(to, &markets.good_weight, &markets.good_size),
            markets.good_weight[g],
            markets.good_size[g],
        );
        self.quantity.min(held.min(room))
    }
}

impl Command for MoveCargo {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.quantity <= 0 {
            return fail("Quantity must be positive.");
        }
        if !known_good(ctx, self.good) {
            return fail("Unknown good.");
        }
        // Goods can still be taken out of a lost facility's warehouse during the claim (DEC-052).
        if let Some(e) = site_check(ctx, self.facility, !self.to_warehouse) {
            return fail(e);
        }
        if self.fill(ctx) > 0 {
            CommandResult::Ok
        } else {
            fail("Nothing to move, or no room.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let moved = self.fill(ctx);
        let warehouse = warehouse_holder(self.facility);
        let (from, to) = if self.to_warehouse {
            (PLAYER_CART, warehouse)
        } else {
            (warehouse, PLAYER_CART)
        };
        let cargo = ctx.world.get_mut::<CargoTable>();
        cargo.change(from, self.good, -moved);
        cargo.change(to, self.good, moved);
        ctx.events.publish(CargoMoved {
            facility: self.facility,
            good: self.good,
            to_warehouse: self.to_warehouse,
            requested: self.quantity,
            moved,
        });
    }
}
