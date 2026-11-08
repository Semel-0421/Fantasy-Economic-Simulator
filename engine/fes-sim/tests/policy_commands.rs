//! The command cases of C# `PolicyTests`: lobbying in the company's name, heavy lobbying tipping
//! the review (and the licence it makes necessary), and licence prices following the step.
mod common;

use common::sample_with_config;
use fes_core::{GuidanceAxis, QualityGrade, WorldLevel};
use fes_sim::agents::licenses::LicenseTable;
use fes_sim::agents::policy::PolicyState;
use fes_sim::agents::policy_system::scale;
use fes_sim::agents::policy_system::{POLICY_LICENSED_GOODS, POLICY_TOLLS};
use fes_sim::agents::{BankState, PlayerState};
use fes_sim::commands::facility::FoundCompany;
use fes_sim::commands::licenses::{license_price, BuyLicense, Lobby};
use fes_sim::commands::trade::player_may_sell;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{Command, Simulation, SimulationBuilder, TurnReport};

const CITY: &str = "aur_n1_r1_c1";
const OTHER_NATION_CITY: &str = "bor_n1_r1_c1";
const CONFIG: &str = "key,value\nfinance.event_chance_permille,0\nmarket.credit_share_permille,0\nmarket.momentum_permille,0\n";

fn build() -> Simulation {
    SimulationBuilder::from_data(&sample_with_config(CONFIG), 42)
        .expect("builder")
        .build()
        .expect("build")
}

fn node(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .get::<NodeTable>()
        .try_find(key)
        .expect(key)
        .0
}

/// The nation's node and its index.
fn nation_of(sim: &Simulation, key: &str) -> (i32, usize) {
    let nodes = sim.context().services.get::<NodeTable>();
    let banks = sim.context().services.get::<NodeBanks>();
    let n = nodes.ancestor_at(node(sim, key), WorldLevel::Nation);
    (n, banks.nation_index(nodes, n) as usize)
}

fn place_at(sim: &mut Simulation, key: &str, coins: i64) {
    let at = node(sim, key);
    let continent = sim.context().services.get::<NodeTable>().continent[at as usize] as usize;
    let player = sim.context_mut().world.get_mut::<PlayerState>();
    player.coins.fill(0);
    player.node = at;
    player.coins[continent] = coins;
}

fn local_coins(sim: &Simulation) -> i64 {
    let p = sim.world().get::<PlayerState>();
    let c = sim.context().services.get::<NodeTable>().continent[p.node as usize] as usize;
    p.coins[c]
}

fn turn(sim: &mut Simulation, cmds: Vec<Box<dyn Command>>) -> TurnReport {
    for c in cmds {
        sim.queue(c);
    }
    sim.step().expect("step")
}

fn count(report: &TurnReport, name: &str) -> usize {
    report.events.iter().filter(|e| e.name() == name).count()
}

fn lobby(nation: i32, policy: u8, direction: i32, amount: i64, base_good: i32) -> Box<dyn Command> {
    Box::new(Lobby {
        nation,
        policy,
        direction,
        amount,
        base_good,
    })
}

fn found_company(sim: &Simulation) -> Box<dyn Command> {
    Box::new(FoundCompany {
        headquarters: node(sim, CITY),
        focus: GuidanceAxis::Trade as u8,
        cost: 0,
    })
}

fn to_first_review(sim: &mut Simulation) {
    while sim.turn() <= 52 {
        sim.step().expect("step");
    }
}

#[test]
fn lobbying_is_done_in_the_companys_name_inside_the_nation() {
    let mut sim = build();
    let (nation, index) = nation_of(&sim, CITY);
    place_at(&mut sim, CITY, 10_000);
    let report = turn(&mut sim, vec![lobby(nation, POLICY_TOLLS, 1, 100, -1)]);
    assert_eq!(1, count(&report, "CommandRejected"), "no company yet");

    let found = found_company(&sim);
    turn(&mut sim, vec![found]);
    let other = nation_of(&sim, OTHER_NATION_CITY).0;
    let report = turn(
        &mut sim,
        vec![
            lobby(nation, POLICY_TOLLS, 1, 100, 0), // a good only for licensing
            lobby(nation, POLICY_LICENSED_GOODS, 1, 100, -1), // licensing needs a good
            lobby(other, POLICY_TOLLS, 1, 100, -1), // another nation
            lobby(nation, POLICY_TOLLS, 2, 100, -1),
        ],
    );
    assert_eq!(4, count(&report, "CommandRejected"));

    let reserve = sim.world().get::<BankState>().reserve[index];
    let report = turn(&mut sim, vec![lobby(nation, POLICY_TOLLS, 1, 400, -1)]);
    assert_eq!(0, count(&report, "CommandRejected"));
    assert_eq!(1, count(&report, "Lobbied"));
    assert_eq!(10_000 - 400, local_coins(&sim));
    // The week's NPC loans aside.
    assert!(sim.world().get::<BankState>().reserve[index] >= reserve + 400 - 1_000);
    assert_eq!(1, sim.world().get::<PolicyState>().lobbying.len());
}

#[test]
fn heavy_lobbying_tips_the_review() {
    let mut calm = build();
    let mut lobbied = build();
    let (nation, index) = nation_of(&lobbied, CITY);
    place_at(&mut lobbied, CITY, 100_000_000);
    let found = found_company(&lobbied);
    turn(&mut lobbied, vec![found]);
    calm.step().expect("step");
    let wheat = lobbied
        .context()
        .services
        .database
        .try_get_good("wheat", QualityGrade::Mid)
        .expect("wheat");
    for cmd in [
        lobby(nation, POLICY_TOLLS, 1, 10_000_000, -1),
        lobby(
            nation,
            POLICY_LICENSED_GOODS,
            1,
            10_000_000,
            wheat.base_index(),
        ),
    ] {
        lobbied.queue(cmd);
    }
    to_first_review(&mut calm);
    to_first_review(&mut lobbied);

    let policy = lobbied.world().get::<PolicyState>();
    assert_eq!(1, policy.toll_step[index]);
    assert!(calm.world().get::<PolicyState>().toll_step[index] < 1);
    let licensed = |sim: &Simulation, n: usize| {
        let p = sim.world().get::<PolicyState>();
        p.licensed[n * p.base_good_count + wheat.base_index() as usize] != 0
    };
    assert!(licensed(&lobbied, index));
    assert!(policy.lobbying.is_empty(), "spent for the year");

    // Wheat now needs a license in that nation only; the player can buy one there.
    place_at(&mut lobbied, CITY, 10_000);
    let may_sell = |sim: &Simulation| {
        player_may_sell(
            sim.context(),
            wheat.0,
            sim.world().get::<PlayerState>().node,
        )
    };
    assert!(!may_sell(&lobbied));
    place_at(&mut lobbied, OTHER_NATION_CITY, 10_000);
    let other = nation_of(&lobbied, OTHER_NATION_CITY).1;
    assert_eq!(licensed(&lobbied, other), !may_sell(&lobbied));
    place_at(&mut lobbied, CITY, 10_000);
    let report = turn(&mut lobbied, vec![Box::new(BuyLicense { good: wheat.0 })]);
    assert_eq!(0, count(&report, "CommandRejected"));
    assert!(may_sell(&lobbied));
    assert_eq!(1, lobbied.world().get::<LicenseTable>().count());
}

#[test]
fn license_prices_follow_the_nations_step() {
    let mut sim = build();
    let tools = sim
        .context()
        .services
        .database
        .try_get_good("iron_tools", QualityGrade::Mid)
        .expect("iron_tools");
    place_at(&mut sim, CITY, 0);
    let (_, index) = nation_of(&sim, CITY);
    let price = |sim: &Simulation| {
        let ctx = sim.context();
        fes_sim::commands::licenses::license_player_price(
            &ctx.world,
            &ctx.services.database,
            ctx.services.get::<NodeTable>(),
            ctx.services.get::<NodeBanks>(),
            tools.base_index(),
            nation_of(sim, CITY).0,
            ctx.turn(),
        )
    };
    let before = price(&sim);
    sim.context_mut()
        .world
        .get_mut::<PolicyState>()
        .license_price_step[index] = 2;
    let config = &sim.context().services.database.config;
    let base = license_price(&sim.context().services.database, tools.base_index());
    assert_eq!(scale(config, 0).mul_int(base), before);
    assert_eq!(scale(config, 2).mul_int(base), price(&sim));
}
