//! `agents.roads` (C# `RoadSystem`, `Agents/Roads.cs`, DEC-054): finishes roads under construction
//! and charges each paved route's upkeep to its maintainer; unpaid roads decay and are lost at zero.
use super::banks::BankState;
use super::money_ledger::pay_households;
use super::player::PlayerState;
use crate::context::SimContext;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::roads::{MAX_CONDITION, NO_OWNER, PLAYER_OWNER};
use crate::world::{NodeBanks, NodeTable, RoadState, RouteTable, WorldState};
use fes_core::WorldLevel;

/// C# `FinanceOrder.Roads`.
pub const ORDER: i32 = sim_order::LATE;

/// A road under construction was finished (C# `RoadBuilt`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct RoadBuilt {
    pub from: i32,
    pub to: i32,
    pub turn: i32,
}

/// A road decayed to nothing because nobody paid its upkeep (C# `RoadLost`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct RoadLost {
    pub from: i32,
    pub to: i32,
    pub turn: i32,
}

/// `agents.roads`.
pub struct RoadSystem;

impl System for RoadSystem {
    fn id(&self) -> &'static str {
        "agents.roads"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    /// Roads to the core towns are kept first (DEC-104).
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let mut k = Keeper {
            world: &mut ctx.world,
            events: &mut ctx.events,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
            routes: ctx.services.get::<RouteTable>(),
            upkeep: ctx.services.database.config.road_upkeep,
            decay: ctx.services.database.config.road_decay_permille,
            turn,
        };
        for pass in 0..2 {
            let seats = pass == 0;
            for r in 0..k.routes.count() {
                let (from, to) = (k.routes.from[r], k.routes.to[r]);
                let seat = k.nodes.is_seat(from) || k.nodes.is_seat(to);
                k.step_if(seat == seats, r);
            }
        }
        Ok(())
    }
}

struct Keeper<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    routes: &'a RouteTable,
    upkeep: i64,
    decay: i32,
    turn: i32,
}

impl Keeper<'_> {
    fn step_if(&mut self, due: bool, r: usize) {
        if due {
            self.step_road(r);
        }
    }

    fn step_road(&mut self, r: usize) {
        let turn = self.turn;
        let (from, to) = (self.routes.from[r], self.routes.to[r]);
        let roads = self.world.get_mut::<RoadState>();
        if roads.build_done_turn[r] >= 0 && turn >= roads.build_done_turn[r] {
            roads.build_done_turn[r] = -1;
            roads.condition[r] = MAX_CONDITION;
            roads.owner[r] = PLAYER_OWNER;
            self.events.publish(RoadBuilt { from, to, turn });
            return;
        }
        if !roads.is_paved(r) || roads.owner[r] == NO_OWNER || self.pay_upkeep(r) {
            return;
        }
        let roads = self.world.get_mut::<RoadState>();
        roads.condition[r] -= self.decay;
        if roads.condition[r] > 0 {
            return;
        }
        roads.condition[r] = 0;
        roads.owner[r] = NO_OWNER;
        self.events.publish(RoadLost { from, to, turn });
    }

    /// Upkeep is wages where the road starts (DEC-061).
    fn pay_upkeep(&mut self, r: usize) -> bool {
        if !self.take_upkeep(r) {
            return false;
        }
        pay_households(self.world, self.nodes, self.routes.from[r], self.upkeep);
        true
    }

    fn take_upkeep(&mut self, r: usize) -> bool {
        let upkeep = self.upkeep;
        let from = self.routes.from[r];
        let owner = self.world.get::<RoadState>().owner[r];
        let bank = self.world.get_mut::<BankState>();
        if owner >= 0 {
            if bank.spendable(owner as usize) < upkeep {
                return false;
            }
            bank.reserve[owner as usize] -= upkeep;
            return true;
        }
        let nation_node = self.nodes.ancestor_at(from, WorldLevel::Nation);
        if nation_node >= 0 {
            let nation = self.banks.nation_index(self.nodes, nation_node) as usize;
            if bank.player_deposit[nation] >= upkeep && bank.reserve[nation] >= upkeep {
                bank.player_deposit[nation] -= upkeep;
                bank.reserve[nation] -= upkeep;
                return true;
            }
        }
        let continent = self.nodes.continent[from as usize] as usize;
        let player = self.world.get_mut::<PlayerState>();
        if player.coins[continent] < upkeep {
            return false;
        }
        player.coins[continent] -= upkeep;
        true
    }
}
