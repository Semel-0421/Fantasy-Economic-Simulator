//! Licenses, lobbying and the player's ship (C# `Licenses.cs`, `PlayerShip.cs` and
//! `LobbyCommand`).
use super::facility::player_company;
use super::support::{
    change_local_coins, fail, known_good, local_coins, local_continent, needs_license, nodes,
    player, player_nation_node, port_check,
};
use crate::agents::banks::BankState;
use crate::agents::groups::{GroupTable, GuildMembershipTable};
use crate::agents::guidance::GuidanceSignal;
use crate::agents::licenses::{
    LicenseTable, TRADE_LICENSE_INTERCONTINENTAL, TRADE_LICENSE_INTERNATIONAL,
};
use crate::agents::money_ledger::{pay_households, to_state};
use crate::agents::policy::{LobbyEntry, PolicyState};
use crate::agents::policy_system::scale;
use crate::agents::policy_system::{POLICY_LICENSED_GOODS, POLICY_MINTING};
use crate::agents::ship::PlayerShip;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{GoodId, GuidanceAxis, QualityGrade, WorldLevel};
use fes_data::database::GameDatabase;

/// The player bought or renewed a license (C# `LicenseBought`); `nation` is the nation node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct LicenseBought {
    pub base_good: i32,
    pub nation: i32,
    pub expires_turn: i32,
    pub price: i64,
}

/// C# `GuildBenefits.MemberOfTrade`.
pub fn member_of_trade(
    world: &WorldState,
    nodes: &NodeTable,
    nation_node: i32,
    category: i32,
    turn: i32,
) -> bool {
    let groups = world.get::<GroupTable>();
    let m = world.get::<GuildMembershipTable>();
    (0..m.count()).any(|r| {
        let g = m.guild[r] as usize;
        turn < m.expires_turn[r]
            && groups.category[g] == category
            && nodes.ancestor_at(groups.node[g], WorldLevel::Nation) == nation_node
    })
}

/// C# `LicenseRules.Price`.
pub fn license_price(db: &GameDatabase, base_good: i32) -> i64 {
    db.config.license_price_multiplier.mul_int(i64::from(
        db.good(GoodId::from(base_good, QualityGrade::Mid))
            .base_price,
    ))
}

/// C# `LicenseRules.PlayerPrice` (DEC-057, DEC-058).
pub fn license_player_price(
    world: &WorldState,
    db: &GameDatabase,
    nodes: &NodeTable,
    banks: &NodeBanks,
    base_good: i32,
    nation_node: i32,
    turn: i32,
) -> i64 {
    let nation = banks.nation_index(nodes, nation_node) as usize;
    let step = world.get::<PolicyState>().license_price_step[nation];
    let price = scale(&db.config, step).mul_int(license_price(db, base_good));
    let category = db
        .good(GoodId::from(base_good, QualityGrade::Mid))
        .category_index;
    if member_of_trade(world, nodes, nation_node, category, turn) {
        price - db.config.guild_license_discount.mul_int(price)
    } else {
        price
    }
}

/// Buys or renews for a year the license for a good from the player's nation (C#
/// `BuyLicenseCommand`); signals a trade direction.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyLicense {
    pub good: i32,
}

impl BuyLicense {
    fn price(&self, ctx: &SimContext) -> i64 {
        license_player_price(
            &ctx.world,
            &ctx.services.database,
            nodes(ctx),
            ctx.services.get::<NodeBanks>(),
            self.good / QualityGrade::COUNT as i32,
            player_nation_node(ctx),
            ctx.turn(),
        )
    }
}

impl Command for BuyLicense {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if !known_good(ctx, self.good) {
            return fail("Unknown good.");
        }
        let p = player(ctx);
        if p.is_travelling() {
            return fail("Licenses are bought in a nation, not on the road.");
        }
        if player_nation_node(ctx) < 0 {
            return fail("No nation issues licenses here.");
        }
        let n = nodes(ctx);
        if n.absorbed()
            && !n.is_seat(p.node)
            && !crate::scenario::rules::licences_anywhere(&ctx.services)
        {
            return fail("Licenses are issued at the core towns.");
        }
        if !needs_license(
            &ctx.world,
            n,
            ctx.services.get::<NodeBanks>(),
            self.good,
            p.node,
        ) {
            return fail("This good needs no license here.");
        }
        if local_coins(ctx) >= self.price(ctx) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let price = self.price(ctx);
        let nation = player_nation_node(ctx);
        let base = self.good / QualityGrade::COUNT as i32;
        let turn = ctx.turn();
        change_local_coins(ctx, -price);
        let n = ctx.services.get::<NodeTable>();
        to_state(
            &mut ctx.world,
            n,
            ctx.services.get::<NodeBanks>(),
            nation,
            price,
        );
        let licenses = ctx.world.get_mut::<LicenseTable>();
        licenses.extend(base, nation, turn);
        let expires_turn = licenses
            .find(base, nation)
            .map_or(0, |r| licenses.expires_turn[r]);
        ctx.events.publish(LicenseBought {
            base_good: base,
            nation,
            expires_turn,
            price,
        });
    }

    fn guidance(&self, ctx: &SimContext) -> Option<GuidanceSignal> {
        Some(GuidanceSignal {
            axis: GuidanceAxis::Trade,
            weight: ctx
                .services
                .database
                .config
                .guidance_weight_buy_license
                .raw(),
            focus_node: player(ctx).node,
        })
    }
}

/// Buys a year's license to carry cargo out of the player's nation (C# `BuyTradeLicenseCommand`,
/// DEC-116); `kind` is a `TRADE_LICENSE_*` value.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyTradeLicense {
    pub kind: i32,
}

impl BuyTradeLicense {
    fn price(&self, ctx: &SimContext) -> i64 {
        let config = &ctx.services.database.config;
        if self.kind == TRADE_LICENSE_INTERNATIONAL {
            config.international_license_price
        } else {
            config.intercontinental_license_price
        }
    }
}

impl Command for BuyTradeLicense {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.kind != TRADE_LICENSE_INTERNATIONAL && self.kind != TRADE_LICENSE_INTERCONTINENTAL {
            return fail("Unknown license.");
        }
        let p = player(ctx);
        let n = nodes(ctx);
        if p.is_travelling() {
            return fail("Licenses are bought in a nation, not on the road.");
        }
        if player_nation_node(ctx) < 0 {
            return fail("No nation issues licenses here.");
        }
        if n.absorbed()
            && !n.is_seat(p.node)
            && !crate::scenario::rules::licences_anywhere(&ctx.services)
        {
            return fail("Licenses are issued at the core towns.");
        }
        if local_coins(ctx) >= self.price(ctx) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let nation = player_nation_node(ctx);
        let price = self.price(ctx);
        let turn = ctx.turn();
        change_local_coins(ctx, -price);
        to_state(
            &mut ctx.world,
            ctx.services.get::<NodeTable>(),
            ctx.services.get::<NodeBanks>(),
            nation,
            price,
        );
        ctx.world
            .get_mut::<LicenseTable>()
            .extend(self.kind, nation, turn);
    }
}

/// Buys a ship at the player's port (C# `BuyShipCommand`, DEC-053, DEC-061).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyShip;

impl Command for BuyShip {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if ctx.world.get::<PlayerShip>().owned {
            return fail("The player already owns a ship.");
        }
        if let Some(e) = port_check(ctx) {
            return fail(e);
        }
        if local_coins(ctx) >= ctx.services.database.config.ship_price {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let price = ctx.services.database.config.ship_price;
        let node = player(ctx).node;
        let continent = local_continent(ctx) as i32;
        change_local_coins(ctx, -price);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), node, price);
        let ship = ctx.world.get_mut::<PlayerShip>();
        ship.owned = true;
        ship.bought_continent = continent;
        ship.docked_at = node;
    }
}

/// Sells the ship where it is docked (C# `SellShipCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellShip;

impl Command for SellShip {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if let Some(e) = port_check(ctx) {
            return fail(e);
        }
        if ctx.world.get::<PlayerShip>().available_at(player(ctx).node) {
            CommandResult::Ok
        } else {
            fail("The ship is not docked here.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let config = &ctx.services.database.config;
        let value = config.ship_resale_share.mul_int(config.ship_price);
        let node = player(ctx).node;
        change_local_coins(ctx, value);
        pay_households(
            &mut ctx.world,
            ctx.services.get::<NodeTable>(),
            node,
            -value,
        );
        let ship = ctx.world.get_mut::<PlayerShip>();
        ship.owned = false;
        ship.docked_at = -1;
    }
}

/// The player lobbied a policy (C# `Lobbied`); `nation` is the nation node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct Lobbied {
    pub nation: i32,
    pub policy: u8,
    pub base_good: i32,
    pub direction: i32,
    pub amount: i64,
}

/// Spends local coins lobbying a nation's policy in the company's name (C# `LobbyCommand`,
/// DEC-058); `policy` is the `PolicyKind` byte, `base_good` -1 unless it is licensed goods.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Lobby {
    pub nation: i32,
    pub policy: u8,
    pub direction: i32,
    pub amount: i64,
    pub base_good: i32,
}

impl Command for Lobby {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if player_company(ctx.world.get::<GroupTable>()) < 0 {
            return fail("Lobbying is done in the company's name: found it first.");
        }
        if self.amount <= 0 {
            return fail("Amount must be positive.");
        }
        if self.direction != 1 && self.direction != -1 {
            return fail("Direction must be +1 or -1.");
        }
        if self.policy > POLICY_MINTING {
            return fail("Unknown policy.");
        }
        let n = nodes(ctx);
        if self.nation < 0
            || self.nation as usize >= n.count()
            || n.level_of(self.nation) != WorldLevel::Nation
        {
            return fail("Pick a nation.");
        }
        let base_goods = ctx.services.database.base_good_count() as i32;
        let bad_good = if self.policy == POLICY_LICENSED_GOODS {
            self.base_good < 0 || self.base_good >= base_goods
        } else {
            self.base_good != -1
        };
        if bad_good {
            return fail("Name a good for licensing, and only for licensing.");
        }
        let p = player(ctx);
        if p.is_travelling() || n.ancestor_at(p.node, WorldLevel::Nation) != self.nation {
            return fail("Lobbying is done inside the nation.");
        }
        if local_coins(ctx) >= self.amount {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let nation = ctx
            .services
            .get::<NodeBanks>()
            .nation_index(nodes(ctx), self.nation);
        change_local_coins(ctx, -self.amount);
        ctx.world.get_mut::<BankState>().reserve[nation as usize] += self.amount;
        let group = player_company(ctx.world.get::<GroupTable>());
        ctx.world
            .get_mut::<PolicyState>()
            .lobbying
            .push(LobbyEntry {
                group,
                nation,
                policy: self.policy,
                base_good: self.base_good,
                direction: self.direction,
                amount: self.amount,
            });
        ctx.events.publish(Lobbied {
            nation: self.nation,
            policy: self.policy,
            base_good: self.base_good,
            direction: self.direction,
            amount: self.amount,
        });
    }
}
