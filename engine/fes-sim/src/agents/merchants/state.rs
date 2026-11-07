//! Moves the columns the detailed job writes out of the world and back (O(1) per column), so the
//! job can hold them all mutably while it reads the rest of the world.
use super::job::JobState;
use super::AgentTable;
use crate::agents::{
    BankState, BillRoom, BillTable, CargoTable, DevelopmentState, GroupTable, PurseState,
};
use crate::economy::{HouseholdCash, MarketStore};
use crate::world::WorldState;
use std::mem::take;

impl JobState {
    pub fn take_from(world: &mut WorldState) -> Self {
        let mut s = Self::default();
        let agents = world.get_mut::<AgentTable>();
        s.memory = take(&mut agents.memory);
        s.node = take(&mut agents.node);
        s.destination = take(&mut agents.destination);
        s.arrival_turn = take(&mut agents.arrival_turn);
        s.negative_weeks = take(&mut agents.negative_cash_weeks);
        s.cash = take(&mut agents.cash);
        s.load_cost = take(&mut agents.load_cost);
        s.hedge = take(&mut agents.hedge_floor);
        let markets = world.get_mut::<MarketStore>();
        s.stock = take(&mut markets.stock);
        s.supply = take(&mut markets.supply);
        s.demand = take(&mut markets.demand);
        s.input_bid = take(&mut markets.input_bid);
        s.float = take(&mut world.get_mut::<PurseState>().float);
        s.imports = take(&mut world.get_mut::<DevelopmentState>().imports);
        let households = world.get_mut::<HouseholdCash>();
        s.household_cash = take(&mut households.cash);
        s.home_grown = take(&mut households.home_grown);
        s.group_cash = take(&mut world.get_mut::<GroupTable>().cash);
        let cargo = world.get_mut::<CargoTable>();
        s.cargo_holder = take(&mut cargo.holder);
        s.cargo_good = take(&mut cargo.good);
        s.cargo_quantity = take(&mut cargo.quantity);
        let bills = world.get_mut::<BillTable>();
        s.bill_holder = take(&mut bills.holder);
        s.bill_purse = take(&mut bills.purse);
        s.bill_face = take(&mut bills.face);
        s.bill_due = take(&mut bills.due_turn);
        let room = world.get_mut::<BillRoom>();
        s.bill_room = take(&mut room.room);
        s.bill_bank_room = take(&mut room.bank_room);
        s.bill_bank_spent = take(&mut room.bank_spent);
        s.international = take(&mut world.get_mut::<BankState>().international_reserve);
        s
    }

    pub fn restore(self, world: &mut WorldState) {
        let s = self;
        let agents = world.get_mut::<AgentTable>();
        agents.memory = s.memory;
        agents.node = s.node;
        agents.destination = s.destination;
        agents.arrival_turn = s.arrival_turn;
        agents.negative_cash_weeks = s.negative_weeks;
        agents.cash = s.cash;
        agents.load_cost = s.load_cost;
        agents.hedge_floor = s.hedge;
        let markets = world.get_mut::<MarketStore>();
        markets.stock = s.stock;
        markets.supply = s.supply;
        markets.demand = s.demand;
        markets.input_bid = s.input_bid;
        world.get_mut::<PurseState>().float = s.float;
        world.get_mut::<DevelopmentState>().imports = s.imports;
        let households = world.get_mut::<HouseholdCash>();
        households.cash = s.household_cash;
        households.home_grown = s.home_grown;
        world.get_mut::<GroupTable>().cash = s.group_cash;
        let cargo = world.get_mut::<CargoTable>();
        cargo.holder = s.cargo_holder;
        cargo.good = s.cargo_good;
        cargo.quantity = s.cargo_quantity;
        let bills = world.get_mut::<BillTable>();
        bills.holder = s.bill_holder;
        bills.purse = s.bill_purse;
        bills.face = s.bill_face;
        bills.due_turn = s.bill_due;
        let room = world.get_mut::<BillRoom>();
        room.room = s.bill_room;
        room.bank_room = s.bill_bank_room;
        room.bank_spent = s.bill_bank_spent;
        world.get_mut::<BankState>().international_reserve = s.international;
    }
}
