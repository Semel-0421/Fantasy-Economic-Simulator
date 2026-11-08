//! The scenario results screen as a financial statement (spec 5.2): revenue, costs by kind and net
//! profit, read from the events the player's commands publish each week.
//!
//! Every amount is in the coin of the continent the player started on, converted at the rates of
//! the week it happened. Net profit is the change of the player's money (coins on every continent
//! plus bank deposits); costs the events do not name land in `other`, so revenue minus the costs
//! always ties to net profit.
use crate::agents::banks::BankState;
use crate::agents::player::PlayerState;
use crate::context::SimContext;
use crate::economy::CurrencyState;
use crate::event_record::SimEventRecord;
use crate::world::{NodeBanks, NodeTable};

/// Costs of the scenario, by kind.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct StatementCosts {
    /// Goods bought.
    pub purchases: i64,
    /// Route fees, passage and tolls.
    pub travel: i64,
    /// Guards and insurance premiums.
    pub protection: i64,
    /// Cash and cargo taken on the road, after insurance refunds.
    pub losses: i64,
    /// Everything else that moved the player's money (interest, licences, fees, exchange-rate
    /// changes); negative when it added money that is not sales revenue.
    pub other: i64,
}

impl StatementCosts {
    pub fn total(&self) -> i64 {
        self.purchases + self.travel + self.protection + self.losses + self.other
    }
}

/// The results statement.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct StatementView {
    /// Goods sold, plus contract rewards and dividends the script paid.
    pub revenue: i64,
    pub costs: StatementCosts,
    /// `end_coins - start_coins`, the scenario's score.
    pub net_profit: i64,
    pub start_coins: i64,
    pub end_coins: i64,
    /// Weeks played.
    pub weeks: i32,
}

/// Running totals behind the statement.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct StatementLedger {
    home: usize,
    start_coins: i64,
    start_turn: i32,
    revenue: i64,
    purchases: i64,
    travel: i64,
    protection: i64,
    losses: i64,
    trip_continent: usize,
}

impl StatementLedger {
    /// Starts the ledger at the player's current money.
    pub fn start(ctx: &SimContext) -> Self {
        let p = ctx.world.get::<PlayerState>();
        let home = ctx.services.get::<NodeTable>().continent[p.node as usize] as usize;
        Self {
            home,
            start_coins: money(ctx, home),
            start_turn: ctx.turn(),
            trip_continent: home,
            ..Self::default()
        }
    }

    /// The continent whose coin the statement counts in.
    pub fn home(&self) -> usize {
        self.home
    }

    /// Adds one week's events.
    pub fn record(&mut self, ctx: &SimContext, events: &[SimEventRecord]) {
        for e in events {
            self.record_one(ctx, e);
        }
    }

    fn record_one(&mut self, ctx: &SimContext, e: &SimEventRecord) {
        let nodes = ctx.services.get::<NodeTable>();
        let currency = ctx.world.get::<CurrencyState>();
        let home = self.home;
        let to_home = |amount: i64, from: usize| currency.convert(amount, from, home);
        match e {
            SimEventRecord::GoodsTraded(t) => {
                let value = i64::from(t.filled) * i64::from(t.price);
                let value = to_home(value, nodes.continent[t.market as usize] as usize);
                let into = if t.bought {
                    &mut self.purchases
                } else {
                    &mut self.revenue
                };
                *into += value;
            }
            SimEventRecord::TripStarted(s) => {
                let q = s.paid;
                let fc = q.fee_continent as usize;
                self.trip_continent = fc;
                self.travel +=
                    to_home(q.fee + q.passage, fc) + to_home(q.toll, q.toll_continent as usize);
                self.protection += to_home(q.guards + q.insurance, fc);
            }
            SimEventRecord::TripEncounter(x) => {
                self.losses += to_home(x.loss_value - x.refund, self.trip_continent);
            }
            SimEventRecord::ScenarioDialog {
                coins, continent, ..
            } if *coins != 0 => {
                self.revenue += to_home(*coins, *continent as usize);
            }
            _ => {}
        }
    }

    /// The statement as of now.
    pub fn view(&self, ctx: &SimContext) -> StatementView {
        let end_coins = money(ctx, self.home);
        let net_profit = end_coins - self.start_coins;
        let mut costs = StatementCosts {
            purchases: self.purchases,
            travel: self.travel,
            protection: self.protection,
            losses: self.losses,
            other: 0,
        };
        costs.other = self.revenue - net_profit - costs.total();
        StatementView {
            revenue: self.revenue,
            costs,
            net_profit,
            start_coins: self.start_coins,
            end_coins,
            weeks: ctx.turn() - self.start_turn,
        }
    }
}

/// The player's coins on every continent plus bank deposits, in the `home` continent's coin.
pub fn money(ctx: &SimContext, home: usize) -> i64 {
    let currency = ctx.world.get::<CurrencyState>();
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    let coins: i64 = ctx
        .world
        .get::<PlayerState>()
        .coins
        .iter()
        .enumerate()
        .map(|(c, &v)| currency.convert(v, c, home))
        .sum();
    let deposits: i64 = ctx
        .world
        .get::<BankState>()
        .player_deposit
        .iter()
        .enumerate()
        .map(|(n, &v)| {
            let node = banks.nation_node(nodes, n as i32);
            currency.convert(v, nodes.continent[node as usize] as usize, home)
        })
        .sum();
    coins + deposits
}
