//! Player commands on securities, bonds and roads (C# `ShortTests`, `ShareTests`, `VentureTests`,
//! `ClassesCoverAndBondsTests` command cases and `InfrastructureTests.Player_BuildsARoad_AndKeepsItUp`).
mod common;

use common::{build, build_sample, sample_config};
use fes_sim::agents::asset_prices::{
    households_holder, share_price, venture_price, Refs, PLAYER_HOLDER,
};
use fes_sim::agents::banks::BankState;
use fes_sim::agents::credit::BondTable;
use fes_sim::agents::facilities::{FacilityTable, OWNER_NPC};
use fes_sim::agents::groups::GroupTable;
use fes_sim::agents::player::PlayerState;
use fes_sim::agents::purses::PurseState;
use fes_sim::agents::securities::{
    AssetMarkets, ShareTable, ShortLicences, ShortTable, VentureTable,
};
use fes_sim::agents::LoanTable;
use fes_sim::commands::bank::BankTransfer;
use fes_sim::commands::bonds::{BuyBond, SellBond, BOND_PLAYER_HOLDER};
use fes_sim::commands::places::BuildRoad;
use fes_sim::commands::securities::{
    BuyShare, BuyShortLicence, BuyVentureShare, CoverShort, SellShare, SellShort, SellVentureShare,
    MARKET_STOCK,
};
use fes_sim::commands::travel::find_route;
use fes_sim::economy::HouseholdCash;
use fes_sim::world::roads::{MAX_CONDITION, PLAYER_OWNER};
use fes_sim::world::{NodeBanks, NodeTable, RoadState, RouteTable};
use fes_sim::{views, Command, CommandResult, Simulation};

const PLENTY: i64 = 1_000_000_000;

fn error(cmd: &dyn Command, sim: &Simulation) -> Option<String> {
    match cmd.validate(sim.context()) {
        CommandResult::Ok => None,
        CommandResult::Error(e) => Some(e),
    }
}

fn run(sim: &mut Simulation, cmd: &dyn Command) {
    assert_eq!(None, error(cmd, sim));
    cmd.apply(sim.context_mut());
}

fn nodes(sim: &Simulation) -> NodeTable {
    sim.context().services.get::<NodeTable>().clone()
}

fn banks(sim: &Simulation) -> NodeBanks {
    sim.context().services.get::<NodeBanks>().clone()
}

fn coins_at(sim: &mut Simulation, node: i32) -> &mut i64 {
    let c = nodes(sim).continent[node as usize] as usize;
    &mut sim.context_mut().world.get_mut::<PlayerState>().coins[c]
}

fn share_price_of(sim: &Simulation, f: usize) -> i64 {
    let ctx = sim.context();
    let r = Refs {
        db: &ctx.services.database,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    };
    share_price(&ctx.world, r, f)
}

fn nation(sim: &Simulation, f: usize) -> i32 {
    let node = sim.world().get::<FacilityTable>().node[f];
    banks(sim).nation_of(&nodes(sim), node)
}

/// C# `ShareTests.GroupFacility`.
fn group_facility(sim: &Simulation) -> usize {
    let t = sim.world().get::<FacilityTable>();
    let db = &sim.context().services.database;
    (0..t.count())
        .find(|&f| {
            t.owner[f] == OWNER_NPC
                && t.group[f] >= 0
                && t.level[f] > 0
                && !db.facilities[t.def_index[f] as usize].state_owned
                && nation(sim, f) >= 0
        })
        .expect("a group facility")
}

/// At a branch of the facility's nation's bank, with coins; returns the branch.
fn at_the_exchange(sim: &mut Simulation, f: usize) -> i32 {
    let n = nodes(sim);
    let b = banks(sim);
    let nation = nation(sim, f);
    let branch = (0..n.count() as i32)
        .find(|&x| b.branch_nation(&n, x) == nation)
        .expect("a branch");
    sim.context_mut().world.get_mut::<PlayerState>().node = branch;
    *coins_at(sim, branch) = PLENTY;
    branch
}

#[test]
fn the_player_buys_shares_from_the_group_and_sells_them_back() {
    let mut sim = build_sample(&[]);
    let f = group_facility(&sim);
    let branch = at_the_exchange(&mut sim, f);
    let group = sim.world().get::<FacilityTable>().group[f] as usize;
    let price = share_price_of(&sim, f);
    let cash = sim.world().get::<GroupTable>().cash[group];
    run(
        &mut sim,
        &BuyShare {
            facility: f as i32,
            count: 10,
        },
    );
    assert_eq!(
        10,
        sim.world()
            .get::<ShareTable>()
            .held(f as i32, PLAYER_HOLDER)
    );
    assert_eq!(PLENTY - 10 * price, *coins_at(&mut sim, branch));
    assert_eq!(
        cash + 10 * price,
        sim.world().get::<GroupTable>().cash[group]
    );
    sim.context_mut().world.get_mut::<GroupTable>().cash[group] += PLENTY;
    run(
        &mut sim,
        &SellShare {
            facility: f as i32,
            count: 4,
        },
    );
    assert_eq!(
        6,
        sim.world()
            .get::<ShareTable>()
            .held(f as i32, PLAYER_HOLDER)
    );
    assert!(
        error(
            &SellShare {
                facility: f as i32,
                count: 7
            },
            &sim
        )
        .is_some(),
        "holds only six"
    );
    let view = views::shares(sim.context());
    assert_eq!(1, view.shares.len());
    assert_eq!(6, view.shares[0].count);
}

#[test]
fn the_group_keeps_control() {
    let mut sim = build_sample(&[]);
    let f = group_facility(&sim);
    at_the_exchange(&mut sim, f);
    let most = sim
        .context()
        .services
        .database
        .config
        .facility_max_listed_shares;
    assert_eq!(
        None,
        error(
            &BuyShare {
                facility: f as i32,
                count: most
            },
            &sim
        )
    );
    assert_eq!(
        Some("The group keeps control of it."),
        error(
            &BuyShare {
                facility: f as i32,
                count: most + 1
            },
            &sim
        )
        .as_deref()
    );
}

#[test]
fn shares_are_traded_only_at_the_nations_bank() {
    let mut sim = build_sample(&[]);
    let f = group_facility(&sim);
    let n = nodes(&sim);
    let b = banks(&sim);
    let away = (0..n.count() as i32)
        .find(|&x| b.branch_nation(&n, x) < 0)
        .expect("no branch");
    sim.context_mut().world.get_mut::<PlayerState>().node = away;
    *coins_at(&mut sim, away) = PLENTY;
    assert_eq!(
        Some("Shares of this facility trade at its nation's bank."),
        error(
            &BuyShare {
                facility: f as i32,
                count: 1
            },
            &sim
        )
        .as_deref()
    );
}

/// C# `ShortTests.Listed`: a world where groups have listed shares.
fn listed() -> Simulation {
    let mut sim = build(
        &sample_config(&["credit.working_weeks,4", "credit.company_debt_permille,0"]),
        1,
    );
    for _ in 0..30 {
        sim.step().expect("step");
    }
    sim
}

fn listed_facility(sim: &Simulation) -> usize {
    let t = sim.world().get::<FacilityTable>();
    let shares = sim.world().get::<ShareTable>();
    let db = &sim.context().services.database;
    (0..t.count())
        .find(|&f| {
            t.owner[f] == OWNER_NPC
                && t.group[f] >= 0
                && t.level[f] > 0
                && !db.facilities[t.def_index[f] as usize].state_owned
                && nation(sim, f) >= 0
                && shares.outside(f as i32) > 0
                && shares.outside(f as i32) < 60
        })
        .expect("a listed facility")
}

/// Coins moved from the player's purse into the deposit.
fn deposit(sim: &mut Simulation, nation: i32, amount: i64) {
    let node = sim.world().get::<PlayerState>().node;
    *coins_at(sim, node) -= amount;
    let bank = sim.context_mut().world.get_mut::<BankState>();
    bank.player_deposit[nation as usize] += amount;
    bank.reserve[nation as usize] += amount;
}

fn licence(sim: &mut Simulation, nation: i32) {
    sim.context_mut().world.get_mut::<ShortLicences>().licensed[nation as usize] = 1;
}

#[test]
fn the_licence_needs_a_year_of_trading_and_a_deposit() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    let branch = at_the_exchange(&mut sim, f);
    let buy = BuyShortLicence { kind: MARKET_STOCK };
    assert_eq!(
        Some("The licence needs a year of trading on this exchange."),
        error(&buy, &sim).as_deref()
    );
    run(
        &mut sim,
        &BuyShare {
            facility: f as i32,
            count: 1,
        },
    );
    let config = sim.context().services.database.config.clone();
    for _ in 0..config.short_licence_weeks {
        sim.step().expect("step");
    }
    assert_eq!(
        Some("The licence needs a deposit at this exchange's bank."),
        error(&buy, &sim).as_deref()
    );
    deposit(&mut sim, nation, config.short_licence_deposit);
    let reserve = sim.world().get::<BankState>().reserve[nation as usize];
    let coins = *coins_at(&mut sim, branch);
    run(&mut sim, &buy);
    assert_eq!(
        1,
        sim.world().get::<ShortLicences>().licensed[nation as usize]
    );
    assert_eq!(
        coins - config.short_licence_fee,
        *coins_at(&mut sim, branch)
    );
    assert_eq!(
        reserve + config.short_licence_fee,
        sim.world().get::<BankState>().reserve[nation as usize]
    );
}

#[test]
fn selling_short_needs_the_licence_and_the_margin() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    at_the_exchange(&mut sim, f);
    let short = |count| SellShort {
        venture: false,
        asset: f as i32,
        count,
    };
    assert_eq!(
        Some("Short selling needs this exchange's licence."),
        error(&short(5), &sim).as_deref()
    );
    licence(&mut sim, nation);
    sim.context_mut()
        .world
        .get_mut::<BankState>()
        .player_deposit[nation as usize] = 0;
    assert_eq!(
        Some("The deposit at the exchange's bank does not cover the margin."),
        error(&short(5), &sim).as_deref()
    );
    let max = sim.context().services.database.config.short_max_lent_shares;
    assert_eq!(
        Some("No one lends so many shares."),
        error(&short(max + 1), &sim).as_deref()
    );
}

#[test]
fn a_short_sale_sells_borrowed_shares_into_the_deposit_and_is_bought_back() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    at_the_exchange(&mut sim, f);
    licence(&mut sim, nation);
    deposit(&mut sim, nation, 100_000);
    let holder = households_holder(nation);
    let households = sim.world().get::<ShareTable>().held(f as i32, holder);
    let price = share_price_of(&sim, f);
    let before = sim.world().get::<BankState>().player_deposit[nation as usize];
    run(
        &mut sim,
        &SellShort {
            venture: false,
            asset: f as i32,
            count: 5,
        },
    );
    let sold = sim.world().get::<ShortTable>().held(false, f as i32);
    assert!(sold > 0);
    assert_eq!(
        households + sold,
        sim.world().get::<ShareTable>().held(f as i32, holder)
    );
    let now = sim.world().get::<BankState>().player_deposit[nation as usize];
    assert_eq!(before + i64::from(sold) * price, now);
    assert_eq!(
        Some("The deposit holds the margin on short sales."),
        error(&BankTransfer { amount: -now }, &sim).as_deref()
    );
    run(
        &mut sim,
        &CoverShort {
            venture: false,
            asset: f as i32,
            count: sold,
        },
    );
    assert_eq!(0, sim.world().get::<ShortTable>().held(false, f as i32));
    assert_eq!(
        households,
        sim.world().get::<ShareTable>().held(f as i32, holder)
    );
    assert_eq!(
        before,
        sim.world().get::<BankState>().player_deposit[nation as usize]
    );
}

#[test]
fn the_lender_is_paid_a_fee_each_week() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    at_the_exchange(&mut sim, f);
    licence(&mut sim, nation);
    deposit(&mut sim, nation, 100_000);
    run(
        &mut sim,
        &SellShort {
            venture: false,
            asset: f as i32,
            count: 10,
        },
    );
    let deposit = sim.world().get::<BankState>().player_deposit[nation as usize];
    let config = sim.context().services.database.config.clone();
    let fee = config.short_fee.mul_int(10 * share_price_of(&sim, f)) / 52;
    let interest = config.deposit_interest_per_year.mul_int(deposit) / 52;
    assert!(fee > 0);
    sim.step().expect("step");
    let paid = deposit + interest - sim.world().get::<BankState>().player_deposit[nation as usize];
    assert!(paid > fee / 2, "a week's fee left the deposit");
    assert!(paid < fee * 2, "no more than about a week's fee");
}

#[test]
fn a_rising_price_calls_for_margin_then_the_bank_buys_back() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    at_the_exchange(&mut sim, f);
    licence(&mut sim, nation);
    let price = share_price_of(&sim, f);
    let open = sim.context().services.database.config.short_open_share;
    deposit(&mut sim, nation, open.mul_int(10 * price) - 10 * price + 1);
    run(
        &mut sim,
        &SellShort {
            venture: false,
            asset: f as i32,
            count: 10,
        },
    );
    sim.context_mut()
        .world
        .get_mut::<PlayerState>()
        .coins
        .fill(0);
    let weeks = sim.context().services.database.config.margin_call_weeks;
    let (mut called, mut covered) = (0, Vec::new());
    for _ in 0..=weeks + 1 {
        sim.context_mut().world.get_mut::<AssetMarkets>().premium[nation as usize] =
            fes_core::Fx::from_int(4).raw();
        let report = sim.step().expect("step");
        called += count_of(&report, "ShortCalled");
        covered.extend(
            report
                .events
                .iter()
                .filter(|e| e.type_name.ends_with("ShortsCovered"))
                .map(|e| e.debug.clone()),
        );
    }
    assert_eq!(1, called);
    assert_eq!(1, covered.len());
    let lent: i64 = covered[0]
        .split("lent: ")
        .nth(1)
        .and_then(|t| t.split(|c: char| !c.is_ascii_digit()).next())
        .and_then(|t| t.parse().ok())
        .expect("lent");
    assert!(
        lent > 0,
        "the deposit fell short, and the bank lent the rest"
    );
    assert_eq!(0, sim.world().get::<ShortTable>().count());
    assert_eq!(1, sim.world().get::<LoanTable>().count());
}

#[test]
fn shorts_keep_the_coins_and_are_saved() {
    let mut sim = listed();
    let f = listed_facility(&sim);
    let nation = nation(&sim, f);
    at_the_exchange(&mut sim, f);
    licence(&mut sim, nation);
    deposit(&mut sim, nation, 100_000);
    let counts = common::continent_count(&sim);
    let supply: Vec<i64> = (0..counts)
        .map(|c| common::continent_money(&sim, c))
        .collect();
    run(
        &mut sim,
        &SellShort {
            venture: false,
            asset: f as i32,
            count: 10,
        },
    );
    let check = |sim: &Simulation, what: &str| {
        for (c, expected) in supply.iter().enumerate() {
            assert_eq!(*expected, common::continent_money(sim, c), "{what}");
        }
    };
    check(&sim, "after the sale");
    for t in 0..10 {
        sim.context_mut().world.get_mut::<AssetMarkets>().premium[nation as usize] =
            fes_core::Fx::from_int(5).raw();
        sim.step().expect("step");
        check(&sim, &format!("turn {t}"));
    }
    common::assert_saved_and_loaded(&sim);
}

#[test]
fn the_player_trades_venture_shares_at_the_continental_exchange() {
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let b = banks(&sim);
    let continent = sim.world().get::<VentureTable>().continent[0];
    let exchange = sim
        .world()
        .get::<AssetMarkets>()
        .exchange_node(&n, &b, continent);
    assert!(exchange >= 0);
    sim.context_mut().world.get_mut::<PlayerState>().node = if exchange == 0 { 1 } else { 0 };
    *coins_at(&mut sim, exchange) = PLENTY;
    assert_eq!(
        Some("Venture shares trade at the continent's exchange."),
        error(
            &BuyVentureShare {
                venture: 0,
                count: 1
            },
            &sim
        )
        .as_deref()
    );
    sim.context_mut().world.get_mut::<PlayerState>().node = exchange;
    sim.context_mut().world.get_mut::<VentureTable>().cash[0] = 50_000;
    let price = venture_price(sim.world(), &sim.context().services.database.config, 0);
    run(
        &mut sim,
        &BuyVentureShare {
            venture: 0,
            count: 20,
        },
    );
    assert_eq!(20, sim.world().get::<VentureTable>().player_shares[0]);
    assert_eq!(PLENTY - 20 * price, *coins_at(&mut sim, exchange));
    let all = sim.context().services.database.config.venture_shares;
    assert!(
        error(
            &BuyVentureShare {
                venture: 0,
                count: all
            },
            &sim
        )
        .is_some(),
        "only what the households hold"
    );
    assert!(error(
        &SellVentureShare {
            venture: 0,
            count: 21
        },
        &sim
    )
    .is_some());
    assert_eq!(
        None,
        error(
            &SellVentureShare {
                venture: 0,
                count: 5
            },
            &sim
        )
    );
}

fn rejected(report: &fes_sim::TurnReport) -> usize {
    report
        .events
        .iter()
        .filter(|e| e.type_name.ends_with("CommandRejected"))
        .count()
}

fn count_of(report: &fes_sim::TurnReport, name: &str) -> usize {
    report
        .events
        .iter()
        .filter(|e| e.type_name.ends_with(name))
        .count()
}

#[test]
fn player_builds_a_road_and_keeps_it_up() {
    let mut sim = build_sample(&[]);
    let config = sim.context().services.database.config.clone();
    let n = nodes(&sim);
    let village = n.try_find("aur_n1_r1_c1_v1").expect("village").0;
    let city = n.try_find("aur_n1_r1_c1").expect("city").0;
    {
        let p = sim.context_mut().world.get_mut::<PlayerState>();
        p.coins.iter_mut().for_each(|c| *c = 0);
        p.node = village;
    }
    *coins_at(&mut sim, village) = 10_000;
    sim.queue(Box::new(BuildRoad { destination: city }));
    sim.queue(Box::new(BuildRoad { destination: city })); // already under construction
    let report = sim.step().expect("step");
    assert_eq!(1, rejected(&report));
    assert_eq!(
        10_000 - config.road_build_cost_per_turn,
        *coins_at(&mut sim, village)
    );
    let routes = sim.context().services.get::<RouteTable>().clone();
    let (there, back) = (
        find_route(&routes, village, city),
        find_route(&routes, city, village),
    );
    let mut built = count_of(&report, "RoadBuilt");
    for _ in 0..config.road_build_weeks {
        if built > 0 {
            break;
        }
        built = count_of(&sim.step().expect("step"), "RoadBuilt");
    }
    assert_eq!(2, built);
    let roads = sim.world().get::<RoadState>();
    assert_eq!(MAX_CONDITION, roads.condition[there as usize]);
    assert_eq!(PLAYER_OWNER, roads.owner[back as usize]);
    let coins = *coins_at(&mut sim, village);
    sim.step().expect("step");
    assert_eq!(coins - 2 * config.road_upkeep, *coins_at(&mut sim, village));
    sim.queue(Box::new(BuildRoad { destination: city }));
    assert_eq!(1, rejected(&sim.step().expect("step")), "already paved");
}

/// C# `ClassesCoverAndBondsTests.AtBranch`: the player at the first branch with local coins.
fn at_branch() -> (Simulation, i32) {
    let mut sim = build_sample(&[]);
    let n = nodes(&sim);
    let b = banks(&sim);
    let branch = (0..n.count() as i32)
        .find(|&x| b.branch_nation(&n, x) >= 0)
        .expect("a branch");
    sim.context_mut().world.get_mut::<PlayerState>().node = branch;
    *coins_at(&mut sim, branch) = 1_000_000;
    (sim, b.branch_nation(&n, branch))
}

fn total_money(sim: &Simulation) -> i64 {
    (0..common::continent_count(sim))
        .map(|c| common::continent_money(sim, c))
        .sum()
}

fn player_bonds(sim: &Simulation, issuer: i32) -> i64 {
    views::purse(sim.context())
        .bonds
        .iter()
        .find(|b| b.0 == issuer)
        .map_or(0, |b| b.1)
}

#[test]
fn the_player_buys_a_state_bond_and_earns_in_the_account() {
    let (mut sim, nation) = at_branch();
    let issuer = !nation;
    let node = sim.world().get::<PlayerState>().node;
    let coins = *coins_at(&mut sim, node);
    let deposit = sim.world().get::<BankState>().player_deposit[nation as usize];
    let money = total_money(&sim);
    run(
        &mut sim,
        &BuyBond {
            issuer,
            amount: 1_000,
        },
    );
    assert_eq!(coins - 1_000, *coins_at(&mut sim, node));
    assert_eq!(1_000, player_bonds(&sim, issuer));
    assert_eq!(money, total_money(&sim));
    sim.step().expect("step");
    assert!(sim.world().get::<BankState>().player_deposit[nation as usize] > deposit);
    assert!(player_bonds(&sim, issuer) < 1_000);
}

#[test]
fn the_player_sells_a_bond_to_those_with_idle_money() {
    let (mut sim, nation) = at_branch();
    let issuer = !nation;
    sim.context_mut()
        .world
        .get_mut::<BondTable>()
        .add(issuer, BOND_PLAYER_HOLDER, 500);
    let towns = sim.world().get::<PurseState>().nation_nodes[nation as usize].clone();
    for n in towns {
        sim.context_mut().world.get_mut::<HouseholdCash>().cash[n as usize] += 1_000_000;
    }
    let node = sim.world().get::<PlayerState>().node;
    let coins = *coins_at(&mut sim, node);
    let money = total_money(&sim);
    run(
        &mut sim,
        &SellBond {
            issuer,
            amount: 500,
        },
    );
    let sold = *coins_at(&mut sim, node) - coins;
    assert!(sold > 0);
    assert_eq!(500 - sold, player_bonds(&sim, issuer));
    assert_eq!(money, total_money(&sim));
    assert!(
        error(
            &SellBond {
                issuer,
                amount: 501
            },
            &sim
        )
        .is_some(),
        "more than held"
    );
}

#[test]
fn a_bond_trades_only_at_the_issuers_bank_and_never_the_players_own_group() {
    let (mut sim, nation) = at_branch();
    let other = if nation == 0 { 1 } else { 0 };
    assert!(error(
        &BuyBond {
            issuer: !other,
            amount: 10
        },
        &sim
    )
    .is_some());
    let node = sim.world().get::<PlayerState>().node;
    let coins = *coins_at(&mut sim, node);
    assert!(error(
        &BuyBond {
            issuer: !nation,
            amount: coins + 1
        },
        &sim
    )
    .is_some());
    let groups = sim.world().get::<GroupTable>().clone();
    for g in 0..groups.count() {
        if groups.is_player[g] != 0 {
            assert!(error(
                &BuyBond {
                    issuer: g as i32,
                    amount: 10
                },
                &sim
            )
            .is_some());
        }
    }
}
