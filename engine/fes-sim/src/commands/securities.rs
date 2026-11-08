//! Shares, venture shares and short sales (C# `Shares.cs`, `Ventures.cs` and `Shorts.cs`
//! commands; DEC-129, DEC-130, DEC-132, DEC-133).
use super::facility::coins_at;
use super::support::{fail, nodes, player, refs};
use crate::agents::asset_prices::{
    households_holder, sell_venture_shares_for_player, share_price, venture_price, Refs,
    PLAYER_HOLDER,
};
use crate::agents::banks::BankState;
use crate::agents::bond_desk::take_from_households;
use crate::agents::credit::BondOffers;
use crate::agents::facilities::{FacilityTable, OWNER_NPC};
use crate::agents::groups::GroupTable;
use crate::agents::money_ledger::pay_nation_households;
use crate::agents::player::PlayerState;
use crate::agents::securities::{
    AssetMarkets, ShareTable, ShortLicences, ShortTable, VentureTable,
};
use crate::agents::shorts::{buy_back, short_bank, short_households, short_price, short_value};
use crate::agents::ventures::household_shares;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::world::{NodeBanks, NodeTable, WorldState};

/// C# `AssetMarketKind` values a short licence names.
pub const MARKET_STOCK: u8 = 0;
pub const MARKET_VENTURE: u8 = 2;

fn change_local(ctx: &mut SimContext, delta: i64) {
    let node = player(ctx).node;
    let c = ctx.services.get::<NodeTable>().continent[node as usize] as usize;
    ctx.world.get_mut::<PlayerState>().coins[c] += delta;
}

/// C# `ShortRules.Market`: the asset market an asset trades on.
pub fn short_market(world: &WorldState, r: Refs, venture: bool, asset: i32) -> i32 {
    if venture {
        let continent = world.get::<VentureTable>().continent[asset as usize];
        world.get::<AssetMarkets>().venture(continent) as i32
    } else {
        // C# `AssetMarkets.Stock(nation)` is the nation index itself.
        r.banks
            .nation_of(r.nodes, world.get::<FacilityTable>().node[asset as usize])
    }
}

/// C# `ShortRules.Traded`.
fn traded(ctx: &mut SimContext, venture: bool, asset: i32) {
    let market = short_market(&ctx.world, refs(ctx), venture, asset);
    let turn = ctx.turn();
    let licences = ctx.world.get_mut::<ShortLicences>();
    if market >= 0 && licences.first_trade[market as usize] < 0 {
        licences.first_trade[market as usize] = turn;
    }
}

/// C# `Exchange.Check`: shares trade at a branch of the facility's nation's bank.
fn exchange_check(ctx: &SimContext, facility: i32) -> Option<&'static str> {
    let facilities = ctx.world.get::<FacilityTable>();
    if facility < 0 || facility as usize >= facilities.count() {
        return Some("Unknown facility.");
    }
    let f = facility as usize;
    if facilities.owner[f] != OWNER_NPC || facilities.group[f] < 0 || facilities.level[f] == 0 {
        return Some("This facility has no shares for sale.");
    }
    if ctx.services.database.facilities[facilities.def_index[f] as usize].state_owned {
        return Some("The state does not sell shares of this facility.");
    }
    let p = player(ctx);
    let banks = ctx.services.get::<NodeBanks>();
    let nation = banks.nation_of(nodes(ctx), facilities.node[f]);
    if !p.is_travelling() && nation >= 0 && banks.branch_nation(nodes(ctx), p.node) == nation {
        None
    } else {
        Some("Shares of this facility trade at its nation's bank.")
    }
}

/// C# `ContinentalExchange.Check`.
fn continental_check(ctx: &SimContext, venture: i32) -> Option<&'static str> {
    let ventures = ctx.world.get::<VentureTable>();
    if venture < 0 || venture as usize >= ventures.count() {
        return Some("Unknown venture.");
    }
    let p = player(ctx);
    let exchange = ctx.world.get::<AssetMarkets>().exchange_node(
        nodes(ctx),
        ctx.services.get::<NodeBanks>(),
        ventures.continent[venture as usize],
    );
    if !p.is_travelling() && exchange >= 0 && p.node == exchange {
        None
    } else {
        Some("Venture shares trade at the continent's exchange.")
    }
}

fn share_cost(ctx: &SimContext, facility: i32, count: i32) -> i64 {
    i64::from(count) * share_price(&ctx.world, refs(ctx), facility as usize)
}

/// Buys shares from the facility's group at the share price (C# `BuyShareCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyShare {
    pub facility: i32,
    pub count: i32,
}

impl Command for BuyShare {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        if let Some(e) = exchange_check(ctx, self.facility) {
            return fail(e);
        }
        let config = &ctx.services.database.config;
        if ctx.world.get::<ShareTable>().outside(self.facility) + self.count
            > config.facility_max_listed_shares
        {
            return fail("The group keeps control of it.");
        }
        if coins_at(ctx, player(ctx).node) >= share_cost(ctx, self.facility, self.count) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let cost = share_cost(ctx, self.facility, self.count);
        change_local(ctx, -cost);
        let group = ctx.world.get::<FacilityTable>().group[self.facility as usize];
        ctx.world.get_mut::<GroupTable>().cash[group as usize] += cost;
        ctx.world
            .get_mut::<ShareTable>()
            .add(self.facility, PLAYER_HOLDER, self.count);
        traded(ctx, false, self.facility);
    }
}

/// Sells shares back to the facility's group (C# `SellShareCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellShare {
    pub facility: i32,
    pub count: i32,
}

impl Command for SellShare {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        if let Some(e) = exchange_check(ctx, self.facility) {
            return fail(e);
        }
        if ctx
            .world
            .get::<ShareTable>()
            .held(self.facility, PLAYER_HOLDER)
            < self.count
        {
            return fail("The player holds fewer shares.");
        }
        let group = ctx.world.get::<FacilityTable>().group[self.facility as usize];
        if ctx.world.get::<GroupTable>().cash[group as usize]
            >= share_cost(ctx, self.facility, self.count)
        {
            CommandResult::Ok
        } else {
            fail("The group cannot pay for them.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let value = share_cost(ctx, self.facility, self.count);
        change_local(ctx, value);
        let group = ctx.world.get::<FacilityTable>().group[self.facility as usize];
        ctx.world.get_mut::<GroupTable>().cash[group as usize] -= value;
        ctx.world
            .get_mut::<ShareTable>()
            .add(self.facility, PLAYER_HOLDER, -self.count);
        traded(ctx, false, self.facility);
    }
}

/// Buys venture shares from its households (C# `BuyVentureShareCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyVentureShare {
    pub venture: i32,
    pub count: i32,
}

impl BuyVentureShare {
    fn cost(&self, ctx: &SimContext) -> i64 {
        i64::from(self.count)
            * venture_price(
                &ctx.world,
                &ctx.services.database.config,
                self.venture as usize,
            )
    }
}

impl Command for BuyVentureShare {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        if let Some(e) = continental_check(ctx, self.venture) {
            return fail(e);
        }
        let config = &ctx.services.database.config;
        if household_shares(
            ctx.world.get::<VentureTable>(),
            config,
            self.venture as usize,
        ) < self.count
        {
            return fail("Not so many shares are for sale.");
        }
        if coins_at(ctx, player(ctx).node) >= self.cost(ctx) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let cost = self.cost(ctx);
        change_local(ctx, -cost);
        let nation = short_households(&ctx.world, refs(ctx), true, self.venture);
        pay_nation_households(&mut ctx.world, nation, cost);
        ctx.world.get_mut::<VentureTable>().player_shares[self.venture as usize] += self.count;
        traded(ctx, true, self.venture);
    }
}

/// Sells venture shares back to its households, as far as their idle money goes (C#
/// `SellVentureShareCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellVentureShare {
    pub venture: i32,
    pub count: i32,
}

impl Command for SellVentureShare {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        if let Some(e) = continental_check(ctx, self.venture) {
            return fail(e);
        }
        if ctx.world.get::<VentureTable>().player_shares[self.venture as usize] >= self.count {
            CommandResult::Ok
        } else {
            fail("The player holds fewer shares.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let raised = {
            let r = Refs {
                db: &ctx.services.database,
                nodes: ctx.services.get::<NodeTable>(),
                banks: ctx.services.get::<NodeBanks>(),
            };
            sell_venture_shares_for_player(
                &mut ctx.world,
                &mut offers,
                r,
                turn,
                self.venture as usize,
                self.count,
            )
        };
        *ctx.services.get_mut::<BondOffers>() = offers;
        change_local(ctx, raised);
        traded(ctx, true, self.venture);
    }
}

/// Buys a licence to sell short on the exchange where the player stands (C#
/// `BuyShortLicenceCommand`); `kind` is the `AssetMarketKind` byte.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyShortLicence {
    pub kind: u8,
}

impl BuyShortLicence {
    /// The market and the bank holding its margin.
    fn market(&self, ctx: &SimContext) -> Option<(usize, usize)> {
        let p = player(ctx);
        if p.is_travelling() {
            return None;
        }
        let n = nodes(ctx);
        let banks = ctx.services.get::<NodeBanks>();
        if self.kind == MARKET_STOCK {
            let bank = banks.branch_nation(n, p.node);
            return (bank >= 0).then_some((bank as usize, bank as usize));
        }
        if self.kind != MARKET_VENTURE {
            return None;
        }
        let markets = ctx.world.get::<AssetMarkets>();
        let continent = n.continent[p.node as usize];
        if markets.exchange_node(n, banks, continent) != p.node {
            return None;
        }
        let bank = markets.venture_lender(n, banks, continent);
        (bank >= 0).then(|| (markets.venture(continent), bank as usize))
    }
}

impl Command for BuyShortLicence {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let Some((market, bank)) = self.market(ctx) else {
            return fail("There is no such exchange here.");
        };
        let licences = ctx.world.get::<ShortLicences>();
        if licences.licensed[market] != 0 {
            return fail("The player holds this licence.");
        }
        let config = &ctx.services.database.config;
        let first = licences.first_trade[market];
        if first < 0 || ctx.turn() - first < config.short_licence_weeks {
            return fail("The licence needs a year of trading on this exchange.");
        }
        if ctx.world.get::<BankState>().player_deposit[bank] < config.short_licence_deposit {
            return fail("The licence needs a deposit at this exchange's bank.");
        }
        if coins_at(ctx, player(ctx).node) >= config.short_licence_fee {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Some((market, bank)) = self.market(ctx) else {
            return;
        };
        let fee = ctx.services.database.config.short_licence_fee;
        change_local(ctx, -fee);
        ctx.world.get_mut::<BankState>().reserve[bank] += fee;
        ctx.world.get_mut::<ShortLicences>().licensed[market] = 1;
    }
}

/// C# `ShortDesk.Check`.
fn short_desk_check(ctx: &SimContext, venture: bool, asset: i32) -> Option<&'static str> {
    let error = if venture {
        continental_check(ctx, asset)
    } else {
        exchange_check(ctx, asset)
    };
    if error.is_some() {
        return error;
    }
    if short_bank(&ctx.world, refs(ctx), venture, asset) < 0 {
        return Some("No bank holds the margin here.");
    }
    let market = short_market(&ctx.world, refs(ctx), venture, asset);
    if ctx.world.get::<ShortLicences>().licensed[market as usize] != 0 {
        None
    } else {
        Some("Short selling needs this exchange's licence.")
    }
}

/// Borrows shares and sells them to the households (C# `SellShortCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellShort {
    pub venture: bool,
    pub asset: i32,
    pub count: i32,
}

impl Command for SellShort {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        if let Some(e) = short_desk_check(ctx, self.venture, self.asset) {
            return fail(e);
        }
        let config = &ctx.services.database.config;
        let shorts = ctx.world.get::<ShortTable>();
        let lent = shorts.held(self.venture, self.asset) + self.count;
        let room = if self.venture {
            household_shares(ctx.world.get::<VentureTable>(), config, self.asset as usize)
        } else {
            config.facility_shares - 1 - ctx.world.get::<ShareTable>().outside(self.asset)
                + shorts.held(false, self.asset)
        };
        if lent > config.short_max_lent_shares || lent > room {
            return fail("No one lends so many shares.");
        }
        let r = refs(ctx);
        let bank = short_bank(&ctx.world, r, self.venture, self.asset);
        let proceeds = i64::from(self.count) * short_price(&ctx.world, r, self.venture, self.asset);
        let required = config
            .short_open_share
            .mul_int(short_value(&ctx.world, r, bank) + proceeds);
        if ctx.world.get::<BankState>().player_deposit[bank as usize] + proceeds >= required {
            CommandResult::Ok
        } else {
            fail("The deposit at the exchange's bank does not cover the margin.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let turn = ctx.turn();
        let (price, households, bank) = {
            let r = refs(ctx);
            (
                short_price(&ctx.world, r, self.venture, self.asset),
                short_households(&ctx.world, r, self.venture, self.asset),
                short_bank(&ctx.world, r, self.venture, self.asset),
            )
        };
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let taken = take_from_households(
            &mut ctx.world,
            &mut offers,
            &ctx.services.database.config,
            turn,
            households,
            i64::from(self.count) * price,
        );
        *ctx.services.get_mut::<BondOffers>() = offers;
        let sold = (taken / price) as i32;
        if taken > i64::from(sold) * price {
            pay_nation_households(&mut ctx.world, households, taken - i64::from(sold) * price);
        }
        if sold <= 0 {
            return;
        }
        let state = ctx.world.get_mut::<BankState>();
        state.player_deposit[bank as usize] += i64::from(sold) * price;
        state.reserve[bank as usize] += i64::from(sold) * price;
        if !self.venture {
            ctx.world
                .get_mut::<ShareTable>()
                .add(self.asset, households_holder(households), sold);
        }
        ctx.world
            .get_mut::<ShortTable>()
            .add(self.venture, self.asset, bank, sold);
        traded(ctx, self.venture, self.asset);
    }
}

/// Buys back shares sold short (C# `CoverShortCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CoverShort {
    pub venture: bool,
    pub asset: i32,
    pub count: i32,
}

impl Command for CoverShort {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.count <= 0 {
            return fail("Count must be positive.");
        }
        let error = if self.venture {
            continental_check(ctx, self.asset)
        } else {
            exchange_check(ctx, self.asset)
        };
        if let Some(e) = error {
            return fail(e);
        }
        if ctx.world.get::<ShortTable>().held(self.venture, self.asset) < self.count {
            return fail("The player has sold fewer short.");
        }
        let r = refs(ctx);
        let households = short_households(&ctx.world, r, self.venture, self.asset);
        if !self.venture
            && ctx
                .world
                .get::<ShareTable>()
                .held(self.asset, households_holder(households))
                < self.count
        {
            return fail("No one sells so many shares.");
        }
        let cost = i64::from(self.count) * short_price(&ctx.world, r, self.venture, self.asset);
        let bank = short_bank(&ctx.world, r, self.venture, self.asset);
        if ctx.world.get::<BankState>().payable_deposit(bank as usize) >= cost {
            CommandResult::Ok
        } else {
            fail("The deposit at the exchange's bank cannot pay for them.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        traded(ctx, self.venture, self.asset);
        let r = Refs {
            db: &ctx.services.database,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
        };
        buy_back(
            &mut ctx.world,
            r,
            self.venture,
            self.asset,
            self.count,
            true,
        );
    }
}
