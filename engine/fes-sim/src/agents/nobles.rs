//! Who holds each fief (C# `Nobles.cs`, `agents.nobles`, version 1). The map decides the holders,
//! so only money and contracts are saved.
use super::columns::read_len;
use super::money_ledger::to_state;
use crate::world::{Component, NodeBanks, NodeTable, WorldState};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::WorldLevel;
use fes_data::SimulationConfig;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct NobleState {
    /// Per node: the purse of the house seated there.
    pub cash: Vec<i64>,
    /// Per vassal: the protection its lord owes for last year's aid.
    pub protection_owed: Vec<i64>,
    /// Per vassal: false for a year after its lord failed to protect it.
    pub contract_holds: Vec<bool>,
    /// Per city: the house that holds it, -1 elsewhere. Derived from the map, not saved.
    pub city_holder: Vec<i32>,
    /// Per node: its lord's node, or -1 when it is no vassal. Derived from the map, not saved.
    pub lord: Vec<i32>,
}

impl NobleState {
    pub fn new(nodes: &NodeTable) -> Self {
        let count = nodes.count();
        let mut city_holder = vec![-1i32; count];
        let mut lord = vec![-1i32; count];
        let mut domain_taken = vec![false; count];
        for n in 0..count as i32 {
            if nodes.level_of(n) != WorldLevel::City {
                continue;
            }
            let nation = nodes.ancestor_at(n, WorldLevel::Nation);
            if nation < 0 {
                city_holder[n as usize] = n;
                continue;
            }
            let taken = domain_taken[nation as usize];
            city_holder[n as usize] = if taken { n } else { nation };
            if taken {
                lord[n as usize] = nation;
            }
            domain_taken[nation as usize] = true;
        }
        for n in 0..count as i32 {
            if nodes.level_of(n) != WorldLevel::Village {
                continue;
            }
            let city = nodes.ancestor_at(n, WorldLevel::City);
            lord[n as usize] = if city >= 0 {
                city_holder[city as usize]
            } else {
                nodes.ancestor_at(n, WorldLevel::Nation)
            };
        }
        Self {
            cash: vec![0; count],
            protection_owed: vec![0; count],
            contract_holds: vec![true; count],
            city_holder,
            lord,
        }
    }
}

impl Component for NobleState {
    fn key(&self) -> &'static str {
        "agents.nobles"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.cash.len() as i32);
        for n in 0..self.cash.len() {
            let _ = w.write_i64(self.cash[n]);
            let _ = w.write_i64(self.protection_owed[n]);
            let _ = w.write_bool(self.contract_holds[n]);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_len(r, self.cash.len())?;
        for n in 0..self.cash.len() {
            self.cash[n] = r.read_i64()?;
            self.protection_owed[n] = r.read_i64()?;
            self.contract_holds[n] = r.read_bool()?;
        }
        Ok(())
    }
}

/// Income tax raised at a node (C# `Nobles.CollectTax`): the royal house takes its share first, then
/// the city's holder and the village's noble; the state's reserve gets the rest. Outside a nation it
/// all goes to the state.
pub fn collect_tax(
    world: &mut WorldState,
    nodes: &NodeTable,
    banks: &NodeBanks,
    config: &SimulationConfig,
    node: i32,
    amount: i64,
) {
    let nation = nodes.ancestor_at(node, WorldLevel::Nation);
    let mut rest = amount;
    if nation >= 0 {
        let state = world.get_mut::<NobleState>();
        let royal = config.noble_royal_share.mul_int(amount);
        state.cash[nation as usize] += royal;
        rest -= royal;
        let city = nodes.ancestor_at(node, WorldLevel::City);
        if city >= 0 {
            let holder = state.city_holder[city as usize];
            let share = config.noble_city_share.mul_int(amount);
            state.cash[holder as usize] += share;
            rest -= share;
        }
        if nodes.level_of(node) == WorldLevel::Village {
            let share = config.noble_village_share.mul_int(amount);
            state.cash[node as usize] += share;
            rest -= share;
        }
    }
    to_state(world, nodes, banks, node, rest);
}
