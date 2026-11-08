//! `agents.contracts` (C# `ContractSystem`, DEC-087): supply contracts. Each week the seller buys
//! the units from its market, the buyer pays the contract price and sells them into its own; each
//! year in each nation the widest price gaps are signed. Off unless `contract.per_nation` is set.
use super::bank_seizure::trader_offer;
use super::contracts::ContractTable;
use super::demand_forecast::at_anchor;
use super::facilities::{FacilityTable, OWNER_NPC};
use super::groups::GroupTable;
use super::merchants::AgentTable;
use super::order_system::QUARTER_WEEKS;
use super::orders::DemandForecast;
use super::policy::PolicyState;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::pricing::demand::DemandParams;
use crate::economy::{CurrencyState, HouseholdState, MarketStore};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx, GoodId};
use fes_data::database::GameDatabase;
use std::collections::HashSet;

/// C# `FinanceOrder.Contracts`.
pub const ORDER: i32 = sim_order::LATE + 26;
const WEEKS_PER_YEAR: i32 = 52;

/// A supply contract was signed (C# `SupplyContractSigned`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SupplyContractSigned {
    pub seller: i32,
    pub buyer: i32,
    pub quantity: i32,
    pub unit_price: i64,
}

/// A buyer could not pay and broke a contract (C# `SupplyContractBroken`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SupplyContractBroken {
    pub seller: i32,
    pub buyer: i32,
}

/// C# `ContractParty`: a group's row, or a merchant's as its complement.
pub fn of_merchant(agent: i32) -> i32 {
    !agent
}

/// The market cells a contract's goods leave and reach, and their nodes (C# `ContractRoute`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ContractRoute {
    pub from_cell: i32,
    pub to_cell: i32,
    pub from_node: i32,
    pub to_node: i32,
}

impl ContractTable {
    /// C# `ContractTable.Add`.
    #[allow(clippy::too_many_arguments)]
    pub fn add(
        &mut self,
        seller: i32,
        buyer: i32,
        route: ContractRoute,
        quantity: i32,
        unit_price: i64,
        end_turn: i32,
        prepaid: i64,
    ) {
        self.seller.push(seller);
        self.buyer.push(buyer);
        self.from_cell.push(route.from_cell);
        self.to_cell.push(route.to_cell);
        self.from_node.push(route.from_node);
        self.to_node.push(route.to_node);
        self.quantity.push(quantity);
        self.unit_price.push(unit_price);
        self.end_turn.push(end_turn);
        self.prepaid.push(prepaid);
    }
}

fn cash(world: &WorldState, party: i32) -> i64 {
    if party < 0 {
        world.get::<AgentTable>().cash[!party as usize]
    } else {
        world.get::<GroupTable>().cash[party as usize]
    }
}

fn pay(world: &mut WorldState, party: i32, amount: i64) {
    if party < 0 {
        world.get_mut::<AgentTable>().cash[!party as usize] += amount;
    } else {
        world.get_mut::<GroupTable>().cash[party as usize] += amount;
    }
}

/// `agents.contracts`. `refused` is cleared each new year before signing.
#[derive(Default)]
pub struct ContractSystem {
    refused: HashSet<i64>,
}

impl System for ContractSystem {
    fn id(&self) -> &'static str {
        "agents.contracts"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = &ctx.services.database;
        if db.config.contracts_per_nation <= 0 {
            return Ok(());
        }
        let turn = ctx.turn();
        let seed = ctx.seed();
        deliver(&mut ctx.world, &mut ctx.events, db, turn);
        if !ctx.clock().is_new_year() || turn == 0 {
            return Ok(());
        }
        self.refused.clear();
        let mut s = Signer {
            sys: self,
            db,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
            world: &mut ctx.world,
            events: &mut ctx.events,
            demand: DemandParams::from_config(&db.config),
            seed,
            turn,
        };
        for n in 0..s.banks.nation_count(s.nodes) {
            let mut k = 0;
            while k < db.config.contracts_per_nation && s.sign(n) {
                k += 1;
            }
        }
        Ok(())
    }
}

/// C# `Deliver`.
fn deliver(world: &mut WorldState, events: &mut EventBus, db: &GameDatabase, turn: i32) {
    let mut r = world.get::<ContractTable>().count();
    while r > 0 {
        r -= 1;
        let c = world.get::<ContractTable>();
        let (seller, buyer) = (c.seller[r], c.buyer[r]);
        if turn >= c.end_turn[r] {
            // What the seller did not deliver goes back, as far as it holds.
            let back = c.prepaid[r].min(cash(world, seller).max(0));
            pay(world, seller, -back);
            pay(world, buyer, back);
            world.get_mut::<ContractTable>().remove_row_swap_back(r);
            continue;
        }
        deliver_row(world, events, db, r);
    }
}

fn deliver_row(world: &mut WorldState, events: &mut EventBus, db: &GameDatabase, r: usize) {
    let config = &db.config;
    let c = world.get::<ContractTable>();
    let (from, to) = (c.from_cell[r] as usize, c.to_cell[r] as usize);
    let (seller, buyer, unit, prepaid) = (c.seller[r], c.buyer[r], c.unit_price[r], c.prepaid[r]);
    let (from_node, to_node, quantity) = (c.from_node[r], c.to_node[r], c.quantity[r]);
    let markets = world.get::<MarketStore>();
    let buy = i64::from(markets.price[from]);
    let sell = i64::from(trader_offer(config.trader_spread, markets.price[to]));
    let mut units = i64::from(quantity).min(i64::from(markets.stock[from]));
    if buy > 0 {
        units = units.min(cash(world, seller).max(0) / buy);
    }
    if units <= 0 {
        return;
    }
    let from_deposit = prepaid.min(config.contract_deposit_share.mul_int(units * unit));
    if cash(world, buyer) + units * sell < units * unit - from_deposit {
        events.publish(SupplyContractBroken { seller, buyer });
        world.get_mut::<ContractTable>().remove_row_swap_back(r);
        return;
    }
    // What the buyer's traders cannot pay for stays with the seller's market.
    let purses = world.get::<PurseState>();
    let float_there = purses.float[purses.node_float[to_node as usize] as usize].max(0);
    if sell > 0 {
        units = units.min(float_there / sell);
    }
    if units <= 0 {
        return;
    }
    let from_deposit = prepaid.min(config.contract_deposit_share.mul_int(units * unit));
    world.get_mut::<ContractTable>().prepaid[r] -= from_deposit;
    let markets = world.get_mut::<MarketStore>();
    markets.stock[from] -= units as i32;
    markets.supply[to] =
        MarketStore::clamp_to_non_negative_int(i64::from(markets.supply[to]) + units);
    let purses = world.get_mut::<PurseState>();
    purses.change_float(from_node, units * buy);
    purses.change_float(to_node, -units * sell);
    pay(world, seller, units * (unit - buy) - from_deposit);
    pay(world, buyer, units * (sell - unit) + from_deposit);
}

fn refuse_key(from_cell: i32, to_cell: i32) -> i64 {
    (i64::from(from_cell) << 32) | i64::from(to_cell as u32)
}

/// The terms both sides agree on (C# `AgreeTerms` outputs).
struct Terms {
    quantity: i32,
    unit: i64,
    term: i32,
}

struct Signer<'a> {
    sys: &'a mut ContractSystem,
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    demand: DemandParams,
    seed: u64,
    turn: i32,
}

impl Signer<'_> {
    fn markets(&self) -> &MarketStore {
        self.world.get::<MarketStore>()
    }

    fn index_of(&self, node: i32) -> i64 {
        self.world.get::<CurrencyState>().price_index[self.nodes.continent[node as usize] as usize]
            .raw()
    }

    /// What a market takes a week: its households at the settled price, or what left it lately.
    fn weekly_demand(&self, node: i32, cell: usize) -> i64 {
        let config = &self.db.config;
        let n = node as usize;
        let level = usize::from(self.nodes.level[n]);
        let population = self.world.get::<HouseholdState>();
        let good = &self.db.goods[self.markets().cell_good[cell] as usize];
        let households = at_anchor(
            &self.demand,
            config,
            level,
            good,
            self.index_of(node),
            population.necessity_weight(n, level, config).raw(),
            population.luxury_weight(n, level, config).raw(),
        );
        households.max(self.world.get::<DemandForecast>().forecast[cell] / i64::from(QUARTER_WEEKS))
    }

    fn anchor_of(&self, node: i32, cell: usize) -> i64 {
        let markets = self.markets();
        let base = i64::from(markets.good_base_price[markets.cell_good[cell] as usize]);
        Fx::round_shift(base.wrapping_mul(self.index_of(node)))
    }

    fn settled_price(&self, node: i32, cell: usize) -> i64 {
        i64::from(self.markets().price[cell]).min(self.anchor_of(node, cell))
    }

    /// C# `Sign`: the widest gap in the nation not yet under contract.
    fn sign(&mut self, nation: i32) -> bool {
        let Some(route) = self.widest_gap(nation) else {
            eprintln!("DBG nation {nation} no gap");
            return false;
        };

        let traded = self.markets().cell_good[route.from_cell as usize];
        let seller = self.seller_at(route.from_node, traded);
        let buyer = self.buyer_at(route.to_node, self.turn / WEEKS_PER_YEAR + route.from_cell);
        let parties = seller != i32::MIN
            && buyer != i32::MIN
            && seller != buyer
            && self.may_carry(seller, traded, route.to_node)
            && self.may_carry(buyer, traded, route.to_node);

        let terms = if parties {
            self.agree_terms(route, buyer)
        } else {
            None
        };
        let Some(t) = terms else {
            self.sys
                .refused
                .insert(refuse_key(route.from_cell, route.to_cell));
            return true;
        };
        let deposit = self
            .db
            .config
            .contract_deposit_share
            .mul_int(i64::from(t.quantity) * t.unit * i64::from(t.term));
        pay(self.world, buyer, -deposit);
        pay(self.world, seller, deposit);
        // Delivery runs before signing, so the first week is next turn.
        self.world.get_mut::<ContractTable>().add(
            seller,
            buyer,
            route,
            t.quantity,
            t.unit,
            self.turn + t.term + 1,
            deposit,
        );
        self.events.publish(SupplyContractSigned {
            seller,
            buyer,
            quantity: t.quantity,
            unit_price: t.unit,
        });
        true
    }

    /// A merchant without licenses trades no licensed good (DEC-024).
    fn may_carry(&self, party: i32, good: i32, node: i32) -> bool {
        if party >= 0 {
            return true;
        }
        let policy = self.world.get::<PolicyState>();
        let nation = self.banks.nation_of(self.nodes, node);
        let row = if nation < 0 {
            policy.nation_count
        } else {
            nation as usize
        };
        let base = GoodId(good).base_index() as usize;
        if policy.licensed[row * policy.base_good_count + base] == 0 {
            return true;
        }
        let stream = det_hash::of("agents.licenses.merchants");
        DetRandom::for_key(self.seed, 0, stream, !party, 0)
            .chance_permille(self.db.config.licensed_merchant_permille)
    }

    fn widest_gap(&self, nation: i32) -> Option<ContractRoute> {
        let purses = self.world.get::<PurseState>();
        let nodes = &purses.nation_nodes[nation as usize];
        let markets = self.markets();
        let mut best_gap = 0i64;
        let mut route = ContractRoute::default();
        for &a in nodes {
            let start = markets.market_cell_start[a as usize] as usize;
            let end = start + markets.market_cell_count[a as usize] as usize;
            let min = self.db.config.contract_min_stock;
            for c in (start..end).filter(|&c| markets.stock[c] >= min) {
                self.widest_gap_from(a, c, nodes, &mut best_gap, &mut route);
            }
        }
        (best_gap > 0).then_some(route)
    }

    fn widest_gap_from(
        &self,
        a: i32,
        c: usize,
        nodes: &[i32],
        best_gap: &mut i64,
        route: &mut ContractRoute,
    ) {
        let markets = self.markets();
        if self.anchor_of(a, c) - i64::from(markets.price[c]) <= *best_gap {
            return;
        }
        let good = markets.cell_good[c];
        for &b in nodes {
            if a == b {
                continue;
            }
            let Some(d) = markets.try_find_cell(b as usize, good) else {
                continue;
            };
            let gap = self.settled_price(b, d) - i64::from(markets.price[c]);
            if gap <= *best_gap
                || markets.stock[d] >= self.db.config.contract_min_stock
                || self.contracted(c as i32, d as i32)
            {
                continue;
            }
            if self.sys.refused.contains(&refuse_key(c as i32, d as i32))
                || self.weekly_demand(b, d) < 2
            {
                continue;
            }
            *best_gap = gap;
            *route = ContractRoute {
                from_cell: c as i32,
                to_cell: d as i32,
                from_node: a,
                to_node: b,
            };
        }
    }

    /// C# `AgreeTerms`: none when no terms suit both sides.
    fn agree_terms(&self, route: ContractRoute, buyer: i32) -> Option<Terms> {
        let config = &self.db.config;
        let markets = self.markets();
        let stock = markets.stock[route.from_cell as usize];
        let to_cell = route.to_cell as usize;
        let quantity = (i64::from(stock / config.contract_stock_weeks)).min(
            config
                .contract_demand_share
                .mul_int(self.weekly_demand(route.to_node, to_cell)),
        ) as i32;
        let quantity = quantity.max(1);
        let discount = Fx::min(
            config.contract_max_discount,
            config.contract_base_discount
                + config.contract_discount_per_unit * Fx::from_int(i64::from(quantity)),
        );
        let sell = self.settled_price(route.to_node, to_cell);
        let unit = (i64::from(markets.price[route.from_cell as usize]) + 1)
            .max(sell - discount.mul_int(sell));
        let weekly = i64::from(quantity) * unit;
        let held = cash(self.world, buyer);
        let afford = held.max(0) / config.contract_deposit_share.mul_int(weekly).max(1);
        let term = i64::from(stock / quantity)
            .min(afford)
            .min(i64::from(config.contract_max_weeks)) as i32;
        let ok = unit < sell
            && term >= config.contract_min_weeks
            && held >= weekly * i64::from(config.contract_credit_weeks);

        ok.then_some(Terms {
            quantity,
            unit,
            term,
        })
    }

    fn contracted(&self, from: i32, to: i32) -> bool {
        let c = self.world.get::<ContractTable>();
        (0..c.count()).any(|r| c.from_cell[r] == from && c.to_cell[r] == to)
    }

    /// The group making the good there, else the richest merchant based there.
    fn seller_at(&self, node: i32, good: i32) -> i32 {
        let f = self.world.get::<FacilityTable>();
        for i in 0..f.count() {
            if f.node[i] != node || f.owner[i] != OWNER_NPC || f.group[i] < 0 {
                continue;
            }
            let def = &self.db.facilities[f.def_index[i] as usize];
            if self.db.recipes[def.recipe]
                .outputs
                .iter()
                .any(|l| l.good.0 == good)
            {
                return f.group[i];
            }
        }
        self.richest_merchant(node)
    }

    /// By turns a merchant based there or the group with the most cash that owns something there.
    fn buyer_at(&self, node: i32, pick: i32) -> i32 {
        let merchant = self.richest_merchant(node);
        let f = self.world.get::<FacilityTable>();
        let groups = self.world.get::<GroupTable>();
        let mut best = -1i32;
        for i in 0..f.count() {
            if f.node[i] != node || f.owner[i] != OWNER_NPC || f.group[i] < 0 {
                continue;
            }
            let g = f.group[i];
            if best < 0 || groups.cash[g as usize] > groups.cash[best as usize] {
                best = g;
            }
        }
        if best < 0 {
            return merchant;
        }
        if merchant != i32::MIN && pick % 2 == 0 {
            merchant
        } else {
            best
        }
    }

    fn richest_merchant(&self, node: i32) -> i32 {
        let agents = self.world.get::<AgentTable>();
        let mut best = -1i32;
        for a in 0..agents.count() {
            if agents.home[a] == node
                && agents.owner[a] == OWNER_NPC
                && (best < 0 || agents.cash[a] > agents.cash[best as usize])
            {
                best = a as i32;
            }
        }
        if best < 0 {
            i32::MIN
        } else {
            of_merchant(best)
        }
    }
}
