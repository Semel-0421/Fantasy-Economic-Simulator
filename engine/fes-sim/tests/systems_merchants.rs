//! `agents.travel` and `agents.merchants` against the recorded C# turns, and the C# `MerchantTests`,
//! `MerchantInsuranceTests` and travel tests ported. The Burst-against-managed parity tests have no
//! Rust counterpart: the job runs as one plain loop.
mod common;

use common::{build, build_sample, data_dir, sample_config, sample_with_config};
use fes_core::det::det_hash;
use fes_core::testing::fixtures_dir;
use fes_core::{DetRandom, Fx, QualityGrade, SimDetail, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef, SimulationConfig};
use fes_sim::agents::groups::KIND_GUILD;
use fes_sim::agents::merchants::flow_job::FlowJob;
use fes_sim::agents::merchants::traits::{
    trait_of, AGGRESSIVE, CAUTIOUS, EXPEDITION, LOCAL, PEDDLER,
};
use fes_sim::agents::merchants::MerchantBankrupt;
use fes_sim::agents::merchants::{capital_start, TRAIT_NEUTRAL};
use fes_sim::agents::travel::PlayerArrived;
use fes_sim::agents::{
    AgentTable, BankState, CargoTable, GroupTable, LodState, MerchantSystem, MerchantTraitTable,
    MerchantTripStats, NodeMemory, PlayerState, PurseState,
};
use fes_sim::economy::{HouseholdCash, MarketStore};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::NodeTable;
use fes_sim::{SimEventRecord, Simulation, System};

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const PORTED: [&str; 9] = [
    "agents.travel",
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
    "agents.production",
    "agents.merchants",
    "economy.pricing",
    "agents.household_spending",
    "agents.home_surplus",
];
const MERCHANT_HOLDER_BASE: i32 = 2 << 28;

#[test]
fn travel_and_merchants_match_on_every_sampled_turn() {
    let source = FileDataSource::new(data_dir());
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, &PORTED).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        for id in ["agents.travel", "agents.merchants"] {
            let label = format!("system:{id}");
            assert!(
                report
                    .steps
                    .iter()
                    .any(|s| matches!(s, StepResult::Equal { label: l } if *l == label)),
                "turn {t}: {id} did not run in Rust"
            );
        }
    }
}

fn agents(sim: &Simulation) -> &AgentTable {
    sim.world().get::<AgentTable>()
}

fn agents_mut(sim: &mut Simulation) -> &mut AgentTable {
    sim.context_mut().world.get_mut::<AgentTable>()
}

fn detailed(sim: &Simulation, node: i32) -> bool {
    sim.context().services.get::<LodState>().detail[node as usize] == SimDetail::Detailed as u8
}

fn step(sim: &mut Simulation) -> Vec<SimEventRecord> {
    sim.step().expect("step").events
}

fn load_of(sim: &Simulation, a: usize) -> Vec<(i32, i32)> {
    let cargo = sim.world().get::<CargoTable>();
    (0..cargo.count())
        .filter(|&r| cargo.holder[r] == MERCHANT_HOLDER_BASE + a as i32)
        .map(|r| (cargo.good[r], cargo.quantity[r]))
        .collect()
}

#[test]
fn player_arrives_on_the_arrival_turn() {
    let mut sim = build_sample(&[]);
    let destination = (sim.world().get::<PlayerState>().node + 1) % 5;
    let player = sim.context_mut().world.get_mut::<PlayerState>();
    player.travel_destination = destination;
    player.arrival_turn = 1;
    assert!(step(&mut sim)
        .iter()
        .all(|e| !matches!(e, SimEventRecord::PlayerArrived(_))));
    assert!(sim.world().get::<PlayerState>().is_travelling());
    let events = step(&mut sim);
    let player = sim.world().get::<PlayerState>();
    assert_eq!(player.node, destination);
    assert!(!player.is_travelling());
    assert_eq!(player.arrival_turn, -1);
    assert!(
        events.contains(&SimEventRecord::PlayerArrived(PlayerArrived {
            node: destination,
            turn: 1
        }))
    );
}

#[test]
fn detailed_merchant_buys_travels_and_sells() {
    let mut sim = build_sample(&["merchants.must_act,0"]);
    step(&mut sim);
    let mut trader = None;
    for _ in 0..10 {
        step(&mut sim);
        trader = (0..agents(&sim).count())
            .find(|&a| detailed(&sim, agents(&sim).node[a]) && agents(&sim).destination[a] >= 0);
        if trader.is_some() {
            break;
        }
    }
    let a = trader.expect("no detailed merchant set off");
    assert_eq!(load_of(&sim, a).len(), 1);
    let (destination, arrival, cash) = {
        let t = agents(&sim);
        (t.destination[a], t.arrival_turn[a], t.cash[a])
    };
    while sim.turn() <= arrival {
        step(&mut sim);
    }
    // It sold the load on arrival; it may already have set off again from there.
    assert_eq!(agents(&sim).node[a], destination);
    assert_ne!(agents(&sim).cash[a], cash);
}

fn holds_licenses(seed: u64, a: usize, permille: i32) -> bool {
    let stream = det_hash::of("agents.licenses.merchants");
    DetRandom::for_key(seed, 0, stream, a as i32, 0).chance_permille(permille)
}

#[test]
fn small_groups_stay_on_their_continent_and_carry_licensed_goods_only_with_licenses() {
    let mut sim = build_sample(&[]);
    let db = std::sync::Arc::clone(&sim.context().services.database);
    let mut trips = 0;
    for _ in 0..40 {
        step(&mut sim);
        let nodes = sim.context().services.get::<NodeTable>();
        let t = agents(&sim);
        for a in 0..t.count() {
            let small = i32::from(t.scale[a]) > db.config.merchant_cross_level;
            let home = nodes.continent[t.home[a] as usize];
            if small {
                assert_eq!(home, nodes.continent[t.node[a] as usize]);
            }
            if t.destination[a] < 0 {
                continue;
            }
            trips += 1;
            if small {
                assert_eq!(home, nodes.continent[t.destination[a] as usize]);
            }
            let licensed = holds_licenses(42, a, db.config.licensed_merchant_permille);
            let load = load_of(&sim, a);
            assert!(
                licensed
                    || load
                        .iter()
                        .all(|(g, _)| !db.goods[*g as usize].traits.requires_license)
            );
        }
    }
    assert!(trips > 0);
}

#[test]
fn merchant_below_zero_cash_is_replaced_at_home() {
    let mut sim = build_sample(&["merchants.must_act,0", "merchants.join_permille,0"]);
    step(&mut sim); // LOD is set on the first turn
    let a = (0..agents(&sim).count())
        .find(|&m| detailed(&sim, agents(&sim).node[m]) && agents(&sim).destination[m] < 0)
        .expect("an idle detailed group");
    agents_mut(&mut sim).cash[a] = -1;
    let weeks = sim
        .context()
        .services
        .database
        .config
        .merchant_bankrupt_weeks;
    let mut bankrupt = 0;
    for _ in 0..weeks {
        bankrupt += step(&mut sim)
            .iter()
            .filter(|e| {
                matches!(e, SimEventRecord::MerchantBankrupt(MerchantBankrupt { merchant, .. }) if *merchant as usize == a)
            })
            .count();
    }
    assert_eq!(bankrupt, 1);
    let t = agents(&sim);
    assert_eq!(t.home[a], t.node[a]);
    let config = &sim.context().services.database.config;
    assert_eq!(capital_start(config, 42, a as i32), t.cash[a]);
}

#[test]
fn traits_are_drawn_at_start_and_neutral_when_off() {
    let on = build_sample(&[]);
    let mut kinds = agents(&on).trait_id.clone();
    kinds.sort_unstable();
    kinds.dedup();
    assert!(kinds.len() > 5, "nine combinations, drawn at random");
    let mut off = build(&sample_with_config("key,value\nmerchants.traits,0\n"), 42);
    assert!(agents(&off).trait_id.iter().all(|&t| t == TRAIT_NEUTRAL));
    for _ in 0..54 {
        step(&mut off);
    }
    assert!(agents(&off).trait_id.iter().all(|&t| t == TRAIT_NEUTRAL));
}

#[test]
fn personality_limits_what_a_group_risks() {
    let table = MerchantTraitTable::new(&SimulationConfig::defaults());
    let cautious = usize::from(trait_of(CAUTIOUS, LOCAL));
    let aggressive = usize::from(trait_of(AGGRESSIVE, PEDDLER));
    assert!(table.spend_raw[cautious] < table.spend_raw[aggressive]);
    assert!(table.min_margin_raw[cautious] > table.min_margin_raw[aggressive]);
    assert!(table.distance_power[cautious] > table.distance_power[aggressive]);
    assert!(table.capacity_raw[aggressive] < table.capacity_raw[cautious]);
}

#[test]
fn unsold_cargo_is_sold_once_the_traders_can_pay() {
    let mut sim = build_sample(&["merchants.must_act,0"]);
    step(&mut sim);
    let db = std::sync::Arc::clone(&sim.context().services.database);
    let wheat = db
        .try_get_good("wheat", QualityGrade::Mid)
        .expect("wheat")
        .0;
    let a = (0..agents(&sim).count())
        .find(|&i| {
            let n = agents(&sim).node[i];
            detailed(&sim, n)
                && agents(&sim).destination[i] < 0
                && sim.world().get::<MarketStore>().carries(n as usize, wheat)
        })
        .expect("a group at a wheat market");
    let holder = MERCHANT_HOLDER_BASE + a as i32;
    let node = agents(&sim).node[a] as usize;
    let w = &mut sim.context_mut().world;
    w.get_mut::<CargoTable>().clear(holder);
    w.get_mut::<CargoTable>().change(holder, wheat, 10);
    // Plenty in the float: the load goes in the group's next idle week, without a trip.
    let purse = w.get::<PurseState>().node_float[node] as usize;
    w.get_mut::<PurseState>().float[purse] += 1_000_000;
    w.get_mut::<HouseholdCash>().cash[node] -= 1_000_000;
    let cash = agents(&sim).cash[a];
    step(&mut sim);
    let upkeep = db.config.merchant_upkeep[usize::from(agents(&sim).scale[a])];
    assert!(agents(&sim).cash[a] > cash - upkeep);
}

#[test]
fn cautious_groups_stay_in_their_home_region() {
    let mut sim = build_sample(&[]);
    let cautious = trait_of(CAUTIOUS, EXPEDITION);
    agents_mut(&mut sim).trait_id.fill(cautious);
    let mut trips = 0;
    for _ in 0..30 {
        step(&mut sim);
        let nodes = sim.context().services.get::<NodeTable>();
        let t = agents(&sim);
        for a in 0..t.count() {
            if t.trait_id[a] != cautious || t.destination[a] < 0 {
                continue; // a failed group's successor has its own
            }
            let (home, to) = (t.home[a], t.destination[a]);
            if nodes.level[home as usize] < WorldLevel::Region as u8
                || nodes.continent[to as usize] != nodes.continent[home as usize]
            {
                continue; // those based at a port may sail (DEC-096)
            }
            trips += 1;
            assert_eq!(
                nodes.ancestor_at(home, WorldLevel::Region),
                nodes.ancestor_at(to, WorldLevel::Region)
            );
        }
    }
    assert!(trips > 0);
}

#[test]
fn group_based_in_the_detailed_area_is_stepped_away_from_it() {
    let mut sim = build_sample(&["merchants.must_act,0", "lod.mode,0"]);
    step(&mut sim);
    let a = (0..agents(&sim).count())
        .find(|&i| detailed(&sim, agents(&sim).home[i]) && agents(&sim).destination[i] < 0)
        .expect("a group based in the detailed area");
    let nodes = sim.context().services.get::<NodeTable>();
    let home_continent = nodes.continent[agents(&sim).home[a] as usize];
    let away = (0..nodes.count() as i32)
        .find(|&n| !detailed(&sim, n) && nodes.continent[n as usize] == home_continent)
        .expect("an aggregate node");
    agents_mut(&mut sim).node[a] = away;
    let cash = agents(&sim).cash[a];
    step(&mut sim);
    assert_ne!(cash, agents(&sim).cash[a]);
}

#[test]
fn node_memory_keeps_the_last_four_nodes() {
    let mut memory = NodeMemory::EMPTY;
    assert!(!memory.knows(1));
    for n in 1..=5 {
        memory.learn(n);
    }
    assert!(!memory.knows(1), "the oldest went");
    for n in 2..=5 {
        assert!(memory.knows(n));
    }
    memory.learn(3); // already known: nothing moves
    memory.learn(6);
    assert!(!memory.knows(2));
    assert!(memory.knows(3));
}

/// Coins the merchant step moves between: groups, households, floats, banks, interest groups.
fn money(sim: &Simulation) -> i64 {
    let w = sim.world();
    let bank = w.get::<BankState>();
    agents(sim).cash.iter().sum::<i64>()
        + w.get::<HouseholdCash>().cash.iter().sum::<i64>()
        + w.get::<PurseState>().float.iter().sum::<i64>()
        + bank.reserve.iter().sum::<i64>()
        + bank.international_reserve.iter().sum::<i64>()
        + w.get::<GroupTable>().cash.iter().sum::<i64>()
}

/// C# `Rumors_ConserveMoney_AndTeachGroupsNodes`, with the money of the merchant step alone.
#[test]
fn rumors_conserve_money_and_teach_groups_nodes() {
    let mut sim = build(&sample_with_config("key,value\nmerchants.rumors,1\n"), 42);
    let mut merchants = MerchantSystem::default();
    for _ in 0..30 {
        step(&mut sim);
        let before = money(&sim);
        merchants.execute(sim.context_mut()).expect("merchants");
        assert_eq!(before, money(&sim));
    }
    assert!(
        agents(&sim).memory.iter().any(|m| m.slots[1] >= 0),
        "someone knows more than one node"
    );
}

fn guilds(sim: &Simulation) -> i64 {
    let groups = sim.world().get::<GroupTable>();
    (0..groups.count())
        .filter(|&g| groups.kind[g] == KIND_GUILD)
        .map(|g| groups.cash[g])
        .sum()
}

/// C# `MerchantInsuranceTests.Premiums_ReachTheGuilds`: only the merchants' step runs.
#[test]
fn premiums_reach_the_guilds() {
    let mut sim = build(&sample_config(&["terrain.road.danger_permille,1000"]), 1);
    let mut merchants = MerchantSystem::default();
    let cover = |sim: &Simulation| {
        sim.context()
            .services
            .get::<MerchantTripStats>()
            .spent_cover
    };
    let (mut gained, mut last) = (0, cover(&sim));
    for _ in 0..60 {
        if cover(&sim) != last {
            break;
        }
        step(&mut sim);
        last = cover(&sim);
        let before = guilds(&sim);
        merchants.execute(sim.context_mut()).expect("merchants");
        gained = guilds(&sim) - before;
    }
    assert!(cover(&sim) > last, "someone bought cover");
    assert_ne!(gained, 0, "premiums less refunds moved through the guilds");
}

#[test]
fn aggregate_flow_moves_stock_toward_the_higher_price() {
    let one = Fx::ONE_RAW;
    let (mut international, mut household_cash) = (vec![], vec![0i64; 2]);
    let (mut budget, mut float, mut imports) = (vec![0i64], vec![0i64], vec![0i64]);
    let (mut stock, mut supply, mut demand) = (vec![100, 0], vec![0, 0], vec![0, 0]);
    FlowJob {
        category_count: 1,
        flow_share_raw: Fx::from_permille(500).raw(),
        licensed_share_raw: Fx::from_permille(200).raw(),
        base_good_count: 1,
        cross_flow_raw: 0,
        continent_count: 1,
        coin_rate: &[one],
        exchange_fee_raw: 0,
        international: &mut international,
        wage_raw: 0,
        household_cash: &mut household_cash,
        detail: &[0, 0],
        continent: &[0, 0],
        capacity: &[1000, 0],
        spend_raw: &[one, one],
        min_margin_raw: &[0, 0],
        vehicle_cost_raw: &[one, one],
        vehicle_danger_raw: &[one, one],
        category_specialty: &[0, 0],
        tariff: &[0, 0],
        surcharge: &[0, 0],
        out_start: &[0, 1],
        out_count: &[1, 0],
        route_index: &[0],
        route_to: &[1],
        route_turns: &[1],
        route_cost_raw: &[one],
        route_budget: &mut budget,
        route_expected_loss: &[Fx::from_permille(100).raw()],
        cell_start: &[0, 1],
        cell_count: &[1, 1],
        cell_good: &[0, 0],
        good_category: &[0],
        good_weight: &[1],
        license_row: &[0, 0],
        licensed: &[0],
        price: &[10, 30],
        node_float: &[0, 0],
        float: &mut float,
        imports: &mut imports,
        stock: &mut stock,
        supply: &mut supply,
        demand: &mut demand,
    }
    .execute();
    // margin 30 - 10 - 1 = 19; half the stock times 19/30 = 31 units, 10% (3) lost on the way.
    assert_eq!(stock[0], 69);
    assert_eq!(supply[0], 31);
    assert_eq!(demand[0], 31);
    assert_eq!(supply[1], 28);
}
