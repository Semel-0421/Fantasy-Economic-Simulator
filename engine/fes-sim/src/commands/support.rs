//! Checks and lookups shared by the player commands (C# `PlayerBank`, `PlayerSite`, `PlayerTrade`,
//! `PlayerPort`, `PolicyRules.NeedsLicense`).
use crate::agents::asset_prices::{short_required, Refs};
use crate::agents::banks::BankState;
use crate::agents::cargo::CargoCapacity;
use crate::agents::facilities::{FacilityTable, OWNER_PLAYER};
use crate::agents::finance::FinanceState;
use crate::agents::player::PlayerState;
use crate::agents::policy::PolicyState;
use crate::agents::ship::PlayerShip;
use crate::commands::CommandResult;
use crate::context::SimContext;
use crate::world::{NodeBanks, NodePorts, NodeTable, WorldState};
use fes_core::WorldLevel;

/// `CommandResult.Fail`.
pub fn fail(message: impl Into<String>) -> CommandResult {
    CommandResult::Error(message.into())
}

/// Turns a check's error into a result.
pub fn check(error: Option<&'static str>) -> CommandResult {
    error.map_or(CommandResult::Ok, fail)
}

pub fn refs(ctx: &SimContext) -> Refs<'_> {
    Refs {
        db: &ctx.services.database,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    }
}

pub fn nodes(ctx: &SimContext) -> &NodeTable {
    ctx.services.get::<NodeTable>()
}

pub fn player(ctx: &SimContext) -> &PlayerState {
    ctx.world.get::<PlayerState>()
}

/// C# `PlayerState.LocalContinent`.
pub fn local_continent(ctx: &SimContext) -> usize {
    let node = player(ctx).node as usize;
    nodes(ctx).continent[node] as usize
}

/// C# `PlayerState.LocalCoins`.
pub fn local_coins(ctx: &SimContext) -> i64 {
    player(ctx).coins[local_continent(ctx)]
}

/// Adds `delta` to the player's local coins.
pub fn change_local_coins(ctx: &mut SimContext, delta: i64) {
    let c = local_continent(ctx);
    ctx.world.get_mut::<PlayerState>().coins[c] += delta;
}

/// C# `PlayerBank.Branch`: the nation of the branch the player stands at.
pub fn branch(ctx: &SimContext) -> Result<usize, &'static str> {
    let p = player(ctx);
    let nation = if p.is_travelling() {
        -1
    } else {
        ctx.services
            .get::<NodeBanks>()
            .branch_nation(nodes(ctx), p.node)
    };
    if nation < 0 {
        Err("There is no bank branch here.")
    } else {
        Ok(nation as usize)
    }
}

/// C# `PlayerBank.RunCap`.
pub fn run_cap(ctx: &SimContext, nation: usize) -> i64 {
    ctx.services
        .database
        .config
        .bank_run_withdraw_share
        .mul_int(ctx.world.get::<BankState>().player_deposit[nation])
}

/// C# `PlayerBank.Withdrawable`.
pub fn withdrawable(ctx: &SimContext, nation: usize) -> i64 {
    let deposit = ctx.world.get::<BankState>().player_deposit[nation];
    let finance = ctx.world.get::<FinanceState>();
    if finance.event_of(nation) != crate::agents::finance::event_kind::BANK_RUN {
        return deposit;
    }
    if finance.last_withdraw_turn[nation] == ctx.turn() {
        0
    } else {
        run_cap(ctx, nation)
    }
}

/// C# `ShortRules.Required` for the player's shorts at `bank`.
pub fn short_margin(ctx: &SimContext, bank: usize, share: fes_core::Fx) -> i64 {
    short_required(&ctx.world, refs(ctx), bank as i32, share)
}

/// C# `PlayerSite.IsAt`.
pub fn is_at(ctx: &SimContext, node: i32) -> bool {
    let p = player(ctx);
    !p.is_travelling() && p.node == node
}

/// C# `PlayerSite.Check`.
pub fn site_check(
    ctx: &SimContext,
    facility: i32,
    allow_lost_warehouse: bool,
) -> Option<&'static str> {
    let facilities = ctx.world.get::<FacilityTable>();
    if facility < 0 || facility as usize >= facilities.count() {
        return Some("Unknown facility.");
    }
    let f = facility as usize;
    let claim = allow_lost_warehouse && facilities.warehouse_claim_until[f] >= 0;
    if facilities.owner[f] != OWNER_PLAYER && !claim {
        return Some("Facility is not owned by the player.");
    }
    if is_at(ctx, facilities.node[f]) {
        None
    } else {
        Some("The player is not at the facility.")
    }
}

/// C# `PlayerTrade.LoadCapacity`: an owned ship docked here carries more than the cart.
pub fn load_capacity(ctx: &SimContext) -> CargoCapacity {
    let config = &ctx.services.database.config;
    if ctx.world.get::<PlayerShip>().available_at(player(ctx).node) {
        CargoCapacity {
            max_weight: config.ship_max_weight,
            max_size: config.ship_max_size,
        }
    } else {
        CargoCapacity {
            max_weight: config.cart_max_weight,
            max_size: config.cart_max_size,
        }
    }
}

/// C# `PlayerPort.Check`.
pub fn port_check(ctx: &SimContext) -> Option<&'static str> {
    let p = player(ctx);
    if p.is_travelling() || !ctx.services.get::<NodePorts>().contains(p.node) {
        Some("Ships are traded at ports.")
    } else {
        None
    }
}

/// C# `PolicyRules.NeedsLicense`.
pub fn needs_license(
    world: &WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    good: i32,
    node: i32,
) -> bool {
    let policy = world.get::<PolicyState>();
    let nation = banks.nation_of(nodes, node);
    let row = if nation < 0 {
        policy.nation_count
    } else {
        nation as usize
    };
    policy.licensed
        [row * policy.base_good_count + (good / fes_core::QualityGrade::COUNT as i32) as usize]
        != 0
}

/// C# `NodeTable.AncestorAt(node, Nation)` of the player's node.
pub fn player_nation_node(ctx: &SimContext) -> i32 {
    nodes(ctx).ancestor_at(player(ctx).node, WorldLevel::Nation)
}

/// C# `GoodId.IsValid && Value < GoodCount`.
pub fn known_good(ctx: &SimContext, good: i32) -> bool {
    good >= 0 && (good as usize) < ctx.services.database.good_count()
}
