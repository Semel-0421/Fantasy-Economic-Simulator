//! The command cases of C# `MoneyTests`: player actions conserve money, selling is capped by the
//! traders' float, the owner's deposit and withdrawal against the income tax, and exchange needs
//! coins at the international bank.
mod common;

use common::{assert_conserved, build_sample};
use fes_core::{GuidanceAxis, QualityGrade};
use fes_sim::agents::asset_prices::{asking_price, Refs};
use fes_sim::agents::cargo::PLAYER_CART;
use fes_sim::agents::facilities::{FacilityTable, OWNER_PLAYER};
use fes_sim::agents::groups::{GroupTable, KIND_GUILD};
use fes_sim::agents::income_tax::IncomeTaxSystem;
use fes_sim::agents::money_ledger::pay_households;
use fes_sim::agents::policy_system::POLICY_TOLLS;
use fes_sim::agents::production::bankruptcy::restart_facility;
use fes_sim::agents::{BankState, CargoTable, IncomeTaxState, PlayerState, PurseState};
use fes_sim::commands::bank::{BankTransfer, Exchange, TakeLoan};
use fes_sim::commands::facility::{BuyFacility, FoundCompany, MoveFacilityCash};
use fes_sim::commands::licenses::{BuyLicense, Lobby};
use fes_sim::commands::places::{BuildRoad, JoinGuild};
use fes_sim::commands::trade::{BuyGoods, SellGoods};
use fes_sim::commands::travel::Travel;
use fes_sim::economy::MarketStore;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{Command, CommandResult, Simulation, System, TurnReport};

const CITY: &str = "aur_n1_r1_c1";

fn node(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .get::<NodeTable>()
        .try_find(key)
        .expect(key)
        .0
}

fn good(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .database
        .try_get_good(key, QualityGrade::Mid)
        .expect(key)
        .0
}

/// Puts the player at `node` with only these local coins; households absorb the difference.
fn set_coins(sim: &mut Simulation, at: i32, coins: i64) {
    let nodes = sim.context().services.get::<NodeTable>().clone();
    let continents = sim.world().get::<PlayerState>().coins.len();
    for c in 0..continents {
        let any = (0..nodes.count() as i32)
            .find(|&n| nodes.continent[n as usize] as usize == c)
            .expect("a node");
        let held = sim.world().get::<PlayerState>().coins[c];
        let world = &mut sim.context_mut().world;
        pay_households(world, &nodes, any, held);
        world.get_mut::<PlayerState>().coins[c] = 0;
    }
    let local = nodes.continent[at as usize] as usize;
    let world = &mut sim.context_mut().world;
    world.get_mut::<PlayerState>().node = at;
    world.get_mut::<PlayerState>().coins[local] = coins;
    pay_households(world, &nodes, at, -coins);
}

fn local_continent(sim: &Simulation) -> usize {
    let at = sim.world().get::<PlayerState>().node;
    sim.context().services.get::<NodeTable>().continent[at as usize] as usize
}

fn local_coins(sim: &Simulation) -> i64 {
    sim.world().get::<PlayerState>().coins[local_continent(sim)]
}

fn float_of(sim: &Simulation, at: i32) -> usize {
    sim.world().get::<PurseState>().node_float[at as usize] as usize
}

/// Moves a float's money to the households of the same nation, so nothing is created.
fn empty_float(sim: &mut Simulation, at: i32) {
    let nodes = sim.context().services.get::<NodeTable>().clone();
    let p = float_of(sim, at);
    let held = sim.world().get::<PurseState>().float[p];
    let world = &mut sim.context_mut().world;
    pay_households(world, &nodes, at, held);
    world.get_mut::<PurseState>().float[p] = 0;
}

fn rejections(report: &TurnReport) -> Vec<String> {
    report
        .events
        .iter()
        .filter(|e| e.type_name.ends_with("CommandRejected"))
        .map(|e| e.debug.clone())
        .collect()
}

/// A turn with these commands; nothing may be rejected and the coins stay conserved.
fn step(sim: &mut Simulation, what: &str, cmds: Vec<Box<dyn Command>>) {
    for c in cmds {
        sim.queue(c);
    }
    let report = sim.step().expect("step");
    assert!(
        rejections(&report).is_empty(),
        "{what}: {:?}",
        rejections(&report)
    );
    assert_conserved(sim, what);
}

#[test]
fn player_actions_conserve_money() {
    let mut sim = build_sample(&[]);
    let city = node(&sim, CITY);
    set_coins(&mut sim, city, 50_000);
    assert_conserved(&sim, "setup");
    let tools = good(&sim, "iron_tools");
    let wheat = good(&sim, "wheat");
    let facility = {
        let t = sim.world().get::<FacilityTable>();
        (0..t.count())
            .find(|&f| t.node[f] == city)
            .expect("facility")
    };
    // Goods, not money: stock put on the shelf changes no continent's money.
    let cell = sim
        .world()
        .get::<MarketStore>()
        .try_find_cell(city as usize, wheat)
        .expect("cell");
    sim.context_mut().world.get_mut::<MarketStore>().stock[cell] += 100;
    step(
        &mut sim,
        "deposit and buy",
        vec![
            Box::new(BankTransfer { amount: 5_000 }),
            Box::new(BuyGoods {
                good: wheat,
                quantity: 1,
                pay_from_deposit: false,
            }),
        ],
    );
    step(
        &mut sim,
        "sell, license, company",
        vec![
            Box::new(SellGoods {
                good: wheat,
                quantity: 1,
            }),
            Box::new(BuyLicense { good: tools }),
            Box::new(FoundCompany {
                headquarters: city,
                focus: GuidanceAxis::Trade as u8,
                cost: 1_000,
            }),
        ],
    );
    facility_and_policy(&mut sim, city, facility);
    travel_and_road(&mut sim);
}

fn facility_and_policy(sim: &mut Simulation, city: i32, facility: usize) {
    let price = {
        let ctx = sim.context();
        let r = Refs {
            db: &ctx.services.database,
            nodes: ctx.services.get::<NodeTable>(),
            banks: ctx.services.get::<NodeBanks>(),
        };
        asking_price(&ctx.world, r, facility)
    };
    let guild = {
        let g = sim.world().get::<GroupTable>();
        (0..g.count())
            .find(|&i| g.kind[i] == KIND_GUILD && g.node[i] == city)
            .expect("a guild") as i32
    };
    step(
        sim,
        "facility and guild",
        vec![
            Box::new(BuyFacility {
                facility: facility as i32,
                price,
                axis: GuidanceAxis::Industry as u8,
            }),
            Box::new(JoinGuild { guild }),
        ],
    );
    let nation = {
        let nodes = sim.context().services.get::<NodeTable>();
        nodes.ancestor_at(city, fes_core::WorldLevel::Nation)
    };
    step(
        sim,
        "loan and lobbying",
        vec![
            Box::new(TakeLoan { amount: 500 }),
            Box::new(Lobby {
                nation,
                policy: POLICY_TOLLS,
                direction: 1,
                amount: 300,
                base_good: -1,
            }),
        ],
    );
}

fn travel_and_road(sim: &mut Simulation) {
    let home = local_continent(sim) as i32;
    step(
        sim,
        "exchange",
        vec![Box::new(Exchange {
            from_continent: home,
            to_continent: 1 - home,
            amount: 1_000,
        })],
    );
    let village = node(sim, "aur_n1_r1_c1_v1");
    step(
        sim,
        "road",
        vec![Box::new(BuildRoad {
            destination: village,
        })],
    );
    let there = node(sim, "aur_n1_r1_c2");
    step(
        sim,
        "travel",
        vec![Box::new(Travel {
            destination: there,
            guards: true,
            insurance: true,
        })],
    );
    for t in 0..12 {
        step(sim, &format!("after, week {t}"), vec![]);
    }
}

#[test]
fn selling_is_capped_by_the_traders_float() {
    let mut sim = build_sample(&[]);
    std::sync::Arc::make_mut(&mut sim.context_mut().services.database)
        .config
        .trader_spread = fes_core::Fx::ZERO;
    let city = node(&sim, CITY);
    set_coins(&mut sim, city, 0);
    let wheat = good(&sim, "wheat");
    sim.context_mut()
        .world
        .get_mut::<CargoTable>()
        .change(PLAYER_CART, wheat, 10);
    empty_float(&mut sim, city);
    sim.queue(Box::new(SellGoods {
        good: wheat,
        quantity: 10,
    }));
    let report = sim.step().expect("step");
    let rejected = rejections(&report);
    assert_eq!(1, rejected.len());
    assert!(rejected[0].contains("no money"), "{rejected:?}");
    let cart = |sim: &Simulation| {
        sim.world()
            .get::<CargoTable>()
            .quantity_of(PLAYER_CART, wheat)
    };
    assert_eq!(10, cart(&sim));

    // With coins for three units the traders buy three.
    let cell = sim
        .world()
        .get::<MarketStore>()
        .try_find_cell(city as usize, wheat)
        .expect("cell");
    empty_float(&mut sim, city);
    let price = i64::from(sim.world().get::<MarketStore>().price[cell]);
    let p = float_of(&sim, city);
    let nodes = sim.context().services.get::<NodeTable>().clone();
    let world = &mut sim.context_mut().world;
    world.get_mut::<PurseState>().float[p] = 3 * price;
    pay_households(world, &nodes, city, -3 * price);
    let before = local_coins(&sim);
    SellGoods {
        good: wheat,
        quantity: 10,
    }
    .apply(sim.context_mut());
    assert_eq!(7, cart(&sim));
    assert_eq!(3 * price, local_coins(&sim) - before);
    assert_eq!(0, sim.world().get::<PurseState>().float[p]);
}

/// Steps to a new year and gives the player a facility at the city.
fn own_facility_at_the_city(sim: &mut Simulation) -> usize {
    sim.step().expect("step");
    while !sim.context().clock().is_new_year() {
        sim.step().expect("step");
    }
    let city = node(sim, CITY);
    let t = sim.context_mut().world.get_mut::<FacilityTable>();
    let f = (0..t.count())
        .find(|&i| t.node[i] == city)
        .expect("facility");
    t.owner[f] = OWNER_PLAYER;
    f
}

#[test]
fn income_tax_does_not_count_the_owners_deposit() {
    let mut sim = build_sample(&[]);
    let f = own_facility_at_the_city(&mut sim);
    let city = node(&sim, CITY);
    set_coins(&mut sim, city, 50_000);
    let world = &mut sim.context_mut().world;
    restart_facility(world.get_mut::<IncomeTaxState>(), f, 1_000);
    world.get_mut::<FacilityTable>().cash[f] = 1_000;
    let deposit = MoveFacilityCash {
        facility: f as i32,
        amount: 10_000,
    };
    assert_eq!(CommandResult::Ok, deposit.validate(sim.context()));
    deposit.apply(sim.context_mut());
    IncomeTaxSystem
        .execute(sim.context_mut())
        .expect("income tax");
    assert_eq!(11_000, sim.world().get::<FacilityTable>().cash[f]);
}

#[test]
fn withdrawal_leaves_the_tax_on_the_years_profit() {
    let mut sim = build_sample(&[]);
    let f = own_facility_at_the_city(&mut sim);
    let city = node(&sim, CITY);
    set_coins(&mut sim, city, 0);
    let world = &mut sim.context_mut().world;
    restart_facility(world.get_mut::<IncomeTaxState>(), f, 0);
    world.get_mut::<FacilityTable>().cash[f] = 10_000; // a profit of 10,000, so 1,000 owed
    let too_much = MoveFacilityCash {
        facility: f as i32,
        amount: -10_000,
    };
    assert_ne!(CommandResult::Ok, too_much.validate(sim.context()));
    let withdrawal = MoveFacilityCash {
        facility: f as i32,
        amount: -9_000,
    };
    assert_eq!(CommandResult::Ok, withdrawal.validate(sim.context()));
    withdrawal.apply(sim.context_mut());
    IncomeTaxSystem
        .execute(sim.context_mut())
        .expect("income tax");
    assert_eq!(
        0,
        sim.world().get::<FacilityTable>().cash[f],
        "the whole tax was paid"
    );
}

#[test]
fn exchange_needs_coins_at_the_international_banks() {
    let mut sim = build_sample(&[]);
    let city = node(&sim, CITY);
    set_coins(&mut sim, city, 1_000);
    let home = local_continent(&sim);
    let other = 1 - home;
    sim.context_mut()
        .world
        .get_mut::<BankState>()
        .international_reserve[other] = 0;
    sim.queue(Box::new(Exchange {
        from_continent: home as i32,
        to_continent: other as i32,
        amount: 1_000,
    }));
    let report = sim.step().expect("step");
    assert_eq!(1, rejections(&report).len());
}
