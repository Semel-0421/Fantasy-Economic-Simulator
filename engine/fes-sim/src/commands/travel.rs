//! Player trips (C# `TravelCommands.cs`, the quote half of `TravelCosts` and `Encounters`).
use super::support::{fail, nodes, player};
use crate::agents::cargo::{CargoCapacity, CargoTable, PLAYER_CART};
use crate::agents::development::DevelopmentState;
use crate::agents::groups::{city_of, insurance_guild, GroupTable, GuildMembershipTable};
use crate::agents::licenses::{
    LicenseTable, TRADE_LICENSE_INTERCONTINENTAL, TRADE_LICENSE_INTERNATIONAL,
};
use crate::agents::money_ledger::{pay_from_group, pay_households, to_state};
use crate::agents::player::PlayerState;
use crate::agents::production::lines::find_nearest_market;
use crate::agents::purses::PurseState;
use crate::agents::ship::PlayerShip;
use crate::agents::travel_costs::TravelCosts;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::economy::{CurrencyState, MarketStore};
use crate::world::{
    NodeBanks, NodeSpecialties, NodeTable, NodeTolls, RoadState, RouteHazards, RouteTable,
    WorldState,
};
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx, GoodId, QualityGrade, RouteTerrain, WorldLevel};
use fes_data::database::GameDatabase;

/// C# `EncounterLoss`.
pub const LOSS_NONE: u8 = 0;
pub const LOSS_CASH: u8 = 1;
pub const LOSS_CARGO: u8 = 2;

/// What a trip costs now (C# `TripQuote`).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct TripQuote {
    pub route: i32,
    /// Route fee in the departure coin.
    pub fee: i64,
    pub fee_continent: i32,
    /// Toll of the entered node, in its coin.
    pub toll: i64,
    pub toll_continent: i32,
    pub cargo_units: i32,
    pub passage: i64,
    pub guards: i64,
    pub insurance: i64,
    /// The cargo's value at the destination's prices, in the toll's coin.
    pub cargo_value: i64,
    /// Raw Fx.
    pub encounter_chance: i64,
}

impl TripQuote {
    /// C# `DepartureCost`.
    pub fn departure_cost(&self) -> i64 {
        self.fee + self.passage + self.guards + self.insurance
    }

    /// C# `AffordableWith`.
    pub fn affordable_with(&self, coins: &[i64]) -> bool {
        let (f, t) = (self.fee_continent as usize, self.toll_continent as usize);
        if f == t {
            coins[f] >= self.departure_cost() + self.toll
        } else {
            coins[f] >= self.departure_cost() && coins[t] >= self.toll
        }
    }
}

/// C# `TripOptions`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct TripOptions {
    pub guards: bool,
    pub insurance: bool,
    pub own_ship: bool,
    pub guild_member: bool,
}

/// The trip started (C# `TripStarted`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct TripStarted {
    pub destination: i32,
    pub arrival_turn: i32,
    pub paid: TripQuote,
    pub turn: i32,
}

/// An encounter took cash or cargo, insurance paid some back (C# `TripEncounter`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct TripEncounter {
    pub destination: i32,
    pub loss: u8,
    pub loss_value: i64,
    pub refund: i64,
    pub turn: i32,
}

fn is_water(routes: &RouteTable, route: usize) -> bool {
    RouteTerrain::from_u8(routes.terrain[route]).is_some_and(|t| t.is_water())
}

/// C# `TravelCosts.FindRoute`.
pub fn find_route(routes: &RouteTable, from: i32, to: i32) -> i32 {
    let start = routes.out_start[from as usize];
    for k in start..start + routes.out_count[from as usize] {
        let r = routes.route_index[k as usize];
        if routes.to[r as usize] == to {
            return r;
        }
    }
    -1
}

fn costs<'a>(world: &'a WorldState, ctx_services: &'a crate::context::Services) -> TravelCosts<'a> {
    TravelCosts {
        config: &ctx_services.database.config,
        routes: ctx_services.get::<RouteTable>(),
        hazards: ctx_services.get::<RouteHazards>(),
        roads: world.get::<RoadState>(),
        development: world.get::<DevelopmentState>(),
        purses: world.get::<PurseState>(),
    }
}

/// C# `TravelCosts.EncounterChance`.
fn encounter_chance(costs: &TravelCosts, route: usize, cargo_value: i64, guards: bool) -> Fx {
    let config = costs.config;
    let mut chance = costs.route_danger(route)
        * (Fx::ONE + Fx::from_ratio(cargo_value, config.danger_value_scale));
    if guards {
        chance = chance * (Fx::ONE - config.guard_reduction);
    }
    if chance.raw() > Fx::ONE_RAW {
        Fx::ONE
    } else if chance.raw() < 0 {
        Fx::ZERO
    } else {
        chance
    }
}

fn is_specialty_category(
    db: &GameDatabase,
    specialties: &NodeSpecialties,
    node: i32,
    category: i32,
) -> bool {
    let start = specialties.start[node as usize];
    let end = start + specialties.count[node as usize];
    (start..end).any(|k| {
        let good = GoodId::from(specialties.base_good[k as usize], QualityGrade::Low);
        db.good(good).category_index == category
    })
}

/// C# `TravelCosts.Quote`.
pub fn quote(
    world: &WorldState,
    services: &crate::context::Services,
    route: usize,
    cargo: &[(i32, i32)],
    options: TripOptions,
) -> TripQuote {
    let db = &services.database;
    let config = &db.config;
    let nodes = services.get::<NodeTable>();
    let routes = services.get::<RouteTable>();
    let tolls = services.get::<NodeTolls>();
    let specialties = services.get::<NodeSpecialties>();
    let markets = world.get::<MarketStore>();
    let to = routes.to[route];
    let from_continent = nodes.continent[routes.from[route] as usize];
    let to_continent = nodes.continent[to as usize];
    let (mut units, mut toll, mut value) = (0i64, 0i64, 0i64);
    for &(good, quantity) in cargo {
        units += i64::from(quantity);
        let Some((_, cell)) = find_nearest_market(nodes, markets, to, good) else {
            continue;
        };
        let item_value = i64::from(quantity) * i64::from(markets.price[cell]);
        let mut rate = tolls.tariff[to as usize];
        if is_specialty_category(db, specialties, to, db.good(GoodId(good)).category_index) {
            rate += tolls.specialty_surcharge[to as usize];
        }
        toll += Fx::from_raw(rate).mul_int(item_value);
        value += item_value;
    }
    let c = costs(world, services);
    let fee = i64::from(routes.base_fee[route]) + c.per_unit_charge(route).mul_int(units);
    let passage = if is_water(routes, route) && !options.own_ship {
        config.passage_base_fee + units * config.passage_per_unit
    } else {
        0
    };
    let value_here =
        world
            .get::<CurrencyState>()
            .convert(value, to_continent as usize, from_continent as usize);
    let guards = if options.guards {
        config.guard_base_cost + config.guard_cost_share.mul_int(value_here)
    } else {
        0
    };
    let mut insurance = if options.insurance {
        config.insurance_premium.mul_int(value_here)
    } else {
        0
    };
    if options.guild_member {
        insurance -= config.guild_insurance_discount.mul_int(insurance);
    }
    TripQuote {
        route: route as i32,
        fee,
        fee_continent: from_continent,
        toll,
        toll_continent: to_continent,
        cargo_units: units.min(i64::from(i32::MAX)) as i32,
        passage,
        guards,
        insurance,
        cargo_value: value,
        encounter_chance: encounter_chance(&c, route, value, options.guards).raw(),
    }
}

/// C# `Encounters.Roll`.
pub fn roll_encounter(seed: u64, turn: i32, entity: i32, salt: u32, chance: i64) -> u8 {
    let mut rng = DetRandom::for_key(seed, turn, det_hash::of("agents.encounters"), entity, salt);
    if rng.next_fx01().raw() >= chance {
        return LOSS_NONE;
    }
    if rng.next_int(2) == 0 {
        LOSS_CASH
    } else {
        LOSS_CARGO
    }
}

/// C# `GuildBenefits.MemberHere`.
pub fn member_here(world: &WorldState, nodes: &NodeTable, node: i32, turn: i32) -> bool {
    let groups = world.get::<GroupTable>();
    let m = world.get::<GuildMembershipTable>();
    let city = city_of(nodes, node);
    if city < 0 {
        return false;
    }
    (0..m.count()).any(|r| turn < m.expires_turn[r] && groups.node[m.guild[r] as usize] == city)
}

/// C# `PlayerTrip.TryQuote`.
pub fn try_quote(
    ctx: &SimContext,
    destination: i32,
    guards: bool,
    insurance: bool,
) -> Option<TripQuote> {
    let node = player(ctx).node;
    let route = find_route(ctx.services.get::<RouteTable>(), node, destination);
    if route < 0 {
        return None;
    }
    let cart = ctx.world.get::<CargoTable>().collect(PLAYER_CART);
    let options = TripOptions {
        guards,
        insurance,
        own_ship: ctx.world.get::<PlayerShip>().available_at(node),
        guild_member: member_here(&ctx.world, nodes(ctx), node, ctx.turn()),
    };
    Some(quote(
        &ctx.world,
        &ctx.services,
        route as usize,
        &cart,
        options,
    ))
}

/// C# `PlayerTrip.Carrier` (DEC-034).
fn carrier(ctx: &SimContext, route: usize) -> CargoCapacity {
    let config = &ctx.services.database.config;
    let ship = is_water(ctx.services.get::<RouteTable>(), route)
        && ctx.world.get::<PlayerShip>().available_at(player(ctx).node);
    if ship {
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

fn cart_load(world: &WorldState) -> (i64, i64) {
    let markets = world.get::<MarketStore>();
    world
        .get::<CargoTable>()
        .load_of(PLAYER_CART, &markets.good_weight, &markets.good_size)
}

/// C# `PlayerTrip.CargoFits`.
pub fn cargo_fits(ctx: &SimContext, route: usize) -> bool {
    let load = cart_load(&ctx.world);
    let capacity = carrier(ctx, route);
    load.0 <= capacity.max_weight && load.1 <= capacity.max_size
}

/// C# `LicenseRules.TradeLicenseNeeded`: the kind and the nation node, when one is needed.
pub fn trade_license_needed(nodes: &NodeTable, from: i32, to: i32) -> Option<(i32, i32)> {
    let nation = nodes.ancestor_at(from, WorldLevel::Nation);
    let kind = if nodes.continent[from as usize] != nodes.continent[to as usize] {
        TRADE_LICENSE_INTERCONTINENTAL
    } else {
        TRADE_LICENSE_INTERNATIONAL
    };
    (nation >= 0 && nation != nodes.ancestor_at(to, WorldLevel::Nation)).then_some((kind, nation))
}

/// C# `PlayerTrip.CargoMayLeave` (DEC-116).
pub fn cargo_may_leave(ctx: &SimContext, from: i32, to: i32) -> bool {
    let Some((kind, nation)) = trade_license_needed(nodes(ctx), from, to) else {
        return true;
    };
    let load = cart_load(&ctx.world);
    load.0 <= 0 && load.1 <= 0
        || ctx
            .world
            .get::<LicenseTable>()
            .is_valid(kind, nation, ctx.turn())
}

/// Sets the player off along a route (C# `TravelCommand`, DEC-026, DEC-061).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Travel {
    pub destination: i32,
    pub guards: bool,
    pub insurance: bool,
}

impl Command for Travel {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.destination < 0 || self.destination as usize >= nodes(ctx).count() {
            return fail("Unknown destination.");
        }
        let p = player(ctx);
        if p.is_travelling() {
            return fail("Already travelling.");
        }
        let Some(q) = try_quote(ctx, self.destination, self.guards, self.insurance) else {
            return fail("No route to destination.");
        };
        if !cargo_fits(ctx, q.route as usize) {
            return fail("The cargo does not fit what carries it on this route.");
        }
        if !cargo_may_leave(ctx, p.node, self.destination) {
            return fail("Carrying cargo out of this nation needs its trade license.");
        }
        if q.affordable_with(&p.coins) {
            CommandResult::Ok
        } else {
            fail("Not enough coins for the trip.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Some(q) = try_quote(ctx, self.destination, self.guards, self.insurance) else {
            return;
        };
        let node = player(ctx).node;
        {
            let p = ctx.world.get_mut::<PlayerState>();
            p.coins[q.fee_continent as usize] -= q.departure_cost();
            p.coins[q.toll_continent as usize] -= q.toll;
        }
        let n = ctx.services.get::<NodeTable>();
        pay_households(&mut ctx.world, n, node, q.fee + q.passage + q.guards);
        let guild = insurance_guild(ctx.world.get::<GroupTable>(), n, node);
        if guild >= 0 {
            ctx.world.get_mut::<GroupTable>().cash[guild as usize] += q.insurance;
        } else {
            pay_households(&mut ctx.world, n, node, q.insurance);
        }
        to_state(
            &mut ctx.world,
            n,
            ctx.services.get::<NodeBanks>(),
            self.destination,
            q.toll,
        );
        let routes = ctx.services.get::<RouteTable>();
        let route = q.route as usize;
        if q.passage == 0 && is_water(routes, route) {
            ctx.world.get_mut::<PlayerShip>().docked_at = self.destination;
        }
        let turn = ctx.turn();
        let arrival_turn = turn + routes.travel_turns[route];
        {
            let p = ctx.world.get_mut::<PlayerState>();
            p.travel_destination = self.destination;
            p.arrival_turn = arrival_turn;
        }
        ctx.events.publish(TripStarted {
            destination: self.destination,
            arrival_turn,
            paid: q,
            turn,
        });
        self.encounter(ctx, q);
    }
}

impl Travel {
    fn encounter(&self, ctx: &mut SimContext, q: TripQuote) {
        let turn = ctx.turn();
        let loss = roll_encounter(ctx.seed(), turn, 0, 0, q.encounter_chance);
        if loss == LOSS_NONE {
            return;
        }
        let config = &ctx.services.database.config;
        let n = ctx.services.get::<NodeTable>();
        let node = player(ctx).node;
        let fc = q.fee_continent as usize;
        let value = if loss == LOSS_CASH {
            let p = ctx.world.get_mut::<PlayerState>();
            let value = config.encounter_cash_loss.mul_int(p.coins[fc].max(0));
            p.coins[fc] -= value;
            pay_households(&mut ctx.world, n, node, value);
            value
        } else {
            let lost = take_cargo(
                ctx.world.get_mut::<CargoTable>(),
                PLAYER_CART,
                config.encounter_cargo_loss,
            );
            let lost_quote = quote(
                &ctx.world,
                &ctx.services,
                q.route as usize,
                &lost,
                TripOptions::default(),
            );
            ctx.world.get::<CurrencyState>().convert(
                lost_quote.cargo_value,
                q.toll_continent as usize,
                fc,
            )
        };
        let refund = if self.insurance && loss == LOSS_CARGO {
            config.insurance_refund.mul_int(value)
        } else {
            0
        };
        ctx.world.get_mut::<PlayerState>().coins[fc] += refund;
        let guild = insurance_guild(ctx.world.get::<GroupTable>(), n, node);
        pay_from_group(&mut ctx.world, n, guild, node, refund);
        ctx.events.publish(TripEncounter {
            destination: self.destination,
            loss,
            loss_value: value,
            refund,
            turn,
        });
    }
}

/// C# `Encounters.TakeCargo`: takes `share` of every carried good; returns what was lost.
pub fn take_cargo(cargo: &mut CargoTable, holder: i32, share: Fx) -> Vec<(i32, i32)> {
    let mut lost = cargo.collect(holder);
    for item in &mut lost {
        let quantity = share.mul_int(i64::from(item.1)) as i32;
        cargo.change(holder, item.0, -quantity);
        item.1 = quantity;
    }
    lost.retain(|item| item.1 != 0);
    lost
}
