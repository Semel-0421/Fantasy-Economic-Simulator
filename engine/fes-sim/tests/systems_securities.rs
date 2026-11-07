//! Ventures, asset markets, finance records and short sales against the C# unit tests
//! (`VentureTests`, `AssetMarketTests`, `RentTests`, `FinanceTests`, `ShortTests`). Commands are not
//! ported yet, so holdings are set as the commands' `Apply` leaves them, and the systems run alone,
//! one turn per run, unless a test says otherwise.
mod common;

use common::build_sample;
use fes_core::Fx;
use fes_sim::agents::asset_prices::venture_price;
use fes_sim::agents::securities::{AssetMarkets, VentureTable};
use fes_sim::agents::ventures::venture_fundamental;
use fes_sim::agents::BankState;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::Simulation;

const WEEKS_PER_YEAR: i32 = 52;

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
