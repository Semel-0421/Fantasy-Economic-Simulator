//! Guilds, land and roads (C# `Guilds.cs`, `Land.cs` and `Roads.cs` commands).
use super::facility::coins_at;
use super::support::{
    change_local_coins, fail, known_good, load_capacity, local_coins, nodes, player,
};
use super::travel::find_route;
use crate::agents::asset_prices::land_price;
use crate::agents::cargo::{guild_hall_holder, CargoCapacity, CargoTable, PLAYER_CART};
use crate::agents::groups::{GroupTable, GuildMembershipTable, KIND_GUILD};
use crate::agents::land::LandState;
use crate::agents::money_ledger::pay_households;
use crate::agents::player::PlayerState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::economy::{HouseholdCash, MarketStore};
use crate::world::roads::MAX_CONDITION;
use crate::world::{NodeTable, RoadState, RouteTable};
use fes_core::RouteTerrain;

/// The player joined or renewed a guild (C# `GuildJoined`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GuildJoined {
    pub guild: i32,
    pub expires_turn: i32,
    pub fee: i64,
}

impl GuildMembershipTable {
    /// C# `Find`.
    pub fn find(&self, guild: i32) -> Option<usize> {
        (0..self.count()).find(|&r| self.guild[r] == guild)
    }

    /// C# `IsMember`.
    pub fn is_member(&self, guild: i32, turn: i32) -> bool {
        self.find(guild)
            .is_some_and(|r| turn < self.expires_turn[r])
    }

    /// C# `Extend`: adds a year, counted from the current expiry when still valid.
    pub fn extend(&mut self, guild: i32, turn: i32) {
        let year = crate::clock::WEEKS_PER_YEAR;
        match self.find(guild) {
            None => {
                self.guild.push(guild);
                self.expires_turn.push(turn + year);
            }
            Some(r) => self.expires_turn[r] = turn.max(self.expires_turn[r]) + year,
        }
    }
}

/// C# `PlayerGuild.Check`.
fn guild_check(ctx: &SimContext, guild: i32) -> Option<&'static str> {
    let groups = ctx.world.get::<GroupTable>();
    if guild < 0 || guild as usize >= groups.count() || groups.kind[guild as usize] != KIND_GUILD {
        return Some("Unknown guild.");
    }
    let p = player(ctx);
    if p.is_travelling() || p.node != groups.node[guild as usize] {
        Some("Guilds are joined in their city.")
    } else {
        None
    }
}

/// Joins or renews a guild in the player's city (C# `JoinGuildCommand`, DEC-056).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct JoinGuild {
    pub guild: i32,
}

impl Command for JoinGuild {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if let Some(e) = guild_check(ctx, self.guild) {
            return fail(e);
        }
        if local_coins(ctx) >= ctx.services.database.config.guild_fee {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let fee = ctx.services.database.config.guild_fee;
        let turn = ctx.turn();
        change_local_coins(ctx, -fee);
        ctx.world.get_mut::<GroupTable>().cash[self.guild as usize] += fee;
        let m = ctx.world.get_mut::<GuildMembershipTable>();
        m.extend(self.guild, turn);
        let expires_turn = m.find(self.guild).map_or(0, |r| m.expires_turn[r]);
        ctx.events.publish(GuildJoined {
            guild: self.guild,
            expires_turn,
            fee,
        });
    }
}

/// Moves goods between the cart and a guild hall (C# `MoveGuildHallCargoCommand`, DEC-057).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MoveGuildHallCargo {
    pub guild: i32,
    pub good: i32,
    pub quantity: i32,
    pub to_hall: bool,
}

impl MoveGuildHallCargo {
    fn ends(&self) -> (i32, i32) {
        let hall = guild_hall_holder(self.guild);
        if self.to_hall {
            (PLAYER_CART, hall)
        } else {
            (hall, PLAYER_CART)
        }
    }

    fn fill(&self, ctx: &SimContext) -> i32 {
        let cargo = ctx.world.get::<CargoTable>();
        let markets = ctx.world.get::<MarketStore>();
        let config = &ctx.services.database.config;
        let (from, to) = self.ends();
        let capacity = if self.to_hall {
            CargoCapacity {
                max_weight: config.guild_hall_max_weight,
                max_size: config.guild_hall_max_size,
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

impl Command for MoveGuildHallCargo {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.quantity <= 0 {
            return fail("Quantity must be positive.");
        }
        if !known_good(ctx, self.good) {
            return fail("Unknown good.");
        }
        if let Some(e) = guild_check(ctx, self.guild) {
            return fail(e);
        }
        if !ctx
            .world
            .get::<GuildMembershipTable>()
            .is_member(self.guild, ctx.turn())
        {
            return fail("Only members use the guild hall.");
        }
        if self.fill(ctx) > 0 {
            CommandResult::Ok
        } else {
            fail("Nothing to move, or no room.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let moved = self.fill(ctx);
        let (from, to) = self.ends();
        let cargo = ctx.world.get_mut::<CargoTable>();
        cargo.change(from, self.good, -moved);
        cargo.change(to, self.good, moved);
    }
}

fn households_of(nodes: &NodeTable, node: i32) -> usize {
    if nodes.absorbed() {
        nodes.core[node as usize] as usize
    } else {
        node as usize
    }
}

fn plot_price(ctx: &SimContext, node: i32) -> i64 {
    land_price(
        &ctx.world,
        nodes(ctx),
        &ctx.services.database.config,
        node as usize,
    )
}

fn change_coins_at(ctx: &mut SimContext, node: i32, delta: i64) {
    let c = ctx.services.get::<NodeTable>().continent[node as usize] as usize;
    ctx.world.get_mut::<PlayerState>().coins[c] += delta;
}

/// Buys a plot from the households where the player stands (C# `BuyPlotCommand`, DEC-128).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyPlot;

impl Command for BuyPlot {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let p = player(ctx);
        if p.is_travelling() {
            return fail("The player is travelling.");
        }
        if ctx.world.get::<LandState>().free(p.node as usize) <= 0 {
            return fail("No land is for sale here.");
        }
        if coins_at(ctx, p.node) >= plot_price(ctx, p.node) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let n = player(ctx).node;
        let price = plot_price(ctx, n);
        change_coins_at(ctx, n, -price);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), n, price);
        ctx.world.get_mut::<LandState>().player_plots[n as usize] += 1;
    }
}

/// Sells a plot back to the households where the player stands (C# `SellPlotCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellPlot;

impl Command for SellPlot {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let p = player(ctx);
        if p.is_travelling() {
            return fail("The player is travelling.");
        }
        if ctx.world.get::<LandState>().player_plots[p.node as usize] <= 0 {
            return fail("The player holds no land here.");
        }
        let cash = ctx.world.get::<HouseholdCash>().cash[households_of(nodes(ctx), p.node)];
        if cash >= plot_price(ctx, p.node) {
            CommandResult::Ok
        } else {
            fail("No one here can pay for it.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let n = player(ctx).node;
        let price = plot_price(ctx, n);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), n, -price);
        change_coins_at(ctx, n, price);
        ctx.world.get_mut::<LandState>().player_plots[n as usize] -= 1;
    }
}

/// Commissions paving of the route to a neighbour (C# `BuildRoadCommand`, DEC-061).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuildRoad {
    pub destination: i32,
}

impl BuildRoad {
    fn cost(ctx: &SimContext, route: usize) -> i64 {
        ctx.services.database.config.road_build_cost_per_turn
            * i64::from(ctx.services.get::<RouteTable>().travel_turns[route])
    }
}

impl Command for BuildRoad {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let p = player(ctx);
        if p.is_travelling() {
            return fail("Roads are commissioned on the spot, not on the road.");
        }
        let routes = ctx.services.get::<RouteTable>();
        let route = if self.destination >= 0 && (self.destination as usize) < nodes(ctx).count() {
            find_route(routes, p.node, self.destination)
        } else {
            -1
        };
        if route < 0 {
            return fail("No route to destination.");
        }
        let r = route as usize;
        if RouteTerrain::from_u8(routes.terrain[r]).is_some_and(|t| t.is_water()) {
            return fail("Water routes cannot be paved.");
        }
        let roads = ctx.world.get::<RoadState>();
        if roads.build_done_turn[r] >= 0 {
            return fail("A road is already being built there.");
        }
        if roads.condition[r] == MAX_CONDITION {
            return fail("The road is already paved.");
        }
        if local_coins(ctx) >= Self::cost(ctx, r) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let node = player(ctx).node;
        let routes = ctx.services.get::<RouteTable>();
        let route = find_route(routes, node, self.destination);
        if route < 0 {
            return;
        }
        let back = find_route(routes, self.destination, node);
        let cost = Self::cost(ctx, route as usize);
        change_local_coins(ctx, -cost);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), node, cost);
        let done = ctx.turn() + ctx.services.database.config.road_build_weeks;
        let roads = ctx.world.get_mut::<RoadState>();
        roads.build_done_turn[route as usize] = done;
        if back >= 0 && roads.build_done_turn[back as usize] < 0 {
            roads.build_done_turn[back as usize] = done;
        }
    }
}
