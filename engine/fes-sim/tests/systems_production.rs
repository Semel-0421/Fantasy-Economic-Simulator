//! `agents.production` against the recorded C# turns, plus unit tests ported from the C#
//! `ProductionTests` (only the systems ported so far run in these worlds).
use fes_core::testing::fixtures_dir;
use fes_core::{Fx, SimDetail};
use fes_core::{GoodId, IndustryTier};
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_sim::agents::cargo::warehouse_holder;
use fes_sim::agents::facilities::{OWNER_NPC, OWNER_PLAYER};
use fes_sim::agents::production::lines::{build_lines, Lines};
use fes_sim::agents::production::FacilityBankrupt;
use fes_sim::agents::{CargoTable, DemandForecast, DevelopmentState, FacilityTable, LodState};
use fes_sim::economy::MarketFlows;
use fes_sim::economy::MarketStore;
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::{NodeSpecialties, NodeTable};
use fes_sim::{Simulation, SimulationBuilder};
use std::path::PathBuf;
use std::sync::{Arc, Mutex};

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const PORTED: [&str; 4] = [
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
    "agents.production",
];

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

fn definitions() -> (GameDatabase, MapDef) {
    let source = FileDataSource::new(data_dir());
    (
        GameDatabase::load(&source).expect("database"),
        MapDef::load(&source).expect("map"),
    )
}

#[test]
fn production_matches_on_every_sampled_turn() {
    let (db, map) = definitions();
    for t in TURNS {
        let path = fixtures_dir().join(format!(
            "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
        ));
        let step = StepFile::read(&path).expect("step file");
        let report = replay_turn(&step, &db, &map, &PORTED).expect("replay");
        assert_eq!(report.first_difference(), None, "turn {t}");
        for id in PORTED {
            let label = format!("system:{id}");
            assert!(
                report
                    .steps
                    .iter()
                    .any(|s| matches!(s, StepResult::Equal { label: l } if *l == label)),
                "turn {t}: {label} did not run in Rust"
            );
        }
    }
}

/// The shipped data with some config keys replaced (C# `TestWorlds.SampleWith`).
struct With {
    inner: FileDataSource,
    config: Vec<(String, String)>,
}

impl DataSource for With {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        let text = self.inner.read_text(name)?;
        if name != CONFIG_FILE || self.config.is_empty() {
            return Ok(text);
        }
        let mut lines: Vec<String> = text
            .lines()
            .filter(|l| {
                !self
                    .config
                    .iter()
                    .any(|(k, _)| l.starts_with(&format!("{k},")))
            })
            .map(str::to_string)
            .collect();
        lines.extend(self.config.iter().map(|(k, v)| format!("{k},{v}")));
        Ok(lines.join("\n"))
    }
}

fn sim_with(config: &[(&str, &str)]) -> Simulation {
    let source = With {
        inner: FileDataSource::new(data_dir()),
        config: config
            .iter()
            .map(|(k, v)| (k.to_string(), v.to_string()))
            .collect(),
    };
    SimulationBuilder::from_data(&source, 42)
        .expect("builder")
        .build()
        .expect("build")
}

fn facilities_of(sim: &Simulation, tier: IndustryTier) -> Vec<usize> {
    let db = &sim.context().services.database;
    let facilities = sim.world().get::<FacilityTable>();
    (0..facilities.count())
        .filter(|&f| db.facilities[facilities.def_index[f] as usize].tier == tier)
        .collect()
}

fn start_cash(sim: &Simulation, tier: IndustryTier) -> i64 {
    sim.context().services.database.config.facility_start_cash[tier as usize]
}

fn upkeep(sim: &Simulation, tier: IndustryTier) -> i64 {
    sim.context().services.database.config.facility_upkeep[tier as usize]
}

fn collect_bankruptcies(sim: &mut Simulation) -> Arc<Mutex<Vec<FacilityBankrupt>>> {
    let seen = Arc::new(Mutex::new(Vec::new()));
    let into = Arc::clone(&seen);
    sim.context_mut()
        .events
        .subscribe::<FacilityBankrupt>(move |e, _| {
            into.lock().unwrap().push(*e);
            Ok(())
        });
    seen
}

#[test]
fn facilities_start_with_their_tiers_cash() {
    let sim = sim_with(&[]);
    let db = &sim.context().services.database;
    let facilities = sim.world().get::<FacilityTable>();
    for f in 0..facilities.count() {
        let tier = db.facilities[facilities.def_index[f] as usize].tier as usize;
        assert_eq!(facilities.cash[f], db.config.facility_start_cash[tier]);
    }
}

#[test]
fn npc_output_is_sold_at_the_current_price() {
    let mut sim = sim_with(&[]);
    let start = start_cash(&sim, IndustryTier::Primary);
    sim.step().unwrap();
    let facilities = sim.world().get::<FacilityTable>();
    for f in facilities_of(&sim, IndustryTier::Primary) {
        assert!(facilities.cash[f] > start, "facility {f}");
    }
}

#[test]
fn facility_below_zero_cash_goes_bankrupt_after_the_grace_weeks() {
    let mut sim = sim_with(&[]);
    let bakeries = facilities_of(&sim, IndustryTier::Tertiary);
    for &f in &bakeries {
        sim.context_mut().world.get_mut::<FacilityTable>().cash[f] = 0;
    }
    let weeks = sim
        .context()
        .services
        .database
        .config
        .facility_bankrupt_weeks;
    let seen = collect_bankruptcies(&mut sim);
    for _ in 0..weeks - 1 {
        sim.step().unwrap();
    }
    assert!(seen.lock().unwrap().is_empty());
    let facilities = sim.world().get::<FacilityTable>();
    assert!(bakeries
        .iter()
        .all(|&f| facilities.cash[f] < 0 && facilities.negative_cash_weeks[f] == weeks - 1));

    sim.step().unwrap();
    let seen = seen.lock().unwrap();
    let mut got: Vec<usize> = seen.iter().map(|b| b.facility as usize).collect();
    got.sort_unstable();
    assert_eq!(got, bakeries);
    assert!(seen
        .iter()
        .all(|b| !b.was_player_owned && b.warehouse_claim_until == -1));
    let start = start_cash(&sim, IndustryTier::Tertiary);
    let facilities = sim.world().get::<FacilityTable>();
    assert!(bakeries
        .iter()
        .all(|&f| facilities.cash[f] == start && facilities.negative_cash_weeks[f] == 0));
}

#[test]
fn a_new_operators_bankruptcy_keeps_the_players_claim_on_the_warehouse() {
    let mut sim = sim_with(&[]);
    let bakery = facilities_of(&sim, IndustryTier::Tertiary)[0];
    let until = sim.turn() + 100;
    let weeks = sim
        .context()
        .services
        .database
        .config
        .facility_bankrupt_weeks;
    let facilities = sim.context_mut().world.get_mut::<FacilityTable>();
    facilities.warehouse_claim_until[bakery] = until;
    facilities.owner[bakery] = OWNER_NPC;
    facilities.cash[bakery] = -1_000_000_000;
    facilities.negative_cash_weeks[bakery] = weeks;
    let seen = collect_bankruptcies(&mut sim);
    sim.step().unwrap();
    let mine: Vec<FacilityBankrupt> = seen
        .lock()
        .unwrap()
        .iter()
        .filter(|b| b.facility as usize == bakery)
        .copied()
        .collect();
    assert_eq!(mine.len(), 1);
    assert_eq!(mine[0].warehouse_claim_until, until);
    assert_eq!(
        sim.world().get::<FacilityTable>().warehouse_claim_until[bakery],
        until
    );
}

#[test]
fn a_lost_player_facility_goes_to_a_family_and_its_warehouse_is_cleared_after_the_claim() {
    let mut sim = sim_with(&[]);
    let bakery = facilities_of(&sim, IndustryTier::Tertiary)[0];
    let weeks = sim
        .context()
        .services
        .database
        .config
        .facility_bankrupt_weeks;
    let lost_weeks = sim.context().services.database.config.lost_warehouse_weeks;
    let facilities = sim.context_mut().world.get_mut::<FacilityTable>();
    facilities.owner[bakery] = OWNER_PLAYER;
    facilities.cash[bakery] = -1_000_000_000;
    facilities.negative_cash_weeks[bakery] = weeks;
    let holder = warehouse_holder(bakery as i32);
    sim.context_mut()
        .world
        .get_mut::<CargoTable>()
        .change(holder, 0, 3);
    let seen = collect_bankruptcies(&mut sim);
    sim.step().unwrap();
    let b = *seen
        .lock()
        .unwrap()
        .iter()
        .find(|b| b.facility as usize == bakery)
        .expect("bankrupt");
    assert!(b.was_player_owned);
    assert_eq!(b.warehouse_claim_until, b.turn + lost_weeks);
    let facilities = sim.world().get::<FacilityTable>();
    assert_eq!(facilities.owner[bakery], OWNER_NPC);
    assert!(facilities.group[bakery] >= 0);
    assert_eq!(facilities.repurposable[bakery], 1);
    assert_eq!(sim.world().get::<CargoTable>().quantity_of(holder, 0), 3);
    while sim.turn() <= b.warehouse_claim_until {
        sim.step().unwrap();
    }
    assert_eq!(sim.world().get::<CargoTable>().quantity_of(holder, 0), 0);
    assert_eq!(
        sim.world().get::<FacilityTable>().warehouse_claim_until[bakery],
        -1
    );
}

/// A primary facility with a single output (C# `FarmForSale`), handed to the player.
fn players_farm(sim: &mut Simulation) -> usize {
    let db = Arc::clone(&sim.context().services.database);
    let farm = facilities_of(sim, IndustryTier::Primary)
        .into_iter()
        .find(|&f| {
            let def = &db.facilities[sim.world().get::<FacilityTable>().def_index[f] as usize];
            !def.state_owned && db.recipes[def.recipe].outputs.len() == 1
        })
        .expect("a farm");
    sim.context_mut().world.get_mut::<FacilityTable>().owner[farm] = OWNER_PLAYER;
    farm
}

#[test]
fn player_output_goes_to_the_warehouse() {
    let mut sim = sim_with(&[("facility.output_wage_permille", "0")]);
    let farm = players_farm(&mut sim);
    let cash = sim.world().get::<FacilityTable>().cash[farm];
    sim.step().unwrap();
    let db = &sim.context().services.database;
    let cargo = sim.world().get::<CargoTable>();
    let rows: Vec<usize> = (0..cargo.count())
        .filter(|&r| cargo.holder[r] == warehouse_holder(farm as i32))
        .collect();
    let facilities = sim.world().get::<FacilityTable>();
    let def = &db.facilities[facilities.def_index[farm] as usize];
    let output = db.recipes[def.recipe].outputs[0].good;
    assert_eq!(rows.len(), 1);
    // A specialty may come out a grade higher.
    assert_eq!(
        GoodId(cargo.good[rows[0]]).base_index(),
        output.base_index()
    );
    assert!(cargo.quantity[rows[0]] > 0);
    // Nothing sold: the warehouse had room.
    assert_eq!(
        facilities.cash[farm],
        cash - upkeep(&sim, IndustryTier::Primary)
    );
}

#[test]
fn player_output_overflow_is_sold() {
    let mut sim = sim_with(&[
        ("cargo.warehouse.max_weight", "20"),
        ("cargo.warehouse.max_size", "20"),
    ]);
    let farm = players_farm(&mut sim);
    let cash = sim.world().get::<FacilityTable>().cash[farm];
    sim.step().unwrap();
    let markets = sim.world().get::<MarketStore>();
    let (weight, size) = sim.world().get::<CargoTable>().load_of(
        warehouse_holder(farm as i32),
        &markets.good_weight,
        &markets.good_size,
    );
    assert!(weight <= 20 && size <= 20 && weight > 0);
    assert!(
        sim.world().get::<FacilityTable>().cash[farm] > cash - upkeep(&sim, IndustryTier::Primary)
    );
}

/// A world running only lod, world events, the fold and production (plus pricing when asked),
/// with every facility but `keep` idled at level 0 so the produced column is `keep`'s alone.
fn lod_world(mode: &str, with_pricing: bool) -> Simulation {
    let source = With {
        inner: FileDataSource::new(data_dir()),
        config: vec![("lod.mode".to_string(), mode.to_string())],
    };
    let mut b = SimulationBuilder::from_data(&source, 42)
        .expect("builder")
        .remove_system("agents.household_spending")
        .remove_system("agents.home_surplus")
        .remove_system("agents.households")
        .remove_system("economy.currency");
    if !with_pricing {
        b = b.remove_system("economy.pricing");
    }
    b.build().expect("build")
}

fn lines_now(sim: &Simulation) -> Lines {
    let ctx = sim.context();
    let w = sim.world();
    build_lines(
        &ctx.services.database,
        ctx.services.get::<NodeTable>(),
        w.get::<MarketStore>(),
        ctx.services.get::<MarketFlows>(),
        ctx.services.get::<NodeSpecialties>(),
        w.get::<FacilityTable>(),
        w.get::<DevelopmentState>(),
    )
}

/// The first output line of `f`.
fn first_output(lines: &Lines, f: usize) -> usize {
    (lines.start[f]..lines.start[f + 1])
        .find(|&k| lines.is_input[k] == 0)
        .expect("an output line")
}

/// An NPC facility without aid lines, whose lines all have a market, on a node `lod` leaves
/// aggregate.
fn plain_aggregate_facility(sim: &Simulation, lines: &Lines) -> Option<usize> {
    let facilities = sim.world().get::<FacilityTable>();
    let detail = &sim.context().services.get::<LodState>().detail;
    (0..facilities.count()).find(|&f| {
        let mut r = lines.start[f]..lines.start[f + 1];
        facilities.owner[f] == OWNER_NPC
            && detail[facilities.node[f] as usize] == SimDetail::Aggregate as u8
            && r.clone().any(|k| lines.is_input[k] == 0)
            && r.all(|k| lines.is_input[k] <= 1 && lines.cell[k] >= 0)
    })
}

/// C# `ProductionJob.PlannedOutput` (ProductionJobs.cs:125-135, aggregate branch: no `NextSpread`)
/// and `MakeOutput`/`Emit` (ProductionJobs.cs:236-258, `upgraded = MulIntRaw(quantity,
/// GradeUpChanceRaw)` when not detailed): what `f` adds to each cell this week.
fn expected_aggregate_output(sim: &Simulation, lines: &Lines, f: usize) -> Vec<(usize, i64)> {
    let ctx = sim.context();
    let c = &ctx.services.database.config;
    let facilities = sim.world().get::<FacilityTable>();
    let plan = &sim.world().get::<DemandForecast>().plan;
    let mul = &ctx.services.get::<MarketFlows>().multipliers;
    let level = i64::from(facilities.level[f]);
    let r = lines.start[f]..lines.start[f + 1];
    let mut output = Fx::from_raw(facilities.efficiency[f])
        * Fx::from_int(level)
        * (Fx::ONE + c.facility_expansion_bonus * Fx::from_int(level - 1));
    output = output * Fx::from_raw(plan[lines.cell[first_output(lines, f)] as usize]);
    let mut out = Vec::new();
    for k in r.filter(|&k| lines.is_input[k] == 0) {
        let quantity = Fx::mul_int_raw(i64::from(lines.quantity[k]), output.raw());
        let quantity = Fx::mul_int_raw(quantity, lines.output_mul[k]);
        let mut upgraded = 0;
        if lines.up_good[k] >= 0 {
            upgraded = Fx::mul_int_raw(quantity, c.specialty_grade_up_chance.raw());
            if lines.up_cell[k] >= 0 {
                let q = Fx::mul_int_raw(upgraded, mul[lines.up_modifier[k] as usize]);
                out.push((
                    lines.up_cell[k] as usize,
                    i64::from(MarketStore::clamp_to_non_negative_int(q)),
                ));
            }
        }
        if lines.cell[k] >= 0 {
            let q = Fx::mul_int_raw(quantity - upgraded, mul[lines.modifier[k] as usize]);
            out.push((
                lines.cell[k] as usize,
                i64::from(MarketStore::clamp_to_non_negative_int(q)),
            ));
        }
    }
    out
}

/// Gives `keep` level 2 (so the expansion bonus counts), ample cash and every input in stock, idles
/// every other facility (level 0 makes the planned output zero), runs one turn and returns what the
/// turn added to each cell's produced count.
fn produce_alone(sim: &mut Simulation, lines: &Lines, keep: usize) -> Vec<i64> {
    let world = &mut sim.context_mut().world;
    let facilities = world.get_mut::<FacilityTable>();
    for f in 0..facilities.count() {
        facilities.level[f] = if f == keep { 2 } else { 0 };
    }
    if keep < facilities.count() {
        facilities.cash[keep] = 1 << 40;
        let stock = &mut world.get_mut::<MarketStore>().stock;
        for k in lines.start[keep]..lines.start[keep + 1] {
            if lines.is_input[k] != 0 {
                stock[lines.cell[k] as usize] = 1 << 30;
            }
        }
    }
    let before = sim.world().get::<DemandForecast>().produced.clone();
    sim.step().unwrap();
    let after = &sim.world().get::<DemandForecast>().produced;
    after.iter().zip(&before).map(|(a, b)| a - b).collect()
}

fn check_aggregate_output(mode: &str) {
    let mut sim = lod_world(mode, false);
    // The first step evaluates lod and folds; the facility and lines are chosen from its result.
    sim.step().unwrap();
    let lines = lines_now(&sim);
    let f = plain_aggregate_facility(&sim, &lines).expect("a plain aggregate facility");
    sim.context_mut().world.get_mut::<FacilityTable>().level[f] = 2;
    let mut want = vec![0i64; sim.world().get::<DemandForecast>().produced.len()];
    for (cell, q) in expected_aggregate_output(&sim, &lines, f) {
        want[cell] += q;
    }
    assert!(want.iter().any(|&q| q > 0), "facility {f} makes nothing");
    let got = produce_alone(&mut sim, &lines, f);
    assert_eq!(got, want, "lod.mode {mode}, facility {f}");
}

#[test]
fn aggregate_output_matches_the_csharp_expected_value_in_lod_mode_2() {
    check_aggregate_output("2");
}

#[test]
fn aggregate_output_matches_the_csharp_expected_value_in_lod_mode_0() {
    check_aggregate_output("0");
}

#[test]
fn pricing_consumes_the_aggregate_output() {
    let run = |idle_all: bool| {
        let mut sim = lod_world("2", true);
        sim.step().unwrap();
        let lines = lines_now(&sim);
        let f = plain_aggregate_facility(&sim, &lines).expect("a plain aggregate facility");
        let cell = lines.cell[first_output(&lines, f)] as usize;
        let keep = if idle_all { usize::MAX } else { f };
        let produced = produce_alone(&mut sim, &lines, keep);
        let m = sim.world().get::<MarketStore>();
        (
            cell,
            produced[cell],
            m.stock[cell],
            m.price[cell],
            m.supply[cell],
        )
    };
    let (cell, made, stock, price, supply) = run(false);
    let (idle_cell, idle_made, idle_stock, idle_price, _) = run(true);
    assert_eq!(cell, idle_cell);
    assert!(made > 0 && idle_made == 0);
    // Pricing settles the week's supply into stock and price; the aggregate output reached it.
    assert_eq!(supply, 0, "pricing left the supply unconsumed");
    assert!(
        (stock, price) != (idle_stock, idle_price),
        "pricing ignored {made} units of aggregate output"
    );
}
