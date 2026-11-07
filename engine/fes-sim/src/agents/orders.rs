//! Made-to-order tables (C# `MadeToOrder.cs`): `agents.orders`, `agents.demand_forecast`, and the
//! unsaved `MerchantTripStats`.
use super::columns::{native_table, read_col, read_items, write_col, write_items};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

native_table! {
    /// Orders for goods made on request (C# `OrderTable`, version 1).
    OrderTable, "agents.orders", 1,
    {
        buyer: i32,
        node: i32,
        cell: i32,
        quantity: i32,
        placed_turn: i32,
        ready_turn: i32,
        vehicle: u8,
    }
}

/// What detailed merchants did on their trips (C# `MerchantTripStats`); not saved.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MerchantTripStats {
    pub trips: i64,
    pub guarded: i64,
    pub insured: i64,
    pub encounters: i64,
    pub loss_sales: i64,
    pub loss_amount: i64,
    pub carried_on: i64,
    pub forced_sales: i64,
    pub sold: i64,
    pub lost_with_groups: i64,
    pub spent_goods: i64,
    pub spent_fees: i64,
    pub spent_tolls: i64,
    pub spent_guards: i64,
    pub spent_cover: i64,
    pub spent_upkeep: i64,
    pub spent_stolen: i64,
    pub cover_refunds: i64,
    pub spent_discount: i64,
    pub spent_wages: i64,
    pub spent_tax: i64,
    pub spent_fees_to_associations: i64,
    pub spent_loans: i64,
    pub spent_vehicles: i64,
    pub spent_hedge: i64,
    pub hedge_paid: i64,
    pub forced_trips: i64,
}

/// What makers selling at each market plan to make (C# `DemandForecast`, `agents.demand_forecast`,
/// version 1); every column is per market cell.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DemandForecast {
    /// Raw Fx: the share of capacity factories selling there run at.
    pub plan: Vec<i64>,
    /// Units factories made this quarter.
    pub produced: Vec<i64>,
    pub forecast: Vec<i64>,
    pub stock_at_start: Vec<i64>,
}

impl DemandForecast {
    pub fn new(cells: usize) -> Self {
        Self {
            plan: vec![Fx::ONE_RAW; cells],
            produced: vec![0; cells],
            forecast: vec![0; cells],
            stock_at_start: vec![0; cells],
        }
    }
}

impl Component for DemandForecast {
    fn key(&self) -> &'static str {
        "agents.demand_forecast"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.plan);
        write_col(w, &self.produced);
        write_items(w, &self.forecast);
        write_items(w, &self.stock_at_start);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.plan)?;
        read_col(r, &mut self.produced)?;
        read_items(r, &mut self.forecast)?;
        read_items(r, &mut self.stock_at_start)
    }
}
