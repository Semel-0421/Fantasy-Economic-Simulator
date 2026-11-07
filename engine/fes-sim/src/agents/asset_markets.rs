//! `agents.asset_markets` (C# `AssetMarketSystem`, `Agents/AssetMarkets.cs`, DEC-131, DEC-134):
//! rent on the player's plots, then each market's households pay interest on their margin debt,
//! borrow on a rising market or pay back on a call, and the premium moves; each quarter the
//! indicators are published and a market heats or bursts on them (no dice).
use super::asset_prices::{
    households_holder, list_shares, share_fundamental, share_price, Refs, PLAYER_HOLDER,
};
use super::bond_desk::{take_from_households, take_from_node};
use super::credit::BondOffers;
use super::facilities::{FacilityTable, OWNER_NPC};
use super::finance::event_kind::CREDIT_CRUNCH;
use super::groups::GroupTable;
use super::land::LandState;
use super::money_ledger::{lend_nation_households, pay_households, pay_nation_households};
use super::player::PlayerState;
use super::purses::PurseState;
use super::securities::{AssetMarkets, ShareTable, VentureTable};
use super::ventures::{household_shares, venture_fundamental};
use super::{BankState, FinanceState};
use crate::clock::{WEEKS_PER_QUARTER, WEEKS_PER_YEAR};
use crate::context::SimContext;
use crate::economy::{HouseholdCash, HouseholdState};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::{Fx, WorldLevel};
use fes_data::SimulationConfig;
use std::sync::Arc;

/// C# `FinanceOrder.AssetMarkets`.
pub const ORDER: i32 = sim_order::LATE + 9;
/// C# `AssetMarkets.History`: quarters of debt kept.
pub const HISTORY: usize = 4;
/// C# `AssetMarketPhase`.
pub const CALM: u8 = 0;
pub const HEATED: u8 = 1;
pub const BUST: u8 = 2;

/// C# `AssetMarketKind`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AssetMarketKind {
    Stock = 0,
    Property = 1,
    Venture = 2,
}

/// A market's price rose past the heated line (C# `MarketHeated`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MarketHeated {
    pub kind: AssetMarketKind,
    pub node: i32,
    pub premium: Fx,
    pub turn: i32,
}

/// A market burst on its published indicators (C# `MarketBurst`); `premium` is before the fall.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MarketBurst {
    pub kind: AssetMarketKind,
    pub node: i32,
    pub premium: Fx,
    pub turn: i32,
}

impl AssetMarkets {
    /// C# `Households`: a class's land trades at its core.
    pub fn households(&self, nodes: &NodeTable, node: usize) -> usize {
        if nodes.absorbed() {
            nodes.core[node] as usize
        } else {
            node
        }
    }

    /// C# `Property`.
    pub fn property(&self, nodes: &NodeTable, node: usize) -> usize {
        self.nation_count + self.households(nodes, node)
    }

    /// C# `Venture`.
    pub fn venture(&self, continent: i32) -> usize {
        self.nation_count + self.node_count + continent as usize
    }

    pub fn is_stock(&self, m: usize) -> bool {
        m < self.nation_count
    }

    pub fn is_venture(&self, m: usize) -> bool {
        m >= self.nation_count + self.node_count
    }

    /// C# `NationBorrows`: a nation's households borrow on an exchange, one node's on land.
    pub fn nation_borrows(&self, m: usize) -> bool {
        self.is_stock(m) || self.is_venture(m)
    }

    pub fn kind_of(&self, m: usize) -> AssetMarketKind {
        if self.is_stock(m) {
            AssetMarketKind::Stock
        } else if self.is_venture(m) {
            AssetMarketKind::Venture
        } else {
            AssetMarketKind::Property
        }
    }

    /// C# `NodeOf`: the nation's node, the market's own, or the continental exchange's.
    pub fn node_of(&self, m: usize, nodes: &NodeTable, banks: &NodeBanks) -> i32 {
        if self.is_stock(m) {
            banks.nation_node(nodes, m as i32)
        } else if self.is_venture(m) {
            self.exchange_node(nodes, banks, (m - self.venture(0)) as i32)
        } else {
            (m - self.nation_count) as i32
        }
    }

    /// C# `ExchangeNode`: the continent's international bank, else its seat.
    pub fn exchange_node(&self, nodes: &NodeTable, banks: &NodeBanks, continent: i32) -> i32 {
        let exchange = banks.international_bank_of(continent);
        if exchange >= 0 {
            return exchange;
        }
        nodes.core[(nodes.level_start(WorldLevel::Continent) + continent) as usize]
    }

    /// C# `VentureLender`: the nation of the continent's exchange.
    pub fn venture_lender(&self, nodes: &NodeTable, banks: &NodeBanks, continent: i32) -> i32 {
        let exchange = self.exchange_node(nodes, banks, continent);
        if exchange < 0 {
            -1
        } else {
            banks.nation_of(nodes, exchange)
        }
    }

    /// C# `NationDebt`: margin debt on the nation's exchange, its towns' land and a continental
    /// exchange it lends to.
    pub fn nation_debt(&self, nation: i32, nodes: &NodeTable, banks: &NodeBanks) -> i64 {
        let mut debt = self.debt[nation as usize];
        for n in 0..self.node_count {
            let d = self.debt[self.nation_count + n];
            if d != 0 && banks.nation_of(nodes, n as i32) == nation {
                debt += d;
            }
        }
        for c in 0..self.continent_count as i32 {
            if self.venture_lender(nodes, banks, c) == nation {
                debt += self.debt[self.venture(c)];
            }
        }
        debt
    }
}

/// `agents.asset_markets`. The scratch columns are rewritten by `Fundamentals` each run.
#[derive(Default)]
pub struct AssetMarketSystem {
    value: Vec<i64>,
    idle: Vec<i64>,
    lender: Vec<i32>,
}

impl System for AssetMarketSystem {
    fn id(&self) -> &'static str {
        "agents.asset_markets"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = Arc::clone(&ctx.services.database);
        let (turn, quarter_end) = (ctx.turn(), ctx.clock().is_quarter_end());
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let mut run = Run {
            world: &mut ctx.world,
            events: &mut ctx.events,
            offers: &mut offers,
            r: Refs {
                db: &db,
                nodes: ctx.services.get::<NodeTable>(),
                banks: ctx.services.get::<NodeBanks>(),
            },
            config: &db.config,
            turn,
            value: &mut self.value,
            idle: &mut self.idle,
            lender: &mut self.lender,
        };
        run.week(quarter_end);
        *ctx.services.get_mut::<BondOffers>() = offers;
        Ok(())
    }
}

struct Run<'a> {
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    offers: &'a mut BondOffers,
    r: Refs<'a>,
    config: &'a SimulationConfig,
    turn: i32,
    value: &'a mut Vec<i64>,
    idle: &'a mut Vec<i64>,
    lender: &'a mut Vec<i32>,
}

impl Run<'_> {
    fn markets(&self) -> &AssetMarkets {
        self.world.get::<AssetMarkets>()
    }

    fn week(&mut self, quarter_end: bool) {
        self.pay_rent();
        if self.config.market_credit_share.raw() <= 0 && self.config.market_momentum.raw() <= 0 {
            return;
        }
        self.fundamentals();
        for m in 0..self.markets().count() {
            if self.lender[m] < 0 || (self.value[m] <= 0 && self.markets().debt[m] == 0) {
                continue;
            }
            let net = self.credit(m);
            self.reprice(m, net);
        }
        if quarter_end {
            self.close_quarter();
        }
    }

    /// DEC-134: the node's households pay the player a week of rent on their plots, as far as
    /// their coins go, into the deposit at the nation's bank (the coins carried outside nations).
    fn pay_rent(&mut self) {
        let config = self.config;
        if config.land_rent_ppm <= 0 {
            return;
        }
        let nodes = self.r.nodes;
        for n in 0..self.world.get::<LandState>().player_plots.len() {
            let land = self.world.get::<LandState>();
            if land.player_plots[n] <= 0 {
                continue;
            }
            let households = self.markets().households(nodes, n);
            let year = land.year_rent(self.world.get::<HouseholdState>(), config, n);
            let cash = self.world.get::<HouseholdCash>().cash[households];
            let rent = (i64::from(land.player_plots[n]) * year / i64::from(WEEKS_PER_YEAR))
                .min(cash.max(0));
            if rent <= 0 {
                continue;
            }
            pay_households(self.world, nodes, n as i32, -rent);
            let nation = self.r.banks.nation_of(nodes, n as i32);
            if nation < 0 {
                let continent = nodes.continent[n] as usize;
                self.world.get_mut::<PlayerState>().coins[continent] += rent;
            } else {
                let bank = self.world.get_mut::<BankState>();
                bank.player_deposit[nation as usize] += rent;
                bank.reserve[nation as usize] += rent;
            }
        }
    }

    /// The fundamental value of what each market's households hold, and the bank each market
    /// borrows from.
    fn fundamentals(&mut self) {
        let (nodes, banks, config) = (self.r.nodes, self.r.banks, self.config);
        let markets = self.world.get::<AssetMarkets>();
        let count = markets.count();
        if self.value.len() != count {
            *self.value = vec![0; count];
            *self.lender = vec![0; count];
        }
        self.value.fill(0);
        for n in 0..markets.nation_count {
            self.lender[n] = n as i32;
        }
        for n in 0..nodes.count() {
            self.lender[markets.nation_count + n] = -1;
        }
        let shares = self.world.get::<ShareTable>();
        for row in 0..shares.count() {
            let holder = shares.holder[row];
            if holder < 0 && holder != PLAYER_HOLDER {
                let fundamental =
                    share_fundamental(self.world, self.r, shares.facility[row] as usize);
                self.value[(-1 - holder) as usize] += i64::from(shares.count[row]) * fundamental;
            }
        }
        let land = self.world.get::<LandState>();
        let households = self.world.get::<HouseholdState>();
        for n in 0..nodes.count() {
            let m = markets.property(nodes, n);
            let nation = banks.nation_of(nodes, n as i32);
            if nation < 0 {
                continue;
            }
            self.lender[m] = nation;
            self.value[m] +=
                i64::from(land.free(n)) * land.fundamental(nodes, households, config, n);
        }
        for c in 0..markets.continent_count as i32 {
            self.lender[markets.venture(c)] = markets.venture_lender(nodes, banks, c);
        }
        let ventures = self.world.get::<VentureTable>();
        for v in 0..ventures.count() {
            let m = markets.venture(ventures.continent[v]);
            self.value[m] += i64::from(household_shares(ventures, config, v))
                * venture_fundamental(ventures, config, v);
        }
    }

    /// The week's interest, then a call or new borrowing. Returns the credit that came into the
    /// market (less what left it).
    fn credit(&mut self, m: usize) -> i64 {
        let config = self.config;
        let nation = self.lender[m] as usize;
        let debt = self.markets().debt[m];
        if debt > 0 {
            // Interest, and a share of the debt paid off, so the room to borrow comes back.
            let rate = Fx::from_raw(self.world.get::<FinanceState>().loan_rate[nation]);
            let interest = rate.mul_int(debt) / i64::from(WEEKS_PER_YEAR);
            let principal = config.market_repay_share.mul_int(debt) / i64::from(WEEKS_PER_YEAR);
            let paid = self.take(m, interest + principal);
            self.world.get_mut::<BankState>().reserve[nation] += paid;
            self.world.get_mut::<AssetMarkets>().debt[m] += interest - paid;
        }
        let markets = self.markets();
        let value = Fx::from_raw(markets.premium[m]).mul_int(self.value[m]);
        let allowed = config.loan_to_value.mul_int(value);
        let debt = markets.debt[m];
        if debt > config.margin_call_share.mul_int(value) {
            let excess = debt - allowed;
            let paid = self.take(m, excess);
            self.world.get_mut::<BankState>().reserve[nation] += paid;
            let markets = self.world.get_mut::<AssetMarkets>();
            markets.debt[m] -= excess;
            markets.written_off[nation] += excess - paid; // what they cannot pay is lost
            return -excess;
        }
        if markets.phase[m] == BUST
            || markets.expectation[m] < 0
            || self.world.get::<FinanceState>().event_of(nation) == CREDIT_CRUNCH
        {
            return 0;
        }
        let room = config.market_credit_share.mul_int((allowed - debt).max(0));
        let draw = room.min(self.world.get::<BankState>().spendable(nation));
        if draw <= 0 {
            return 0;
        }
        self.world.get_mut::<BankState>().reserve[nation] -= draw;
        let markets = self.world.get_mut::<AssetMarkets>();
        markets.debt[m] += draw;
        if markets.nation_borrows(m) {
            lend_nation_households(self.world, nation as i32, draw);
        } else {
            let households = m - markets.nation_count;
            let cash = self.world.get_mut::<HouseholdCash>();
            cash.cash[households] += draw;
            cash.saved_year[households] -= draw; // borrowed, not earned
        }
        draw
    }

    fn take(&mut self, m: usize, amount: i64) -> i64 {
        if amount <= 0 {
            return 0;
        }
        let markets = self.markets();
        if markets.nation_borrows(m) {
            let nation = self.lender[m];
            take_from_households(
                self.world,
                self.offers,
                self.config,
                self.turn,
                nation,
                amount,
            )
        } else {
            let households = m - markets.nation_count;
            take_from_node(
                self.world,
                self.offers,
                self.config,
                self.turn,
                households,
                amount,
            )
        }
    }

    /// Buyers carry on the last quarter's change, credit moves the price by its share of the
    /// fundamental value, and the premium is pulled back toward one.
    fn reprice(&mut self, m: usize, net: i64) {
        let config = self.config;
        let value = self.value[m];
        let markets = self.world.get_mut::<AssetMarkets>();
        let mut premium = Fx::from_raw(markets.premium[m]);
        let mut change = premium * config.market_momentum * Fx::from_raw(markets.expectation[m])
            / Fx::from_int(i64::from(WEEKS_PER_QUARTER));
        if value > 0 && net != 0 {
            change = change + config.market_credit_weight * Fx::from_ratio(net, value);
        }
        change = change
            - config.market_reversion * (premium - Fx::ONE)
                / Fx::from_int(i64::from(WEEKS_PER_YEAR));
        let most = config.market_weekly_move * premium;
        premium = premium + Fx::clamp(change, -most, most);
        markets.premium[m] = Fx::clamp(
            premium,
            config.market_premium_floor,
            config.market_premium_cap,
        )
        .raw();
    }

    fn close_quarter(&mut self) {
        self.groups_trade();
        let markets = self.world.get_mut::<AssetMarkets>();
        markets.burst_nation.fill(false);
        markets.history_index = (markets.history_index + 1) % HISTORY as i32;
        self.idle_money();
        for m in 0..self.markets().count() {
            if self.lender[m] < 0 {
                continue;
            }
            let premium = Fx::from_raw(self.markets().premium[m]);
            let value = premium.mul_int(self.value[m]);
            self.expect(m, premium, value);
            let (leverage, growth) = self.publish(m, premium, value);
            self.judge(m, premium, leverage, growth);
        }
    }

    /// DEC-134: on a dear exchange groups list what they may of their facilities' shares; on a
    /// cheap one groups with spare cash buy back the households' shares.
    fn groups_trade(&mut self) {
        let config = self.config;
        if config.group_list_premium.raw() <= 0 && config.group_buyback_premium.raw() <= 0 {
            return;
        }
        for f in 0..self.world.get::<FacilityTable>().count() {
            let facilities = self.world.get::<FacilityTable>();
            let group = facilities.group[f];
            let nation = self.r.banks.nation_of(self.r.nodes, facilities.node[f]);
            let def = &self.r.db.facilities[facilities.def_index[f] as usize];
            if facilities.owner[f] != OWNER_NPC
                || group < 0
                || nation < 0
                || facilities.level[f] == 0
                || self.world.get::<GroupTable>().is_player[group as usize] != 0
                || def.state_owned
            {
                continue;
            }
            let premium = Fx::from_raw(self.markets().premium[nation as usize]);
            if config.group_list_premium.raw() > 0 && premium >= config.group_list_premium {
                self.list(f, group as usize, nation);
            } else if config.group_buyback_premium.raw() > 0
                && premium <= config.group_buyback_premium
            {
                self.buy_back(f, group as usize, nation);
            }
        }
    }

    fn list(&mut self, f: usize, group: usize, nation: i32) {
        let room = self.config.facility_max_listed_shares
            - self.world.get::<ShareTable>().outside(f as i32);
        if room <= 0 {
            return;
        }
        let amount = i64::from(room) * share_price(self.world, self.r, f);
        let raised = list_shares(
            self.world,
            self.offers,
            self.r,
            self.turn,
            f,
            nation,
            amount,
        );
        self.world.get_mut::<GroupTable>().cash[group] += raised;
    }

    fn buy_back(&mut self, f: usize, group: usize, nation: i32) {
        let config = self.config;
        let holder = households_holder(nation);
        let held = self.world.get::<ShareTable>().held(f as i32, holder);
        let groups = self.world.get::<GroupTable>();
        let keep = i64::from(config.bond_group_keep_times)
            * config.group_start_cash[groups.kind[group] as usize];
        let spare = groups.cash[group] - keep;
        if held <= 0 || spare <= 0 {
            return;
        }
        let price = share_price(self.world, self.r, f);
        let count = i64::from(held).min(spare / 2 / price) as i32;
        if count <= 0 {
            return;
        }
        let paid = i64::from(count) * price;
        self.world.get_mut::<GroupTable>().cash[group] -= paid;
        pay_nation_households(self.world, nation, paid);
        self.world
            .get_mut::<ShareTable>()
            .add(f as i32, holder, -count);
    }

    /// Buyers expect the last quarter's change to go on, and new idle money looks for something
    /// to buy.
    fn expect(&mut self, m: usize, premium: Fx, value: i64) {
        let config = self.config;
        let idle = self.idle[m];
        let weight = self.idle_weight(m);
        let turn = self.turn;
        let markets = self.world.get_mut::<AssetMarkets>();
        let mut expectation = premium / Fx::from_raw(markets.quarter_premium[m]) - Fx::ONE;
        if turn >= WEEKS_PER_QUARTER && value > 0 {
            expectation = expectation + weight * Fx::from_ratio(idle - markets.idle[m], value);
        }
        markets.idle[m] = idle;
        let most = config.market_weekly_move * Fx::from_int(i64::from(WEEKS_PER_QUARTER));
        markets.expectation[m] = Fx::clamp(expectation, -most, most).raw();
        markets.quarter_premium[m] = premium.raw();
    }

    fn idle_weight(&self, m: usize) -> Fx {
        let markets = self.markets();
        if markets.is_stock(m) {
            self.config.market_idle_weight
        } else if markets.is_venture(m) {
            self.config.market_venture_idle_weight
        } else {
            self.config.market_property_idle_weight
        }
    }

    /// The quarter's indicators: the price over the fundamental, leverage, and credit growth over
    /// the year. Returns (leverage, growth).
    fn publish(&mut self, m: usize, premium: Fx, value: i64) -> (Fx, Fx) {
        let config = self.config;
        let markets = self.world.get_mut::<AssetMarkets>();
        let slot = m * HISTORY + markets.history_index as usize;
        let year_ago = markets.debt_history[slot]; // the oldest, about to be replaced
        let debt = markets.debt[m];
        markets.debt_history[slot] = debt;
        let leverage = if value > 0 {
            Fx::from_ratio(debt, value)
        } else if debt > 0 {
            config.market_leverage_limit + Fx::ONE
        } else {
            Fx::ZERO
        };
        let growth = if year_ago > 0 {
            Fx::from_ratio(debt - year_ago, year_ago)
        } else if debt > 0 {
            Fx::ONE
        } else {
            Fx::ZERO
        };
        markets.published_premium[m] = premium.raw();
        markets.published_leverage[m] = leverage.raw();
        markets.published_credit_growth[m] = growth.raw();
        (leverage, growth)
    }

    /// What households hold above the weeks of spending they keep: a nation's for its exchange,
    /// a node's for its land, a continent's for its exchange.
    fn idle_money(&mut self) {
        let config = self.config;
        let nodes = self.r.nodes;
        let markets = self.world.get::<AssetMarkets>();
        if self.idle.len() != markets.count() {
            *self.idle = vec![0; markets.count()];
        }
        self.idle.fill(0);
        let cash = self.world.get::<HouseholdCash>();
        let purses = self.world.get::<PurseState>();
        for n in 0..nodes.count() {
            if markets.households(nodes, n) != n {
                continue;
            }
            let idle = (cash.cash[n] - i64::from(config.bond_keep_weeks) * cash.desired[n]).max(0);
            self.idle[markets.property(nodes, n)] = idle;
            let nation = purses.node_float[n] as usize;
            if nation < markets.nation_count {
                self.idle[nation] += idle;
            }
            self.idle[markets.venture(nodes.continent[n])] += idle;
        }
    }

    /// Heated above the first line; bursts above the second, or when heated and credit shrinks on
    /// a leveraged market or the bank's cover falls under its floor. The bust ends once the price
    /// stops falling.
    fn judge(&mut self, m: usize, premium: Fx, leverage: Fx, growth: Fx) {
        let config = self.config;
        let markets = self.markets();
        let phase = markets.phase[m];
        if phase == BUST {
            if markets.expectation[m] >= 0 {
                self.world.get_mut::<AssetMarkets>().phase[m] = CALM;
            }
            return;
        }
        let cover = match self.world.get::<FinanceState>().latest() {
            None => Fx::from_int(1000),
            Some(latest) => latest.reserve_ratio(self.lender[m] as usize),
        };
        let heated = phase == HEATED;
        let burst = premium >= config.market_burst_premium
            || (heated && growth < Fx::ZERO && leverage > config.market_leverage_limit)
            || (heated && config.bank_run_cover.raw() > 0 && cover < config.bank_run_cover);
        if burst {
            self.burst(m, premium);
        } else if !heated && premium >= config.market_heated_premium {
            self.world.get_mut::<AssetMarkets>().phase[m] = HEATED;
            let (kind, node) = self.kind_and_node(m);
            let turn = self.turn;
            self.events.publish(MarketHeated {
                kind,
                node,
                premium,
                turn,
            });
        } else if heated && premium < config.market_heated_premium {
            self.world.get_mut::<AssetMarkets>().phase[m] = CALM;
        }
    }

    fn kind_and_node(&self, m: usize) -> (AssetMarketKind, i32) {
        let markets = self.markets();
        (
            markets.kind_of(m),
            markets.node_of(m, self.r.nodes, self.r.banks),
        )
    }

    /// A burst drops the price at once and turns what buyers expect. A stock exchange stops its
    /// nation's credit; land its nation's, and a continental exchange every nation's of its
    /// continent, only when the bank has lent enough on it.
    fn burst(&mut self, m: usize, premium: Fx) {
        let config = self.config;
        let (nodes, banks) = (self.r.nodes, self.r.banks);
        let nation = self.lender[m] as usize;
        let lent = self
            .world
            .get::<FinanceState>()
            .latest()
            .map(|latest| latest.nation_lent[nation]);
        let fallen = premium - config.market_burst_drop * premium;
        let markets = self.world.get_mut::<AssetMarkets>();
        markets.premium[m] = fallen.raw();
        markets.quarter_premium[m] = fallen.raw();
        markets.expectation[m] = (-config.market_burst_drop).raw();
        markets.phase[m] = BUST;
        markets.bursts[m] += 1;
        let reaches = markets.is_stock(m)
            || lent.is_none_or(|lent| markets.debt[m] >= config.market_crunch_share.mul_int(lent));
        if reaches && markets.is_venture(m) {
            let continent = (m - markets.venture(0)) as i32;
            let on_continent = |n: usize| {
                let node = banks.nation_node(nodes, n as i32);
                nodes.continent[node as usize] == continent
            };
            for n in (0..markets.nation_count).filter(|&n| on_continent(n)) {
                markets.burst_nation[n] = true;
            }
        } else if reaches {
            markets.burst_nation[nation] = true;
        }
        let (kind, node) = self.kind_and_node(m);
        let turn = self.turn;
        self.events.publish(MarketBurst {
            kind,
            node,
            premium,
            turn,
        });
    }
}
