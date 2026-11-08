//! C# `TraderMarginTests`: the traders' margin, idle coins and far flows paying their carriers.
mod common;

use common::{build, continent_count, continent_money, sample_config};
use fes_core::{QualityGrade, WorldLevel};
use fes_sim::agents::cargo::PLAYER_CART;
use fes_sim::agents::facilities::FacilityTable;
use fes_sim::agents::player::PlayerState;
use fes_sim::agents::trader_loans::TraderLoanSystem;
use fes_sim::agents::{CargoTable, MerchantSystem, PurseState, TraderLoans};
use fes_sim::commands::trade::SellGoods;
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::world::{NodeTable, RouteTable};
use fes_sim::{Command, Simulation, System};

fn money(sim: &Simulation) -> i64 {
    (0..continent_count(sim))
        .map(|c| continent_money(sim, c))
        .sum()
}

fn good(sim: &Simulation, key: &str) -> i32 {
    sim.context()
        .services
        .database
        .try_get_good(key, QualityGrade::Mid)
        .expect("good")
        .0
}

#[test]
fn the_player_sells_at_the_traders_offer() {
    let mut sim = build(&sample_config(&["markets.trader_spread_permille,300"]), 42);
    let wheat = good(&sim, "wheat");
    let node = sim.world().get::<PlayerState>().node;
    let continent = sim.context().services.get::<NodeTable>().continent[node as usize] as usize;
    let cell = {
        let ctx = sim.context();
        fes_sim::agents::production::lines::find_nearest_market(
            ctx.services.get::<NodeTable>(),
            ctx.world.get::<MarketStore>(),
            node,
            wheat,
        )
        .expect("a market")
        .1
    };
    let world = &mut sim.context_mut().world;
    world.get_mut::<CargoTable>().change(PLAYER_CART, wheat, 10);
    world.get_mut::<MarketStore>().price[cell] = 100;
    let coins = world.get::<PlayerState>().coins[continent];
    SellGoods {
        good: wheat,
        quantity: 10,
    }
    .apply(sim.context_mut());
    assert_eq!(
        coins + 10 * 70,
        sim.world().get::<PlayerState>().coins[continent]
    );
}

#[test]
fn producers_are_paid_the_offer() {
    let paid = |spread: &str| {
        let spread = format!("markets.trader_spread_permille,{spread}");
        let mut sim = build(&sample_config(&[&spread, "credit.working_weeks,0"]), 42);
        sim.step().expect("step");
        sim.world().get::<FacilityTable>().cash.iter().sum::<i64>()
    };
    assert!(paid("300") < paid("0"));
}

#[test]
fn idle_float_goes_to_households_only_beyond_the_weeks_kept() {
    let mut sim = build(
        &sample_config(&[
            "traders.float_keep_weeks,8",
            "traders.float_payout_permille,500",
            "traders.loan_share_permille,0",
        ]),
        42,
    );
    let nodes = sim.world().get::<PurseState>().nation_nodes[0].clone();
    let w = &mut sim.context_mut().world;
    let cash = w.get_mut::<HouseholdCash>();
    for &n in &nodes {
        cash.desired[n as usize] = 0;
    }
    cash.desired[nodes[0] as usize] = 1_000;
    let loans = w.get_mut::<TraderLoans>();
    for &n in &nodes {
        loans.owed_households[n as usize] = 0;
        loans.interest_households[n as usize] = 0;
    }
    loans.owed_bank[0] = 0;
    loans.interest_bank[0] = 0;
    let held = |sim: &Simulation| {
        let cash = sim.world().get::<HouseholdCash>();
        nodes.iter().map(|&n| cash.cash[n as usize]).sum::<i64>()
    };
    let run = |sim: &mut Simulation| {
        TraderLoanSystem.execute(sim.context_mut()).expect("loans");
    };

    sim.context_mut().world.get_mut::<PurseState>().float[0] = 8_000;
    let before = held(&sim);
    run(&mut sim);
    assert_eq!(
        8_000,
        sim.world().get::<PurseState>().float[0],
        "what trade needs stays"
    );
    assert_eq!(before, held(&sim));

    sim.context_mut().world.get_mut::<PurseState>().float[0] = 10_000;
    let total = money(&sim);
    run(&mut sim);
    assert_eq!(
        9_000,
        sim.world().get::<PurseState>().float[0],
        "half of the excess this week"
    );
    assert_eq!(before + 1_000, held(&sim));
    assert_eq!(total, money(&sim));
}

#[test]
fn a_far_flow_pays_its_margin_as_wages_at_the_origin() {
    let wages = |share: &str| -> (i64, i64) {
        let share = format!("merchants.flow_wage_permille,{share}");
        let mut sim = build(&sample_config(&["lod.mode,2", &share]), 42);
        sim.step().expect("step");
        let bread = good(&sim, "bread");
        let (from, cell, dear) = {
            let ctx = sim.context();
            let nodes = ctx.services.get::<NodeTable>();
            let routes = ctx.services.get::<RouteTable>();
            let markets = ctx.world.get::<MarketStore>();
            let r = (0..routes.count())
                .find(|&r| {
                    nodes.level_of(routes.from[r]) == WorldLevel::Village
                        && nodes.level_of(routes.to[r]) == WorldLevel::City
                        && markets
                            .try_find_cell(routes.from[r] as usize, bread)
                            .is_some()
                        && markets
                            .try_find_cell(routes.to[r] as usize, bread)
                            .is_some()
                })
                .expect("a village to city route");
            let (from, to) = (routes.from[r] as usize, routes.to[r] as usize);
            (
                from,
                markets.try_find_cell(from, bread).unwrap(),
                markets.try_find_cell(to, bread).unwrap(),
            )
        };
        let world = &mut sim.context_mut().world;
        let markets = world.get_mut::<MarketStore>();
        let start = markets.market_cell_start[from] as usize;
        for c in start..start + markets.market_cell_count[from] as usize {
            markets.stock[c] = 0;
        }
        markets.stock[cell] = 1_000;
        markets.price[cell] = 10;
        markets.price[dear] = 1_000;
        let purse = world.get::<PurseState>().node_float[from] as usize;
        world.get_mut::<PurseState>().float[purse] = 1_000_000_000;
        let before = world.get::<HouseholdCash>().cash[from];
        let total = money(&sim);
        MerchantSystem::default()
            .execute(sim.context_mut())
            .expect("merchants");
        let moved = 1_000 - i64::from(sim.world().get::<MarketStore>().stock[cell]);
        assert_eq!(total, money(&sim));
        (
            sim.world().get::<HouseholdCash>().cash[from] - before,
            moved,
        )
    };
    let (paid, units) = wages("1000");
    assert!(units > 0);
    assert!(paid > 0);
    assert_eq!(0, wages("0").0);
}
