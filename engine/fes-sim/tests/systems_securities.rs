//! Ventures, asset markets, finance records and short sales against the C# unit tests
//! (`VentureTests`, `AssetMarketTests`, `RentTests`, `FinanceTests`, `ShortTests`). Commands are not
//! ported yet, so holdings are set as the commands' `Apply` leaves them, and the systems run alone,
//! one turn per run, unless a test says otherwise.
mod common;

use common::build_sample;
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx, WorldLevel};
use fes_sim::agents::asset_markets::{AssetMarketKind, MarketBurst, MarketHeated, BUST, HEATED};
use fes_sim::agents::asset_prices::{
    land_price, share_fundamental, share_price, venture_price, Refs,
};
use fes_sim::agents::facilities::OWNER_NPC;
use fes_sim::agents::finance::event_kind::{BANK_RUN, CREDIT_CRUNCH, NONE, RATE_SPIKE};
use fes_sim::agents::finance::reserve_ratio;
use fes_sim::agents::finance_system::{
    loan_rate, FinancialEventEnded, FinancialEventStarted, StatisticsPublished,
};
use fes_sim::agents::securities::{AssetMarkets, ShareTable, VentureTable};
use fes_sim::agents::ventures::venture_fundamental;
use fes_sim::agents::{
    AgentTable, BankState, FacilityTable, FinanceState, GroupTable, LandState, MoneyTotals,
    NpcLoanTable, PlayerState, PurseState,
};
use fes_sim::economy::{HouseholdCash, HouseholdState};
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

const CITY: &str = "aur_n1_r1_c1";
/// C# `FinanceTests.BuildCalm`.
const CALM: &[&str] = &["road.upkeep,0", "finance.event_chance_permille,0"];

fn city(sim: &Simulation) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    nodes.try_find(CITY).expect(CITY).0
}

fn advance_to(sim: &mut Simulation, turn: i32) -> Vec<Arc<dyn SimEvent>> {
    let mut events = Vec::new();
    while sim.context().turn() < turn {
        events = step(sim);
    }
    events
}

fn city_facility(sim: &Simulation) -> usize {
    let facilities = sim.world().get::<FacilityTable>();
    (0..facilities.count())
        .find(|&f| facilities.node[f] == city(sim))
        .expect("a facility in the city")
}

fn recompute(sim: &mut Simulation) {
    let ctx = sim.context_mut();
    let mut totals = MoneyTotals::new(0, 0);
    std::mem::swap(&mut totals, ctx.services.get_mut::<MoneyTotals>());
    let nodes = ctx.services.get::<NodeTable>();
    totals.recompute(&ctx.world, nodes, ctx.services.get::<NodeBanks>());
    *ctx.services.get_mut::<MoneyTotals>() = totals;
}

/// Positive cash of every holder standing in `nation`, plus the player's `player` coins.
fn nation_cash(sim: &Simulation, nation: i32, player: i64) -> i64 {
    let in_nation = |node: i32| nation_of(sim, node) == nation;
    let world = sim.world();
    let facilities = world.get::<FacilityTable>();
    let agents = world.get::<AgentTable>();
    let groups = world.get::<GroupTable>();
    let households = world.get::<HouseholdCash>();
    let nodes = sim.context().services.get::<NodeTable>();
    let facility_cash: i64 = (0..facilities.count())
        .filter(|&f| in_nation(facilities.node[f]))
        .map(|f| facilities.cash[f].max(0))
        .sum();
    let agent_cash: i64 = (0..agents.count())
        .filter(|&a| in_nation(agents.node[a]))
        .map(|a| agents.cash[a].max(0))
        .sum();
    let group_cash: i64 = (0..groups.count())
        .filter(|&g| in_nation(groups.node[g]))
        .map(|g| groups.cash[g].max(0))
        .sum();
    let household_cash: i64 = (0..nodes.count())
        .filter(|&node| in_nation(node as i32))
        .map(|node| households.cash[node].max(0))
        .sum();
    let float = world.get::<PurseState>().float[nation as usize].max(0);
    player + facility_cash + agent_cash + group_cash + float + household_cash
}

#[test]
fn money_totals_count_cash_where_it_is_and_deposits_only_once() {
    let mut sim = build_sample(CALM);
    let at = city(&sim);
    let n = nation_of(&sim, at) as usize;
    let continent = sim.context().services.get::<NodeTable>().continent[at as usize] as usize;
    {
        let player = sim.context_mut().world.get_mut::<PlayerState>();
        player.coins.fill(0);
        player.node = at;
        player.coins[continent] = 1_000;
    }
    recompute(&mut sim);
    let cash = nation_cash(&sim, n as i32, 1_000);
    let totals = sim.context().services.get::<MoneyTotals>();
    assert_eq!(totals.nation_cash[n], cash);
    let total = totals.nation_total(n);
    assert_eq!(total, cash + sim.world().get::<BankState>().reserve[n]);

    // A deposit (as `BankTransferCommand` applies it) moves coins from cash into the reserve.
    let world = &mut sim.context_mut().world;
    world.get_mut::<PlayerState>().coins[continent] -= 400;
    let bank = world.get_mut::<BankState>();
    bank.player_deposit[n] += 400;
    bank.reserve[n] += 400;
    recompute(&mut sim);
    let totals = sim.context().services.get::<MoneyTotals>();
    let bank = sim.world().get::<BankState>();
    assert_eq!(totals.nation_deposits[n], 400);
    assert_eq!(totals.nation_reserve[n], bank.reserve[n]);
    assert_eq!(totals.nation_total(n), total);
    let r = refs(&sim);
    let mut expected = bank.international_reserve[continent] + totals.continent_loose[continent];
    for k in 0..bank.reserve.len() {
        let node = r.banks.nation_node(r.nodes, k as i32);
        if r.nodes.continent[node as usize] == continent as i32 {
            expected += totals.nation_total(k);
        }
    }
    assert_eq!(totals.continent_total[continent], expected);
}

#[test]
fn records_are_taken_each_quarter_and_statistics_published_each_year() {
    let mut sim = build_sample(CALM);
    let n = nation_of(&sim, city(&sim)) as usize;
    advance_to(&mut sim, WEEKS_PER_YEAR);
    let finance = sim.world().get::<FinanceState>();
    let turns: Vec<i32> = finance.records.iter().map(|r| r.turn).collect();
    assert_eq!(turns, [12, 25, 38, 51]);
    assert_eq!(finance.published_year, -1);

    let events = step(&mut sim); // turn 52, the new year
    let finance = sim.world().get::<FinanceState>();
    assert_eq!(finance.published_year, 0);
    assert_eq!(of::<StatisticsPublished>(&events).len(), 1);
    let last = finance.latest().expect("a record");
    assert_eq!(finance.published_nation_total[n], last.nation_total[n]);
    assert_eq!(
        finance.published_reserve_ratio[n],
        last.reserve_ratio(n).raw()
    );
    let index = Fx::from_raw(last.price_index[0]);
    assert_eq!(finance.published_inflation[0], (index - Fx::ONE).raw());

    let kept = sim
        .context()
        .services
        .database
        .config
        .finance_history_quarters;
    advance_to(&mut sim, (kept / 4 + 1) * WEEKS_PER_YEAR);
    assert_eq!(
        sim.world().get::<FinanceState>().records.len(),
        kept as usize
    );
}

#[test]
fn loan_rate_rises_as_the_reserve_is_lent_out() {
    let sim = build_sample(&[]);
    let config = &sim.context().services.database.config;
    let target = config.target_reserve_ratio;
    let base = config.loan_interest_per_year;
    assert_eq!(loan_rate(config, Fx::ONE, NONE), base);
    assert_eq!(loan_rate(config, target, NONE), base);
    let half = Fx::from_raw(target.raw() / 2);
    assert_eq!(loan_rate(config, half, NONE), base * Fx::from_int(2));
    let most = base * config.max_loan_rate_multiplier;
    assert_eq!(loan_rate(config, Fx::ZERO, NONE), most);
    let spiked = base * config.rate_spike_multiplier;
    assert_eq!(loan_rate(config, Fx::ONE, RATE_SPIKE), spiked);
    // DEC-134: a crunch lifts rates too (C# `RentTests.ACrunch_LiftsLoanRates`).
    assert_eq!(loan_rate(config, Fx::ONE, CREDIT_CRUNCH), spiked);
    assert_eq!(reserve_ratio(0, 0), Fx::ONE);
    assert_eq!(reserve_ratio(100, 300), Fx::from_permille(250));
}

#[test]
fn a_bank_that_lent_most_charges_more_from_the_next_quarter() {
    let mut sim = build_sample(&[
        "road.upkeep,0",
        "finance.event_chance_permille,0",
        "bank.npc_max_reserve_share_permille,0",
    ]);
    let n = nation_of(&sim, city(&sim)) as usize;
    let f = city_facility(&sim) as i32;
    let world = &mut sim.context_mut().world;
    let reserve = world.get::<BankState>().reserve[n];
    world
        .get_mut::<NpcLoanTable>()
        .add(f, n as i32, 3 * reserve, 1);
    let config = sim.context().services.database.config.clone();
    let base = config.loan_interest_per_year.raw();
    assert_eq!(sim.world().get::<FinanceState>().loan_rate[n], base);
    advance_to(&mut sim, 13);
    let finance = sim.world().get::<FinanceState>();
    let ratio = finance.latest().expect("a record").reserve_ratio(n);
    assert!(ratio < config.target_reserve_ratio);
    assert_eq!(finance.loan_rate[n], loan_rate(&config, ratio, NONE).raw());
    assert!(finance.loan_rate[n] > base);
}

/// The sample data draws no random events (`finance.event_chance_permille = 0`); this covers the
/// dead branch: every nation draws an event at the first quarter's end, of the kind the stream
/// gives, and events end once the quarter has passed and reserves are fine.
#[test]
fn financial_events_start_at_random_and_end_once_reserves_recover() {
    let mut sim = build_sample(&[
        "road.upkeep,0",
        "finance.event_chance_permille,1000",
        "bank.npc_max_reserve_share_permille,0",
    ]);
    let nations = sim.world().get::<FinanceState>().nation_count;
    let reserves = sim.world().get::<BankState>().reserve.clone();
    let config = sim.context().services.database.config.clone();
    let seed = sim.context().seed();
    let events = advance_to(&mut sim, 13);
    let started = of::<FinancialEventStarted>(&events);
    assert_eq!(started.len(), nations);
    let stream = det_hash::of("agents.finance.events");
    let finance = sim.world().get::<FinanceState>();
    for (k, e) in started.iter().enumerate() {
        // Hand-computed from the stream: the chance roll, then the kind.
        let mut rng = DetRandom::for_key(seed, 12, stream, k as i32, 0);
        assert!(rng.chance_permille(1000));
        let kind = (1 + rng.next_int(3)) as u8;
        assert_eq!(e.kind, kind, "nation {k}");
        assert_eq!(finance.event_of(k), kind);
        assert_eq!(finance.event_start[k], 12);
        if kind == BANK_RUN {
            assert!(sim.world().get::<BankState>().reserve[k] < reserves[k]);
        }
        let lifted = kind == RATE_SPIKE || (kind == CREDIT_CRUNCH && config.crunch_lifts_rates);
        let rate = if lifted {
            config.loan_interest_per_year * config.rate_spike_multiplier
        } else {
            config.loan_interest_per_year
        };
        assert_eq!(finance.loan_rate[k], rate.raw());
    }

    // One nation's bank has lent nearly everything, so its event outlasts the quarter.
    let n = nation_of(&sim, city(&sim)) as usize;
    let f = city_facility(&sim);
    let world = &mut sim.context_mut().world;
    let reserve = world.get::<BankState>().reserve[n];
    world
        .get_mut::<NpcLoanTable>()
        .add(f as i32, n as i32, 100 * reserve, 1);
    world.get_mut::<FacilityTable>().cash[f] = 1_000_000; // keeps paying, so no default
    let events = advance_to(&mut sim, 26);
    let ended = of::<FinancialEventEnded>(&events);
    let city_nation = {
        let nodes = sim.context().services.get::<NodeTable>();
        nodes.ancestor_at(city(&sim), WorldLevel::Nation)
    };
    assert_eq!(ended.len(), nations - 1);
    assert!(ended.iter().all(|e| e.nation != city_nation));
    assert_ne!(sim.world().get::<FinanceState>().event_of(n), NONE);
}

/// C# `AMarket_BurstsAboveTheSecondLine_AndItsNationsCreditStops`.
#[test]
fn a_market_bursts_above_the_second_line_and_its_nations_credit_stops() {
    let mut sim = build_sample(&[]);
    let events = hold_until_quarter_end(&mut sim, 0, Fx::from_int(3));
    let burst = of::<MarketBurst>(&events);
    assert_eq!(burst.len(), 1);
    let nation_node = {
        let r = refs(&sim);
        r.banks.nation_node(r.nodes, 0)
    };
    assert_eq!(burst[0].node, nation_node);
    let markets = sim.world().get::<AssetMarkets>();
    assert_eq!(markets.phase[0], BUST);
    assert!(
        markets.premium[0] < Fx::from_int(3).raw(),
        "the price falls at once"
    );
    assert!(markets.expectation[0] < 0, "and buyers expect it to fall");
    let started = of::<FinancialEventStarted>(&events);
    let crunch = started.iter().find(|e| e.nation == nation_node);
    assert_eq!(crunch.expect("an event").kind, CREDIT_CRUNCH);
    assert_eq!(sim.world().get::<FinanceState>().event_of(0), CREDIT_CRUNCH);
}

/// C# `ABurst_OnAPropertyMarket_StopsItsNationsCredit`.
#[test]
fn a_burst_on_a_property_market_stops_its_nations_credit() {
    let mut sim = build_sample(&[]);
    let (n, m) = {
        let nodes = sim.context().services.get::<NodeTable>();
        let markets = sim.world().get::<AssetMarkets>();
        let n = (0..sim.world().get::<LandState>().plots.len())
            .find(|&x| nation_of(&sim, x as i32) >= 0 && markets.households(nodes, x) == x)
            .expect("a node");
        (n, markets.property(nodes, n))
    };
    let events = hold_until_quarter_end(&mut sim, m, Fx::from_int(3));
    let burst = of::<MarketBurst>(&events);
    assert_eq!(burst.len(), 1);
    assert_eq!(burst[0].kind, AssetMarketKind::Property);
    let nation = nation_of(&sim, n as i32) as usize;
    let event = sim.world().get::<FinanceState>().event_of(nation);
    assert_eq!(event, CREDIT_CRUNCH);
}

/// C# `VentureTests.AContinentalBurst_StopsCreditInEveryNationOfTheContinent`: the unsaved
/// `AssetMarkets.burst_nation`, written by asset_markets at the quarter's end, read by finance.
#[test]
fn a_continental_burst_stops_credit_in_every_nation_of_the_continent() {
    let mut sim = build_sample(&[]);
    let continent = ventures(&sim).continent[0];
    let m = sim.world().get::<AssetMarkets>().venture(continent);
    let events = hold_until_quarter_end(&mut sim, m, Fx::from_int(3));
    let burst = of::<MarketBurst>(&events);
    assert_eq!(burst.len(), 1);
    assert_eq!(burst[0].kind, AssetMarketKind::Venture);
    let r = refs(&sim);
    let finance = sim.world().get::<FinanceState>();
    let markets = sim.world().get::<AssetMarkets>();
    let mut on_continent = 0;
    for k in 0..finance.nation_count {
        let node = r.banks.nation_node(r.nodes, k as i32);
        let here = r.nodes.continent[node as usize] == continent;
        assert_eq!(markets.burst_nation[k], here, "nation {k}");
        if here {
            on_continent += 1;
            assert_eq!(finance.event_of(k), CREDIT_CRUNCH, "nation {k}");
        }
    }
    assert!(on_continent > 0);
}
