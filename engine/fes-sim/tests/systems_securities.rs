//! Ventures, asset markets, finance records and short sales against the C# unit tests
//! (`VentureTests`, `AssetMarketTests`, `RentTests`, `FinanceTests`, `ShortTests`). Commands are not
//! ported yet, so holdings are set as the commands' `Apply` leaves them, and the systems run alone,
//! one turn per run, unless a test says otherwise.
mod common;

use common::build_sample;
use fes_core::Fx;
use fes_sim::agents::asset_markets::{AssetMarketKind, MarketBurst, MarketHeated, HEATED};
use fes_sim::agents::asset_prices::{
    land_price, share_fundamental, share_price, venture_price, Refs,
};
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::securities::{AssetMarkets, ShareTable, VentureTable};
use fes_sim::agents::ventures::venture_fundamental;
use fes_sim::agents::{BankState, FacilityTable, LandState};
use fes_sim::economy::HouseholdState;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{SimEvent, Simulation};
use std::sync::Arc;

const WEEKS_PER_YEAR: i32 = 52;

fn refs(sim: &Simulation) -> Refs<'_> {
    let services = &sim.context().services;
    Refs {
        db: &services.database,
        nodes: services.get::<NodeTable>(),
        banks: services.get::<NodeBanks>(),
    }
}

fn of<E: Clone + 'static>(events: &[Arc<dyn SimEvent>]) -> Vec<E> {
    events
        .iter()
        .filter_map(|e| (**e).as_any().downcast_ref::<E>().cloned())
        .collect()
}

fn step(sim: &mut Simulation) -> Vec<Arc<dyn SimEvent>> {
    sim.step().expect("step");
    sim.context().events.turn_log().to_vec()
}

fn markets(sim: &mut Simulation) -> &mut AssetMarkets {
    sim.context_mut().world.get_mut::<AssetMarkets>()
}

/// C# `HoldUntilQuarterEnd`: advances until a quarter has closed with the market's premium held
/// at `premium` the turn before; returns the events.
fn hold_until_quarter_end(sim: &mut Simulation, m: usize, premium: Fx) -> Vec<Arc<dyn SimEvent>> {
    let published = sim.world().get::<AssetMarkets>().published_premium[m];
    let mut events = Vec::new();
    for _ in 0..=13 {
        markets(sim).premium[m] = premium.raw();
        events.extend(step(sim));
        if sim.world().get::<AssetMarkets>().published_premium[m] != published {
            break;
        }
    }
    events
}

fn nation_of(sim: &Simulation, node: i32) -> i32 {
    let r = refs(sim);
    r.banks.nation_of(r.nodes, node)
}

/// C# `RentTests.PopulatedNode`.
fn populated_node(sim: &Simulation) -> usize {
    let population = &sim.world().get::<HouseholdState>().population;
    let nodes = sim.context().services.get::<NodeTable>();
    let markets = sim.world().get::<AssetMarkets>();
    (0..population.len())
        .find(|&n| {
            nation_of(sim, n as i32) >= 0 && population[n] > 0 && markets.households(nodes, n) == n
        })
        .expect("a populated node")
}

fn ventures(sim: &Simulation) -> &VentureTable {
    sim.world().get::<VentureTable>()
}

fn venture_nation(sim: &Simulation, v: usize) -> i32 {
    let services = &sim.context().services;
    let nodes = services.get::<NodeTable>();
    services
        .get::<NodeBanks>()
        .nation_of(nodes, ventures(sim).port[v])
}

#[test]
fn ventures_are_founded_at_ports_with_a_route_across_the_sea() {
    let sim = build_sample(&[]);
    let nodes = sim.context().services.get::<NodeTable>();
    let table = ventures(&sim);
    assert!(table.count() > 0);
    for v in 0..table.count() {
        let port = table.port[v] as usize;
        assert_eq!(table.continent[v], nodes.continent[port]);
        assert_ne!(
            nodes.continent[port],
            nodes.continent[table.far[v] as usize]
        );
        assert!(venture_nation(&sim, v) >= 0);
    }
}

#[test]
fn a_venture_raises_its_capital_sails_and_comes_home() {
    let mut sim = build_sample(&[]);
    let mut sailed = false;
    for _ in 0..3 * WEEKS_PER_YEAR {
        sim.step().expect("step");
        let turn = sim.context().turn();
        sailed |= ventures(&sim).at_sea(0, turn);
        if sailed && ventures(&sim).voyages[0] > 0 {
            break;
        }
    }
    assert!(sailed, "it fitted out a voyage");
    assert!(ventures(&sim).voyages[0] > 0, "and came home");
}

#[test]
fn a_wrecked_venture_comes_home_with_nothing_and_pays_nothing() {
    let mut sim = build_sample(&["venture.loss_permille,1000"]);
    while ventures(&sim).sail_until[0] < 0 {
        assert!(sim.context().turn() < 2 * WEEKS_PER_YEAR, "it never sailed");
        sim.step().expect("step");
    }
    assert_eq!(ventures(&sim).lost[0], 1);
    let nation = venture_nation(&sim, 0) as usize;
    let deposit = sim.world().get::<BankState>().player_deposit[nation];
    for _ in 0..WEEKS_PER_YEAR {
        sim.step().expect("step");
        if ventures(&sim).voyages[0] > 0 {
            break;
        }
    }
    let table = ventures(&sim);
    assert_eq!((table.voyages[0], table.wrecks[0]), (1, 1));
    assert!(table.profit_year[0] < 0, "the stake was lost");
    assert_eq!(
        sim.world().get::<BankState>().player_deposit[nation],
        deposit
    );
}

#[test]
fn a_share_is_the_ventures_value_over_its_shares_times_the_exchange_premium() {
    let mut sim = build_sample(&[]);
    let config = sim.context().services.database.config.clone();
    let world = &mut sim.context_mut().world;
    let table = world.get_mut::<VentureTable>();
    table.cash[0] = 10_000;
    table.stake[0] = 4_000;
    table.profit_year[0] = 3_000;
    let continent = table.continent[0];
    let fundamental = (10_000 + 4_000 + i64::from(config.venture_value_years) * 3_000)
        / i64::from(config.venture_shares);
    assert_eq!(
        venture_fundamental(world.get::<VentureTable>(), &config, 0),
        fundamental
    );
    let markets = world.get_mut::<AssetMarkets>();
    let m = markets.nation_count + markets.node_count + continent as usize;
    markets.premium[m] = Fx::from_int(3).raw();
    assert_eq!(venture_price(world, &config, 0), 3 * fundamental);
}

#[test]
fn a_share_price_is_its_fundamental_times_the_exchange_premium() {
    let mut sim = build_sample(&[]);
    let f = {
        let facilities = sim.world().get::<FacilityTable>();
        (0..facilities.count())
            .find(|&f| {
                facilities.owner[f] == OWNER_NPC
                    && facilities.group[f] >= 0
                    && nation_of(&sim, facilities.node[f]) >= 0
            })
            .expect("a group facility")
    };
    let nation = nation_of(&sim, sim.world().get::<FacilityTable>().node[f]) as usize;
    let fundamental = share_fundamental(sim.world(), refs(&sim), f);
    assert_eq!(share_price(sim.world(), refs(&sim), f), fundamental);
    markets(&mut sim).premium[nation] = Fx::from_int(2).raw();
    assert_eq!(share_price(sim.world(), refs(&sim), f), 2 * fundamental);
}

#[test]
fn a_land_price_is_its_fundamental_times_the_property_premium() {
    let mut sim = build_sample(&[]);
    let config = sim.context().services.database.config.clone();
    let n = (0..sim.world().get::<LandState>().plots.len())
        .find(|&n| nation_of(&sim, n as i32) >= 0)
        .expect("a node in a nation");
    let (m, fundamental) = {
        let nodes = sim.context().services.get::<NodeTable>();
        let land = sim.world().get::<LandState>();
        let households = sim.world().get::<HouseholdState>();
        let m = sim.world().get::<AssetMarkets>().property(nodes, n);
        (m, land.fundamental(nodes, households, &config, n))
    };
    markets(&mut sim).premium[m] = Fx::from_permille(1500).raw();
    let nodes = sim.context().services.get::<NodeTable>();
    assert_eq!(
        land_price(sim.world(), nodes, &config, n),
        Fx::from_permille(1500).mul_int(fundamental)
    );
}

#[test]
fn a_market_heats_above_the_first_line() {
    let mut sim = build_sample(&[]);
    let events = hold_until_quarter_end(&mut sim, 0, Fx::from_permille(1800));
    let heated = of::<MarketHeated>(&events);
    assert_eq!(heated.len(), 1);
    assert_eq!(heated[0].kind, AssetMarketKind::Stock);
    assert!(of::<MarketBurst>(&events).is_empty());
    assert_eq!(sim.world().get::<AssetMarkets>().phase[0], HEATED);
}

#[test]
fn the_players_plots_earn_rent_from_the_households() {
    let mut sim = build_sample(&[]);
    let config = sim.context().services.database.config.clone();
    let n = populated_node(&sim);
    let nation = nation_of(&sim, n as i32) as usize;
    let world = &mut sim.context_mut().world;
    world.get_mut::<LandState>().player_plots[n] = 3;
    let deposit = world.get::<BankState>().player_deposit[nation];
    let year = world
        .get::<LandState>()
        .year_rent(world.get::<HouseholdState>(), &config, n);
    let week = 3 * year / 52;
    assert!(week > 0);
    sim.step().expect("step");
    let interest = config.deposit_interest_per_year.mul_int(deposit) / 52;
    let now = sim.world().get::<BankState>().player_deposit[nation];
    assert!((now - (deposit + interest + week)).abs() <= 2, "{now}");
}

#[test]
fn on_a_dear_exchange_groups_list_their_shares() {
    let mut sim = build_sample(&[]);
    let outside = |sim: &Simulation| -> i32 {
        let shares = sim.world().get::<ShareTable>();
        (0..sim.world().get::<FacilityTable>().count())
            .map(|f| shares.outside(f as i32))
            .sum()
    };
    let before = outside(&sim);
    hold_until_quarter_end(&mut sim, 0, Fx::from_permille(1600));
    assert!(outside(&sim) > before);
}

/// C# `AFall_CallsTheHouseholdsMargin`, on land (no shares are listed before credit is ported):
/// a market all but worthless calls its debt down to the loan-to-value.
#[test]
fn a_fall_calls_the_households_margin() {
    let mut sim = build_sample(&[]);
    let n = populated_node(&sim);
    let nation = nation_of(&sim, n as i32) as usize;
    let m = {
        let nodes = sim.context().services.get::<NodeTable>();
        sim.world().get::<AssetMarkets>().property(nodes, n)
    };
    let debt = 1_000_000;
    markets(&mut sim).debt[m] = debt;
    markets(&mut sim).premium[m] = 1;
    let written_off = sim.world().get::<AssetMarkets>().written_off[nation];
    sim.step().expect("step");
    let markets = sim.world().get::<AssetMarkets>();
    assert!(markets.debt[m] < debt / 2, "{}", markets.debt[m]);
    assert!(markets.written_off[nation] >= written_off);
}
