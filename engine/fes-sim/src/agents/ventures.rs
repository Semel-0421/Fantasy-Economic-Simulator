//! `agents.ventures` (C# `VentureSystem`, `Agents/Ventures.cs`, DEC-132): each trading venture home
//! from a voyage sells its cargo at its port and pays out its profit; in port it raises its capital
//! from its households and fits out a voyage to the far port.
use super::bank_seizure::trader_offer;
use super::bond_desk::take_from_households;
use super::credit::BondOffers;
use super::money_ledger::{across_continents, pay_nation_households};
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use super::securities::VentureTable;
use super::BankState;
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::{CurrencyState, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx};
use fes_data::SimulationConfig;
use std::sync::Arc;

/// C# `FinanceOrder.Ventures`.
pub const ORDER: i32 = sim_order::LATE + 8;

impl VentureTable {
    /// C# `AtSea`.
    pub fn at_sea(&self, v: usize, turn: i32) -> bool {
        self.sail_until[v] >= 0 && turn < self.sail_until[v]
    }
}

/// C# `VentureBook.Fundamental`.
pub fn venture_fundamental(ventures: &VentureTable, config: &SimulationConfig, v: usize) -> i64 {
    ((ventures.cash[v]
        + ventures.stake[v]
        + i64::from(config.venture_value_years) * ventures.profit_year[v].max(0))
        / i64::from(config.venture_shares))
    .max(1)
}

/// C# `VentureBook.HouseholdShares`.
pub fn household_shares(ventures: &VentureTable, config: &SimulationConfig, v: usize) -> i32 {
    config.venture_shares - ventures.player_shares[v]
}

/// `agents.ventures`.
pub struct VentureSystem;

impl System for VentureSystem {
    fn id(&self) -> &'static str {
        "agents.ventures"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = Arc::clone(&ctx.services.database);
        let (seed, turn) = (ctx.seed(), ctx.turn());
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let mut run = Voyages {
            world: &mut ctx.world,
            offers: &mut offers,
            config: &db.config,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
            seed,
            turn,
            stream: det_hash::of("agents.ventures.wreck"),
        };
        for v in 0..run.world.get::<VentureTable>().count() {
            let ventures = run.world.get::<VentureTable>();
            if ventures.at_sea(v, run.turn) {
                continue;
            }
            if ventures.sail_until[v] >= 0 {
                run.sell_cargo(v);
            } else {
                run.fit_out(v);
            }
        }
        *ctx.services.get_mut::<BondOffers>() = offers;
        Ok(())
    }
}

struct Voyages<'a> {
    world: &'a mut WorldState,
    offers: &'a mut BondOffers,
    config: &'a SimulationConfig,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    seed: u64,
    turn: i32,
    stream: u32,
}

impl Voyages<'_> {
    fn nation(&self, v: usize) -> i32 {
        let port = self.world.get::<VentureTable>().port[v];
        self.banks.nation_of(self.nodes, port)
    }

    fn sell_cargo(&mut self, v: usize) {
        let ventures = self.world.get_mut::<VentureTable>();
        if ventures.lost[v] != 0 {
            ventures.units[v] = 0;
        }
        let (port, good, units) = (ventures.port[v], ventures.good[v], ventures.units[v]);
        let found = find_nearest_market(self.nodes, self.world.get::<MarketStore>(), port, good);
        if let (true, Some((_, cell))) = (units > 0, found) {
            let price = i64::from(trader_offer(
                self.config.trader_spread,
                self.world.get::<MarketStore>().price[cell],
            ));
            let sold = self.world.get::<PurseState>().sellable(port, units, price);
            if sold > 0 {
                let worth = i64::from(sold) * price;
                let store = self.world.get_mut::<MarketStore>();
                store.supply[cell] = MarketStore::clamp_to_non_negative_int(
                    i64::from(store.supply[cell]) + i64::from(sold),
                );
                self.world
                    .get_mut::<PurseState>()
                    .change_float(port, -worth);
                let ventures = self.world.get_mut::<VentureTable>();
                ventures.cash[v] += worth;
                ventures.proceeds[v] += worth;
                ventures.units[v] -= sold;
            }
        }
        if self.world.get::<VentureTable>().units[v] > 0 {
            return;
        }
        self.close(v);
    }

    /// The voyage is over: holders expect the next to earn as the last ones did, and get this
    /// one's profit.
    fn close(&mut self, v: usize) {
        let weeks = i64::from(self.config.venture_voyage_weeks.max(1));
        let ventures = self.world.get_mut::<VentureTable>();
        let profit = ventures.proceeds[v] - ventures.stake[v];
        let yearly = profit * i64::from(WEEKS_PER_YEAR) / weeks;
        ventures.profit_year[v] = if ventures.voyages[v] == 0 {
            yearly
        } else {
            (ventures.profit_year[v] + yearly) / 2
        };
        ventures.voyages[v] += 1;
        if ventures.lost[v] != 0 {
            ventures.wrecks[v] += 1;
        }
        ventures.stake[v] = 0;
        ventures.proceeds[v] = 0;
        ventures.sail_until[v] = -1;
        ventures.good[v] = -1;
        ventures.lost[v] = 0;
        let cash = ventures.cash[v];
        if profit > 0 {
            self.pay_out(v, profit.min(cash));
        }
    }

    fn pay_out(&mut self, v: usize, amount: i64) {
        let nation = self.nation(v);
        let shares = i64::from(self.config.venture_shares);
        let ventures = self.world.get_mut::<VentureTable>();
        let to_player = amount * i64::from(ventures.player_shares[v]) / shares;
        let to_households = amount * i64::from(household_shares(ventures, self.config, v)) / shares;
        ventures.cash[v] -= to_player + to_households;
        pay_nation_households(self.world, nation, to_households);
        let bank = self.world.get_mut::<BankState>();
        bank.player_deposit[nation as usize] += to_player;
        bank.reserve[nation as usize] += to_player;
    }

    fn fit_out(&mut self, v: usize) {
        let config = self.config;
        let nation = self.nation(v);
        let cash = self.world.get::<VentureTable>().cash[v];
        if cash < config.venture_capital {
            let want = config.venture_capital - cash;
            let taken =
                take_from_households(self.world, self.offers, config, self.turn, nation, want);
            self.world.get_mut::<VentureTable>().cash[v] += taken;
        }
        let ventures = self.world.get::<VentureTable>();
        if ventures.cash[v] < config.venture_capital / 2 {
            return;
        }
        let (home, far, port) = (ventures.continent[v], ventures.far[v], ventures.port[v]);
        let far_continent = self.nodes.continent[far as usize];
        let store = self.world.get::<MarketStore>();
        let mut market = far;
        while market >= 0 && store.market_cell_count[market as usize] == 0 {
            market = self.nodes.parent[market as usize];
        }
        if market < 0 {
            return;
        }
        let Some(cell) = self.best(port, market as usize, home, far_continent) else {
            return;
        };
        self.sail(v, cell, home, far, far_continent);
    }

    fn sail(&mut self, v: usize, cell: usize, home: i32, far: i32, far_continent: i32) {
        let currency = self.world.get::<CurrencyState>();
        let cash = self.world.get::<VentureTable>().cash[v];
        let budget = currency.convert(cash, home as usize, far_continent as usize);
        let store = self.world.get::<MarketStore>();
        let price = i64::from(store.price[cell]);
        let units = i64::from(store.stock[cell]).min(budget / price.max(1)) as i32;
        if units <= 0 {
            return;
        }
        let cost = i64::from(units) * price;
        let back = currency.convert(cost, far_continent as usize, home as usize);
        let stake = cash.min(back.max(1));
        let good = store.cell_good[cell];
        across_continents(
            self.world.get_mut::<BankState>(),
            home,
            stake,
            far_continent,
            cost,
        );
        self.world.get_mut::<PurseState>().change_float(far, cost);
        let store = self.world.get_mut::<MarketStore>();
        store.stock[cell] -= units;
        store.supply[cell] = MarketStore::clamp_to_non_negative_int(
            i64::from(store.supply[cell]) + i64::from(units),
        );
        store.demand[cell] = MarketStore::clamp_to_non_negative_int(
            i64::from(store.demand[cell]) + i64::from(units),
        );
        let mut rng = DetRandom::for_key(self.seed, self.turn, self.stream, v as i32, 0);
        let lost = rng.chance_permille(self.config.venture_loss_permille);
        let ventures = self.world.get_mut::<VentureTable>();
        ventures.cash[v] -= stake;
        ventures.stake[v] = stake;
        ventures.good[v] = good;
        ventures.units[v] = units;
        ventures.sail_until[v] = self.turn + self.config.venture_voyage_weeks;
        ventures.lost[v] = u8::from(lost);
    }

    /// The far market's good in stock that sells at home for the most over its price there, by at
    /// least the margin.
    fn best(&self, port: i32, market: usize, home: i32, far_continent: i32) -> Option<usize> {
        let store = self.world.get::<MarketStore>();
        let currency = self.world.get::<CurrencyState>();
        let need = Fx::ONE + self.config.venture_min_margin;
        let mut best_ratio = Fx::ZERO;
        let mut best = None;
        let start = store.market_cell_start[market] as usize;
        for cell in start..start + store.market_cell_count[market] as usize {
            if store.stock[cell] <= 0 || store.price[cell] <= 0 {
                continue;
            }
            let Some((_, home_cell)) =
                find_nearest_market(self.nodes, store, port, store.cell_good[cell])
            else {
                continue;
            };
            let offer = trader_offer(self.config.trader_spread, store.price[home_cell]);
            let there = currency.convert(i64::from(offer), home as usize, far_continent as usize);
            let ratio = Fx::from_ratio(there, i64::from(store.price[cell]));
            if ratio < need || ratio <= best_ratio {
                continue;
            }
            best_ratio = ratio;
            best = Some(cell);
        }
        best
    }
}
