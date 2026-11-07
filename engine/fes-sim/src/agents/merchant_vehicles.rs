//! `agents.merchant_vehicles` (C# `MerchantVehicleSystem`, `MerchantVehicles`, DEC-084, DEC-085):
//! once a year an automated merchant group's vehicle may wear down a step, and a group with enough
//! cash orders the next vehicle, which `agents.orders` delivers.
use super::facilities::MODE_AUTOMATED;
use super::merchants::AgentTable;
use super::orders::OrderTable;
use super::production::lines::find_nearest_market;
use crate::context::SimContext;
use crate::economy::MarketStore;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use fes_core::det::det_hash;
use fes_core::{DetRandom, QualityGrade};
use fes_data::database::GameDatabase;

/// C# `FinanceOrder.MerchantVehicles`.
pub const ORDER: i32 = sim_order::LATE + 22;

/// C# `MerchantVehicles` kinds.
pub const PACK: u8 = 0;
pub const CART: u8 = 1;
pub const WAGON: u8 = 2;
pub const COUNT: u8 = 3;

impl OrderTable {
    /// C# `OrderTable.Add`.
    pub fn add(&mut self, buyer: i32, node: i32, cell: i32, quantity: i32, turn: i32, vehicle: u8) {
        self.buyer.push(buyer);
        self.node.push(node);
        self.cell.push(cell);
        self.quantity.push(quantity);
        self.placed_turn.push(turn);
        self.ready_turn.push(-1);
        self.vehicle.push(vehicle);
    }
}

/// C# `MerchantVehicles.TryGood`: the good that makes a vehicle; none for packs.
pub fn vehicle_good(db: &GameDatabase, vehicle: u8) -> Option<i32> {
    let key = match vehicle {
        CART => "cart",
        WAGON => "wagon",
        _ => return None,
    };
    db.try_get_good(key, QualityGrade::Mid).map(|g| g.0)
}

/// `agents.merchant_vehicles`. `ordering` is scratch, rebuilt each run.
#[derive(Default)]
pub struct MerchantVehicleSystem {
    ordering: Vec<bool>,
}

impl System for MerchantVehicleSystem {
    fn id(&self) -> &'static str {
        "agents.merchant_vehicles"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        if !ctx.clock().is_new_year() || turn == 0 {
            return Ok(());
        }
        let seed = ctx.seed();
        let db = &ctx.services.database;
        let nodes = ctx.services.get::<NodeTable>();
        let stream = det_hash::of("agents.merchant_vehicle_wear");
        let world = &mut ctx.world;
        let count = world.get::<AgentTable>().count();
        self.ordering.clear();
        self.ordering.resize(count, false);
        for &buyer in &world.get::<OrderTable>().buyer {
            if buyer >= 0 {
                self.ordering[buyer as usize] = true;
            }
        }
        for a in 0..count {
            let agents = world.get_mut::<AgentTable>();
            if agents.mode[a] != MODE_AUTOMATED {
                continue;
            }
            if agents.vehicle[a] > PACK
                && DetRandom::for_key(seed, turn, stream, a as i32, 0).next_fx01()
                    < db.config.merchant_vehicle_wear
            {
                agents.vehicle[a] -= 1;
            }
            if let Some(order) = self.next_order(world, db, nodes, a) {
                let (home, cell, next) = order;
                world
                    .get_mut::<OrderTable>()
                    .add(a as i32, home, cell, 1, turn, next);
            }
        }
        Ok(())
    }
}

impl MerchantVehicleSystem {
    /// C# `OrderNextVehicle`: (home, cell, vehicle) of the order to place, if any.
    fn next_order(
        &self,
        world: &crate::world::WorldState,
        db: &GameDatabase,
        nodes: &NodeTable,
        a: usize,
    ) -> Option<(i32, i32, u8)> {
        let agents = world.get::<AgentTable>();
        let next = agents.vehicle[a] + 1;
        if next >= COUNT {
            return None;
        }
        let good = vehicle_good(db, next)?;
        if self.ordering[a] {
            return None;
        }
        let home = agents.home[a];
        let markets = world.get::<MarketStore>();
        let (_, nearest) = find_nearest_market(nodes, markets, home, good)?;
        let times = i64::from(db.config.merchant_vehicle_cash_times[next as usize]);
        if agents.cash[a] < i64::from(markets.price[nearest]) * times {
            return None;
        }
        Some((
            home,
            cell_to_order_from(nodes, markets, home, good, nearest),
            next,
        ))
    }
}

/// The nearest node with one in stock takes the order, else the nearest market, where factories
/// will see it.
fn cell_to_order_from(
    nodes: &NodeTable,
    markets: &MarketStore,
    home: i32,
    good: i32,
    nearest: usize,
) -> i32 {
    let mut m = home;
    while m >= 0 {
        if let Some(c) = markets.try_find_cell(m as usize, good) {
            if markets.stock[c] > 0 {
                return c as i32;
            }
        }
        m = nodes.parent[m as usize];
    }
    nearest as i32
}
