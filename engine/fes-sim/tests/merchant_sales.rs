//! C# `MerchantSaleTests` ported: a group weighs selling here against carrying the load on
//! (DEC-093), sells for bills (DEC-097), to households first (DEC-124), with cover (DEC-099).
mod common;

use common::build_sample;
use fes_core::QualityGrade;
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::merchants::traits::{trait_of, AGGRESSIVE, EXPEDITION};
use fes_sim::agents::{
    AgentTable, BankState, BillRoom, BillTable, CargoTable, GroupTable, MerchantSystem,
    MerchantTripStats, PurseState,
};
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::world::{NodeTable, RouteTable};
use fes_sim::Simulation;

const PLENTY: i64 = 1_000_000;
const MERCHANT_HOLDER_BASE: i32 = 2 << 28;
const MODE_MANUAL: u8 = 0;
const BILL_HOLDER_BANK: i32 = -1;

/// One group at the start of a route, every other group still, and no stock at its node.
struct Scene {
    sim: Simulation,
    good: i32,
    a: usize,
    here: i32,
}

struct Options<'a> {
    same_purse: bool,
    bills: bool,
    spread: i32,
    good: &'a str,
    extra: &'a [&'a str],
}

const DEFAULT: Options = Options {
    same_purse: true,
    bills: false,
    spread: 0,
    good: "wheat",
    extra: &[],
};

impl Scene {
    fn new(o: Options) -> Self {
        let bills = format!("credit.bill_cap_weeks,{}", if o.bills { 8 } else { 0 });
        let spread = format!("markets.trader_spread_permille,{}", o.spread);
        let mut lines = vec!["lod.mode,1", "merchants.must_act,0", &bills, &spread];
        lines.extend_from_slice(o.extra);
        let mut sim = build_sample(&lines);
        sim.step().expect("step"); // LOD is set on the first turn
        let db = std::sync::Arc::clone(&sim.context().services.database);
        let good = db.try_get_good(o.good, QualityGrade::Mid).expect("good").0;
        let ctx = sim.context();
        let (nodes, routes) = (
            ctx.services.get::<NodeTable>(),
            ctx.services.get::<RouteTable>(),
        );
        let (markets, purses) = (
            ctx.world.get::<MarketStore>(),
            ctx.world.get::<PurseState>(),
        );
        let route = (0..routes.count())
            .find(|&r| {
                let (from, to) = (routes.from[r] as usize, routes.to[r] as usize);
                nodes.continent[from] == nodes.continent[to]
                    && routes.travel_turns[r] <= 2
                    && (purses.node_float[from] == purses.node_float[to]) == o.same_purse
                    && markets.carries(from, good)
                    && markets.carries(to, good)
            })
            .expect("a route");
        let here = routes.from[route];
        let a = (0..ctx.world.get::<AgentTable>().count())
            .find(|&a| ctx.world.get::<AgentTable>().owner[a] == OWNER_NPC)
            .expect("an NPC group");
        let mut scene = Self { sim, good, a, here };
        scene.stage();
        scene
    }

    fn stage(&mut self) {
        let (a, here, holder) = (self.a, self.here, self.holder());
        let w = &mut self.sim.context_mut().world;
        let agents = w.get_mut::<AgentTable>();
        for (i, mode) in agents.mode.iter_mut().enumerate() {
            if i != a {
                *mode = MODE_MANUAL;
            }
        }
        agents.node[a] = here;
        agents.home[a] = here;
        agents.destination[a] = -1;
        agents.arrival_turn[a] = -1;
        agents.trait_id[a] = trait_of(AGGRESSIVE, EXPEDITION);
        agents.cash[a] = PLENTY;
        agents.negative_cash_weeks[a] = 0;
        w.get_mut::<CargoTable>().clear(holder);
        let markets = w.get_mut::<MarketStore>();
        let start = markets.market_cell_start[here as usize] as usize;
        let end = start + markets.market_cell_count[here as usize] as usize;
        markets.stock[start..end].fill(0);
        self.set_float(here, PLENTY);
    }

    fn holder(&self) -> i32 {
        MERCHANT_HOLDER_BASE + self.a as i32
    }

    fn agents(&self) -> &AgentTable {
        self.sim.world().get::<AgentTable>()
    }

    fn agents_mut(&mut self) -> &mut AgentTable {
        self.sim.context_mut().world.get_mut::<AgentTable>()
    }

    fn stats(&self) -> &MerchantTripStats {
        self.sim.context().services.get::<MerchantTripStats>()
    }

    fn purse(&self) -> usize {
        self.sim.world().get::<PurseState>().node_float[self.here as usize] as usize
    }

    fn set_float(&mut self, node: i32, coins: i64) {
        let purses = self.sim.context_mut().world.get_mut::<PurseState>();
        let purse = purses.node_float[node as usize] as usize;
        purses.float[purse] = coins;
    }

    fn cell(&self, node: i32) -> usize {
        let markets = self.sim.world().get::<MarketStore>();
        markets
            .try_find_cell(node as usize, self.good)
            .expect("cell")
    }

    fn load(&mut self, units: i32, cost: i64) {
        let (holder, good, a) = (self.holder(), self.good, self.a);
        let w = &mut self.sim.context_mut().world;
        w.get_mut::<CargoTable>().change(holder, good, units);
        w.get_mut::<AgentTable>().load_cost[a] = cost;
    }

    /// The price of the good here, and in every market a route from here reaches.
    fn prices(&mut self, here: i32, elsewhere: i32) {
        let routes = self.sim.context().services.get::<RouteTable>().clone();
        let first = routes.out_start[self.here as usize] as usize;
        for k in first..first + routes.out_count[self.here as usize] as usize {
            let to = routes.to[routes.route_index[k] as usize] as usize;
            let markets = self.sim.context_mut().world.get_mut::<MarketStore>();
            if let Some(cell) = markets.try_find_cell(to, self.good) {
                markets.price[cell] = elsewhere;
            }
        }
        let own = self.cell(self.here);
        self.sim.context_mut().world.get_mut::<MarketStore>().price[own] = here;
    }

    fn run(&mut self) {
        let ctx = self.sim.context_mut();
        *ctx.services.get_mut::<MerchantTripStats>() = MerchantTripStats::default();
        MerchantSystem::default().run_detailed(ctx);
    }

    fn carried(&self) -> i64 {
        let cargo = self.sim.world().get::<CargoTable>();
        (0..cargo.count())
            .filter(|&r| cargo.holder[r] == self.holder())
            .map(|r| i64::from(cargo.quantity[r]))
            .sum()
    }

    fn travelling(&self) -> bool {
        self.agents().destination[self.a] >= 0
    }

    /// Every coin the detailed step can move.
    fn money(&self) -> i64 {
        let w = self.sim.world();
        let bank = w.get::<BankState>();
        w.get::<AgentTable>().cash.iter().sum::<i64>()
            + w.get::<HouseholdCash>().cash.iter().sum::<i64>()
            + w.get::<PurseState>().float.iter().sum::<i64>()
            + bank.reserve.iter().sum::<i64>()
            + bank.international_reserve.iter().sum::<i64>()
            + w.get::<GroupTable>().cash.iter().sum::<i64>()
    }
}

#[test]
fn load_worth_more_elsewhere_is_carried_on_and_money_stays_put() {
    let mut s = Scene::new(DEFAULT);
    s.load(10, 1_000);
    s.prices(10, 1_000);
    let (money, cash) = (s.money(), s.agents().cash[s.a]);
    s.run();
    assert!(s.travelling());
    assert!(s.carried() > 0);
    assert!(s.agents().cash[s.a] < cash, "the trip is paid for");
    assert_eq!(s.stats().carried_on, 1);
    assert_eq!(s.stats().loss_sales, 0);
    assert_eq!(s.money(), money);
}

#[test]
fn gap_smaller_than_the_trip_sells_here_below_cost_and_counts_the_loss() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 10_000);
    s.prices(50, 50);
    let (money, cash) = (s.money(), s.agents().cash[s.a]);
    s.run();
    assert!(!s.travelling());
    assert_eq!(s.carried(), 0);
    assert_eq!(s.stats().loss_sales, 1);
    assert_eq!(s.stats().loss_amount, 5_000, "paid 10,000 and got 5,000");
    assert_eq!(s.agents().load_cost[s.a], 0);
    assert!(s.agents().cash[s.a] > cash);
    assert_eq!(s.money(), money);
}

#[test]
fn sale_above_cost_is_no_loss() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 1_000);
    s.prices(50, 50);
    s.run();
    assert_eq!(s.carried(), 0);
    assert_eq!(s.stats().loss_sales, 0);
    assert_eq!(s.stats().loss_amount, 0);
}

#[test]
fn group_that_would_not_last_the_trip_sells_here_whatever_it_loses() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 10_000);
    s.prices(10, 1_000);
    let weeks = s
        .sim
        .context()
        .services
        .database
        .config
        .merchant_bankrupt_weeks;
    let a = s.a;
    s.agents_mut().cash[a] = -1;
    s.agents_mut().negative_cash_weeks[a] = weeks - 1;
    s.run();
    assert!(!s.travelling());
    assert_eq!(s.carried(), 0);
    assert_eq!(s.stats().forced_sales, 1);
    assert_eq!(s.stats().loss_sales, 1);
    assert!(s.agents().cash[a] > 0, "the sale saved it");
    assert_eq!(s.agents().negative_cash_weeks[a], 0);
}

#[test]
fn group_in_debt_with_time_left_may_still_carry_on() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 10_000);
    s.prices(10, 1_000);
    let a = s.a;
    s.agents_mut().cash[a] = -1;
    s.run();
    assert!(s.travelling());
    assert_eq!(s.stats().forced_sales, 0);
}

#[test]
fn traders_who_cannot_pay_are_not_worth_a_trip() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 10_000);
    s.prices(10, 1_000);
    s.set_float(s.here, 0); // the same purse pays at both ends
    let cash = s.agents().cash[s.a];
    s.run();
    assert!(
        !s.travelling(),
        "it waits instead of paying for a trip to no buyer"
    );
    assert_eq!(s.carried(), 100);
    assert!(s.agents().cash[s.a] > cash - 1_000, "only upkeep went");
}

#[test]
fn partial_sale_keeps_the_books_of_what_is_left() {
    let mut s = Scene::new(DEFAULT);
    s.load(100, 2_000);
    s.prices(10, 10);
    s.set_float(s.here, 500);
    s.run();
    assert_eq!(s.carried(), 50);
    assert_eq!(s.agents().load_cost[s.a], 1_000);
    assert_eq!(s.stats().loss_sales, 1);
    assert_eq!(s.stats().loss_amount, 500);
}

fn bill_scene(bank_room: i64) -> (Scene, usize, usize, i64, i64) {
    let mut s = Scene::new(Options {
        bills: true,
        ..DEFAULT
    });
    s.load(100, 500);
    s.prices(10, 10);
    s.set_float(s.here, 0);
    let purse = s.purse();
    let room = s.sim.context_mut().world.get_mut::<BillRoom>();
    room.room[purse] = PLENTY;
    room.bank_room[purse] = bank_room;
    let before = s.sim.world().get::<BillTable>().count();
    let (cash, money) = (s.agents().cash[s.a], s.money());
    s.run();
    (s, purse, before, cash, money)
}

#[test]
fn traders_without_coins_pay_with_a_bill_that_the_bank_discounts() {
    let (s, purse, before, cash, money) = bill_scene(PLENTY);
    let bills = s.sim.world().get::<BillTable>();
    assert_eq!(s.carried(), 0, "all of it sold");
    assert_eq!(bills.count(), before + 1);
    assert_eq!(bills.face[before], 1_000);
    assert_eq!(bills.holder[before], BILL_HOLDER_BANK);
    assert!(
        s.agents().cash[s.a] > cash + 800,
        "the bank paid at a discount, less upkeep"
    );
    assert_eq!(
        s.sim.world().get::<PurseState>().float[purse],
        0,
        "the traders still owe it"
    );
    assert_eq!(s.money(), money);
}

#[test]
fn bank_without_room_leaves_the_bill_with_the_seller() {
    let (s, _, before, cash, _) = bill_scene(0);
    assert_eq!(s.carried(), 0);
    assert_eq!(s.sim.world().get::<BillTable>().holder[before], s.a as i32);
    assert!(
        s.agents().cash[s.a] < cash + 1,
        "nothing until it falls due"
    );
}

#[test]
fn price_cover_makes_up_a_sale_below_the_floor() {
    let extra = ["merchants.hedge_fee_permille,50"];
    let mut s = Scene::new(Options {
        extra: &extra,
        ..DEFAULT
    });
    s.load(100, 500);
    s.prices(10, 10);
    s.set_float(s.here, PLENTY);
    let (a, nation) = (s.a, s.purse());
    s.sim.context_mut().world.get_mut::<BankState>().reserve[nation] = 1_000_000;
    s.agents_mut().hedge_floor[a] = 1_500;
    let (cash, money) = (s.agents().cash[a], s.money());
    let reserve = s.sim.world().get::<BankState>().reserve[nation];
    s.run();
    assert_eq!(s.carried(), 0);
    assert_eq!(s.agents().hedge_floor[a], 0);
    assert_eq!(
        s.sim.world().get::<BankState>().reserve[nation],
        reserve - 500,
        "the bank pays the 500 short"
    );
    assert!(s.agents().cash[a] > cash + 1_400);
    assert_eq!(s.money(), money);
}

#[test]
fn traders_pay_the_seller_the_price_less_their_margin() {
    let mut s = Scene::new(Options {
        spread: 300,
        ..DEFAULT
    });
    s.load(100, 500);
    s.prices(10, 10);
    s.set_float(s.here, PLENTY);
    let (cash, money) = (s.agents().cash[s.a], s.money());
    s.run();
    assert_eq!(s.carried(), 0);
    let float = s.sim.world().get::<PurseState>().float[s.purse()];
    assert_eq!(float, PLENTY - 100 * 7, "seven of the ten");
    assert!(s.agents().cash[s.a] > cash + 600);
    assert!(s.agents().cash[s.a] < cash + 701);
    assert_eq!(s.money(), money);
}

#[test]
fn margin_counts_when_judging_a_market() {
    // The price here pays for the trip only before the margin.
    fn stage(s: &mut Scene) {
        let good = s.good;
        let markets = s.sim.context_mut().world.get_mut::<MarketStore>();
        for n in 0..markets.market_count() {
            if let Some(cell) = markets.try_find_cell(n, good) {
                markets.price[cell] = 19;
            }
        }
        s.prices(10, 19);
        let cell = s.cell(s.here);
        let w = &mut s.sim.context_mut().world;
        w.get_mut::<MarketStore>().stock[cell] = 100;
        w.get_mut::<PurseState>().float.fill(PLENTY);
    }
    let mut s = Scene::new(Options {
        spread: 500,
        ..DEFAULT
    });
    stage(&mut s);
    s.run();
    assert_eq!(s.carried(), 0, "ten coins for a unit that costs ten");
    let mut none = Scene::new(DEFAULT);
    stage(&mut none);
    none.run();
    assert!(none.carried() > 0);
}

#[test]
fn buying_a_load_enters_its_cost_in_the_books() {
    let mut s = Scene::new(DEFAULT);
    s.prices(10, 1_000);
    let cell = s.cell(s.here);
    s.sim.context_mut().world.get_mut::<MarketStore>().stock[cell] = 100;
    s.run();
    assert!(s.travelling());
    let stock = i64::from(s.sim.world().get::<MarketStore>().stock[cell]);
    assert_eq!(s.agents().load_cost[s.a], (100 - stock) * 10);
    assert!(s.agents().load_cost[s.a] > 0);
}

#[test]
fn households_buy_necessities_first_even_where_traders_have_no_coins() {
    let mut s = Scene::new(Options {
        good: "bread",
        ..DEFAULT
    });
    s.load(100, 500);
    s.prices(10, 10);
    s.set_float(s.here, 0);
    let (here, cell) = (s.here as usize, s.cell(s.here));
    s.sim.context_mut().world.get_mut::<HouseholdCash>().cash[here] = PLENTY;
    let stored = s.sim.world().get::<HouseholdCash>().home_grown[cell];
    let (cash, money) = (s.agents().cash[s.a], s.money());
    s.run();
    let upkeep = s.stats().spent_upkeep;
    let bought = i64::from(s.sim.world().get::<HouseholdCash>().home_grown[cell] - stored);
    assert!(bought > 0, "the households took some");
    assert_eq!(s.carried(), 100 - bought);
    assert_eq!(
        s.agents().cash[s.a],
        cash + bought * 10 - upkeep,
        "at the price, less upkeep"
    );
    assert_eq!(s.money(), money);
}

#[test]
fn what_households_do_not_need_goes_to_the_traders() {
    let mut s = Scene::new(Options {
        good: "bread",
        ..DEFAULT
    });
    s.load(100_000, 50_000);
    s.prices(10, 10);
    s.set_float(s.here, PLENTY);
    let (here, cell) = (s.here as usize, s.cell(s.here));
    s.sim.context_mut().world.get_mut::<HouseholdCash>().cash[here] = PLENTY;
    let stored = s.sim.world().get::<HouseholdCash>().home_grown[cell];
    s.run();
    let bought = s.sim.world().get::<HouseholdCash>().home_grown[cell] - stored;
    assert!(bought < 100_000, "a week's need, not the whole load");
    assert_eq!(s.carried(), 0, "the rest sold to the traders");
}

#[test]
fn buying_a_load_keeps_two_weeks_of_upkeep() {
    let mut s = Scene::new(DEFAULT);
    s.prices(10, 1_000);
    let (cell, a) = (s.cell(s.here), s.a);
    s.sim.context_mut().world.get_mut::<MarketStore>().stock[cell] = 100_000;
    s.agents_mut().cash[a] = 2_000;
    s.run();
    let upkeep = s.stats().spent_upkeep;
    assert!(upkeep > 0);
    assert!(s.agents().load_cost[a] > 0, "it bought");
    assert!(
        s.agents().cash[a] >= upkeep,
        "a week's upkeep is left after paying this week's"
    );
}

#[test]
fn off_sells_all_on_arrival() {
    let extra = ["merchants.weigh_sales,0"];
    let mut s = Scene::new(Options {
        extra: &extra,
        ..DEFAULT
    });
    s.load(100, 10_000);
    s.prices(10, 1_000);
    s.run();
    assert!(!s.travelling());
    assert_eq!(s.carried(), 0);
    assert_eq!(s.stats().carried_on, 0);
}
