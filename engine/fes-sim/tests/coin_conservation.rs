//! The C# keep-the-coins tests: after every turn each continent's money equals its coin supply
//! (`MoneyLedger.ContinentMoney`), and the saved world loads back to the same hash.
mod common;

use common::{
    assert_conserved, assert_saved_and_loaded, build, build_sample, sample_config,
    sample_with_config,
};
use fes_sim::agents::banks::BankState;
use fes_sim::agents::nobles::NobleState;
use fes_sim::agents::orders::MerchantTripStats;
use fes_sim::economy::{CurrencyState, HouseholdState};
use fes_sim::world::NodeTable;
use fes_sim::Simulation;

fn listed() -> Simulation {
    build(
        &sample_config(&["credit.working_weeks,4", "credit.company_debt_permille,0"]),
        1,
    )
}

fn play(sim: &mut Simulation, turns: i32, what: &str) {
    for t in 0..turns {
        sim.step().expect("step");
        assert_conserved(sim, &format!("{what}, turn {t}"));
    }
}

#[test]
fn shares_keep_the_coins_and_are_saved() {
    let mut sim = listed();
    play(&mut sim, 30, "shares");
    assert_saved_and_loaded(&sim);
}

#[test]
fn ventures_keep_the_coins_and_are_saved() {
    let mut sim = build_sample(&[]);
    play(&mut sim, 80, "ventures");
    assert_saved_and_loaded(&sim);
}

#[test]
fn markets_keep_the_coins_and_are_saved() {
    let mut sim = listed();
    play(&mut sim, 80, "markets");
    assert_saved_and_loaded(&sim);
}

#[test]
fn the_luxury_tax_goes_to_the_state_and_keeps_the_coins() {
    let mut sim = build_sample(&[]);
    play(&mut sim, 10, "luxury tax");
}

#[test]
fn aggregate_flows_cross_at_a_share_and_keep_each_continents_coins() {
    let mut sim = build(&sample_config(&["lod.mode,2"]), 42);
    let start = sim.world().get::<BankState>().international_reserve.clone();
    play(&mut sim, 3 * 52, "aggregate flows");
    let now = &sim.world().get::<BankState>().international_reserve;
    assert!(
        now.iter().zip(&start).any(|(a, b)| a != b),
        "coins changed at the banks"
    );
}

#[test]
fn nations_keep_their_international_bank_in_coins() {
    let mut sim = build_sample(&[]);
    sim.step().expect("step");
    let nodes = sim.context().services.get::<NodeTable>().clone();
    let banks = sim
        .context()
        .services
        .get::<fes_sim::world::NodeBanks>()
        .clone();
    let nation_on = |c: i32| {
        (0..banks.nation_count(&nodes))
            .find(|&n| nodes.continent[banks.nation_node(&nodes, n) as usize] == c)
            .expect("a nation") as usize
    };
    let share = |sim: &Simulation, c: usize| {
        let supply = sim.world().get::<CurrencyState>().coin_supply[c];
        sim.context()
            .services
            .database
            .config
            .international_bank_reserve_share
            .mul_int(supply)
    };
    let (share0, share1) = (share(&sim, 0), share(&sim, 1));
    let (n0, n1) = (nation_on(0), nation_on(1));
    // Coins move inside each continent: one bank left empty, the other with far too many.
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.reserve[n0] += bank.international_reserve[0];
    bank.international_reserve[0] = 0;
    bank.reserve[n1] -= 3 * share1;
    bank.international_reserve[1] += 3 * share1;
    sim.step().expect("step");
    let bank = sim.world().get::<BankState>();
    assert!(bank.international_reserve[0] > share0 / 2, "refilled");
    assert!(
        bank.international_reserve[1] < 2 * share1 + share1 / 2,
        "the excess handed back"
    );
    assert_conserved(&sim, "international banks");
}

#[test]
fn villagers_move_to_towns_and_money_stays_conserved() {
    let mut sim = build_sample(&[]);
    let people = |sim: &Simulation| {
        sim.world()
            .get::<HouseholdState>()
            .population
            .iter()
            .sum::<i64>()
    };
    let before = people(&sim);
    for _ in 0..5 * 52 {
        sim.step().expect("step");
    }
    assert!(before > 0);
    assert_conserved(&sim, "migration");
}

#[test]
fn houses_keep_money_conserved() {
    let mut sim = build_sample(&[]);
    for _ in 0..2 * 52 + 5 {
        sim.step().expect("step");
    }
    assert!(sim.world().get::<NobleState>().cash.iter().sum::<i64>() > 0);
    assert_conserved(&sim, "houses");
}

#[test]
fn rumors_conserve_money() {
    let mut sim = build(&sample_with_config("key,value\nmerchants.rumors,1\n"), 42);
    play(&mut sim, 30, "rumors");
}

#[test]
fn price_cover_is_bought_and_keeps_the_coins_whole() {
    let mut sim = build_sample(&[]);
    for _ in 0..6 * 52 {
        sim.step().expect("step");
    }
    assert!(
        sim.context()
            .services
            .get::<MerchantTripStats>()
            .spent_hedge
            > 0
    );
    assert_conserved(&sim, "price cover");
}

#[test]
fn state_projects_and_subsidies_conserve_money() {
    let mut sim = build(
        &sample_with_config(
            "key,value\nfacility.bankrupt_weeks,100000\nbank.npc_max_reserve_share_permille,0\nbank.state_reserve_target_permille,0\nstate.project_weeks,4\nstate.subsidy_max_per_nation,0\n",
        ),
        42,
    );
    play(&mut sim, 3 * 52, "state projects");
    let mut sim = build(
        &sample_with_config("key,value\nbank.state_reserve_target_permille,0\n"),
        42,
    );
    play(&mut sim, 4 * 52, "subsidy");
}
