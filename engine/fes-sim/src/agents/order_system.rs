//! `agents.orders` (C# `OrderSystem`, DEC-085): weekly the order book reserves what markets hold,
//! hands over what is due and drops orders too long unfilled; quarterly households order the
//! made-to-order luxuries they can afford.
use super::merchants::AgentTable;
use super::orders::{MerchantTripStats, OrderTable};
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::pricing::demand::{cell_base, weekly, DemandParams, WeeklyInput};
use crate::economy::{CurrencyState, HouseholdCash, HouseholdState, MarketStore};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeTable, WorldState};
use fes_core::Fx;
use fes_data::database::GameDatabase;

/// C# `FinanceOrder.Orders`.
pub const ORDER: i32 = sim_order::LATE + 23;
/// C# `OrderSystem.QuarterWeeks`.
pub const QUARTER_WEEKS: i32 = 13;

/// A merchant group collected a better vehicle (C# `MerchantVehicleBought`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct MerchantVehicleBought {
    pub agent: i32,
    pub vehicle: u8,
}

/// Goods some facility makes (C# `OrderSystem._made`).
pub fn made_goods(db: &GameDatabase) -> Vec<bool> {
    let mut made = vec![false; db.good_count()];
    for def in &db.facilities {
        for line in &db.recipes[def.recipe].outputs {
            made[line.good.0 as usize] = true;
        }
    }
    made
}

/// `agents.orders`.
pub struct OrderSystem;

impl System for OrderSystem {
    fn id(&self) -> &'static str {
        "agents.orders"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let db = &ctx.services.database;
        let nodes = ctx.services.get::<NodeTable>();
        if turn > 0 && turn % QUARTER_WEEKS == 0 {
            households_order(&mut ctx.world, db, nodes, turn);
        }
        let mut spent = 0i64;
        let world = &mut ctx.world;
        let lead = db.config.order_lead_weeks;
        let expiry = db.config.order_expiry_weeks;
        let mut r = world.get::<OrderTable>().count();
        while r > 0 {
            r -= 1;
            let (orders, markets) = world.get2_mut::<OrderTable, MarketStore>();
            let cell = orders.cell[r] as usize;
            let quantity = orders.quantity[r];
            if orders.ready_turn[r] < 0 {
                reserve(orders, markets, r, turn, lead, expiry);
                continue;
            }
            if turn >= orders.ready_turn[r] {
                spent += collect(world, &mut ctx.events, r, cell, quantity);
            }
        }
        ctx.services.get_mut::<MerchantTripStats>().spent_vehicles += spent;
        Ok(())
    }
}

/// An order waiting for its units: reserve them, or drop it when too long unfilled.
fn reserve(
    orders: &mut OrderTable,
    markets: &mut MarketStore,
    r: usize,
    turn: i32,
    lead: i32,
    expiry: i32,
) {
    let cell = orders.cell[r] as usize;
    let quantity = orders.quantity[r];
    if markets.stock[cell] >= quantity {
        markets.stock[cell] -= quantity;
        orders.ready_turn[r] = turn + lead;
    } else if turn - orders.placed_turn[r] >= expiry {
        orders.remove_row_swap_back(r);
    }
}

/// C# `Collect`; returns what a merchant group paid.
fn collect(
    world: &mut WorldState,
    events: &mut EventBus,
    r: usize,
    cell: usize,
    quantity: i32,
) -> i64 {
    let price = world.get::<MarketStore>().price[cell];
    let cost = i64::from(quantity) * i64::from(price);
    let (buyer, node, vehicle) = {
        let o = world.get::<OrderTable>();
        (o.buyer[r], o.node[r], o.vehicle[r])
    };
    let households = buyer < 0;
    let held = if households {
        world.get::<HouseholdCash>().cash[node as usize]
    } else {
        world.get::<AgentTable>().cash[buyer as usize]
    };
    if held < cost {
        world.get_mut::<MarketStore>().stock[cell] += quantity;
        world.get_mut::<OrderTable>().remove_row_swap_back(r);
        return 0;
    }
    let mut spent = 0;
    if households {
        let cash = world.get_mut::<HouseholdCash>();
        cash.cash[node as usize] -= cost;
        cash.spent_year[node as usize] += cost;
    } else {
        let agents = world.get_mut::<AgentTable>();
        let b = buyer as usize;
        agents.cash[b] -= cost;
        spent = cost;
        if vehicle > agents.vehicle[b] {
            agents.vehicle[b] = vehicle;
            events.publish(MerchantVehicleBought {
                agent: buyer,
                vehicle,
            });
        }
    }
    world.get_mut::<PurseState>().change_float(node, cost);
    let markets = world.get_mut::<MarketStore>();
    markets.supply[cell] = MarketStore::clamp_to_non_negative_int(
        i64::from(markets.supply[cell]) + i64::from(quantity),
    );
    markets.demand[cell] = MarketStore::clamp_to_non_negative_int(
        i64::from(markets.demand[cell]) + i64::from(quantity),
    );
    world.get_mut::<OrderTable>().remove_row_swap_back(r);
    spent
}

/// C# `HouseholdsOrder`.
fn households_order(world: &mut WorldState, db: &GameDatabase, nodes: &NodeTable, turn: i32) {
    let config = &db.config;
    let ask = Ask {
        db,
        demand: DemandParams::from_config(config),
        made: made_goods(db),
        markets: world.get::<MarketStore>(),
        population: world.get::<HouseholdState>(),
    };
    let cash = world.get::<HouseholdCash>();
    let currency = world.get::<CurrencyState>();
    let mut added: Vec<(i32, i32, i32)> = Vec::new();
    for n in 0..nodes.count() {
        let share = cash.luxury_budget_factor(n, config.household_budget_weeks);
        if share <= 0 {
            continue;
        }
        let level = usize::from(nodes.level[n]);
        let start = ask.markets.market_cell_start[n] as usize;
        let end = start + ask.markets.market_cell_count[n] as usize;
        let index = currency.price_index[nodes.continent[n] as usize].raw();
        for c in start..end {
            let units = ask.units(n, level, index, c, share);
            if units > 0 {
                added.push((
                    n as i32,
                    c as i32,
                    MarketStore::clamp_to_non_negative_int(units),
                ));
            }
        }
    }
    let orders = world.get_mut::<OrderTable>();
    for (n, c, units) in added {
        orders.add(-1, n, c, units, turn, 0);
    }
}

struct Ask<'a> {
    db: &'a GameDatabase,
    demand: DemandParams,
    made: Vec<bool>,
    markets: &'a MarketStore,
    population: &'a HouseholdState,
}

impl Ask<'_> {
    /// Units a quarter the households of `n` order at cell `c`, or zero for goods not made to order.
    fn units(&self, n: usize, level: usize, index: i64, c: usize, share: i64) -> i64 {
        let config = &self.db.config;
        let def = &self.db.goods[self.markets.cell_good[c] as usize];
        if !def.traits.made_to_order || def.base_demand <= 0 || !self.made[def.id.0 as usize] {
            return 0;
        }
        let (base, anchor) = cell_base(
            def.base_demand,
            def.base_price,
            config.level_demand_weight[level].raw(),
            Fx::ONE_RAW,
            index,
        );
        let units = weekly(
            &self.demand,
            WeeklyInput {
                weekly_at_anchor: base,
                price: self.markets.price[c],
                anchor,
                luxury_share: Fx::ONE_RAW,
                necessity_weight: self.population.necessity_weight(n, level, config).raw(),
                luxury_weight: self.population.luxury_weight(n, level, config).raw(),
            },
        )
        .units;
        Fx::round_shift(units.wrapping_mul(share)) * i64::from(QUARTER_WEEKS)
    }
}
