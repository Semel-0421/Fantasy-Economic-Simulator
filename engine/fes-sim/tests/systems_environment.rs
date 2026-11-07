//! `agents.lod`, `economy.world_events` and `economy.modifier_fold` against the recorded C# turns,
//! plus unit tests ported from the C# suites (`EconomyTests`, `SimulationTests`, `MerchantTests`).
use fes_core::testing::fixtures_dir;
use fes_core::{Fx, WorldLevel};
use fes_data::database::{GameDatabase, CONFIG_FILE, EVENTS_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_sim::agents::lod_system::evaluate_lod;
use fes_sim::agents::{FacilityTable, LodState, PlayerState};
use fes_sim::economy::modifier_fold::fold_multipliers;
use fes_sim::economy::{MarketFlows, MarketModifier, MarketModifierTable};
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use fes_sim::world::NodeTable;
use fes_sim::{Simulation, SimulationBuilder};
use std::path::PathBuf;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];
const PORTED: [&str; 3] = [
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
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
fn environment_systems_match_on_every_sampled_turn() {
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

/// The shipped data with some files replaced (C# `TestWorlds.SampleWith`).
struct With {
    inner: FileDataSource,
    files: Vec<(&'static str, String)>,
}

impl DataSource for With {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        match self.files.iter().find(|(n, _)| *n == name) {
            Some((_, text)) => Ok(text.clone()),
            None => self.inner.read_text(name),
        }
    }
}

fn sim_with(files: Vec<(&'static str, String)>, seed: u64) -> Simulation {
    let source = With {
        inner: FileDataSource::new(data_dir()),
        files,
    };
    SimulationBuilder::from_data(&source, seed)
        .expect("builder")
        .build()
        .expect("build")
}

const EVENTS_HEADER: &str = "key,level,category,target,mul_permille,duration,chance_permille\n";

fn modifiers(sim: &Simulation) -> Vec<MarketModifier> {
    sim.world().get::<MarketModifierTable>().active().to_vec()
}

#[test]
fn world_events_add_modifiers_that_expire() {
    let events = format!("{EVENTS_HEADER}quake,Continent,*,Supply,500,2,1000\n");
    let mut sim = sim_with(vec![(EVENTS_FILE, events)], 1);
    let continents = sim.context().services.get::<NodeTable>().continent_count() as usize;
    sim.step().unwrap();
    assert_eq!(modifiers(&sim).len(), continents);
    sim.step().unwrap();
    assert_eq!(modifiers(&sim).len(), 2 * continents);
    sim.step().unwrap(); // the first wave expires at turn 2
    assert_eq!(modifiers(&sim).len(), 2 * continents);
    let ms = modifiers(&sim);
    assert!(ms.iter().all(|m| m.expires_turn > 2));
    assert!(ms.iter().all(|m| m.multiplier == Fx::from_raw(32768)));
}

#[test]
fn modifier_fold_runs_after_events_and_multiplies_the_subtree() {
    let events = format!("{EVENTS_HEADER}quake,Continent,*,Supply,500,2,1000\n");
    let mut sim = sim_with(vec![(EVENTS_FILE, events)], 1);
    sim.step().unwrap();
    let flows = sim.context().services.get::<MarketFlows>();
    let nodes = sim.context().services.get::<NodeTable>();
    // Every node sits under a continent with a 0.5 supply modifier; demand and value stay at one.
    for n in 0..nodes.count() {
        for c in 0..flows.category_count {
            assert_eq!(flows.multipliers[flows.multiplier_index(n, c, 0)], 32768);
            assert_eq!(flows.multipliers[flows.multiplier_index(n, c, 1)], 65536);
            assert_eq!(flows.multipliers[flows.multiplier_index(n, c, 2)], 65536);
        }
    }
}

#[test]
fn fold_multiplies_in_list_order_with_round_shift_and_category_filter() {
    let (db, map) = definitions();
    let sim = Simulation::load(
        db,
        map,
        &std::fs::read(fixtures_dir().join("runs/Default_Seed1_ThirtyYears/start.save")).unwrap(),
    )
    .unwrap();
    let nodes = sim.context().services.get::<NodeTable>();
    let region = nodes.level_start(WorldLevel::Region);
    let at = |node: i32, category: i32, target: fes_data::defs::ModifierTarget, raw: i64| {
        MarketModifier {
            node,
            category,
            target,
            multiplier: Fx::from_raw(raw),
            expires_turn: 10,
            source_event: -1,
        }
    };
    use fes_data::defs::ModifierTarget::{Demand, Supply};
    let mods = [
        at(region, 1, Demand, 98_304),
        at(region, 1, Demand, 98_305),
        at(region, -1, Supply, 70_000),
    ];
    let categories = 3;
    let out = fold_multipliers(categories, nodes, &mods);
    let idx = |n: i32, c: usize, t: usize| (n as usize * categories + c) * 3 + t;
    // 65536 * 98304 >> 16 = 98304; 98304 * 98305 rounds half up at the Q16 shift.
    let second = Fx::round_shift(98_304i64 * 98_305);
    assert_eq!(out[idx(region, 1, 1)], second);
    assert_eq!(out[idx(region, 0, 1)], 65_536);
    for c in 0..categories {
        assert_eq!(out[idx(region, c, 0)], 70_000);
    }
    let mut subtree = Vec::new();
    nodes.collect_subtree(region, &mut subtree);
    for n in 0..nodes.count() as i32 {
        let inside = subtree.contains(&n);
        assert_eq!(out[idx(n, 0, 0)], if inside { 70_000 } else { 65_536 });
    }
}

fn lod_sim(mode: i32) -> Simulation {
    let config = format!("key,value\nlod.mode,{mode}\n");
    let base = std::fs::read_to_string(data_dir().join(CONFIG_FILE)).unwrap();
    let mut kept: Vec<&str> = base
        .lines()
        .filter(|l| !l.starts_with("lod.mode,"))
        .collect();
    kept.push(config.lines().nth(1).unwrap());
    sim_with(vec![(CONFIG_FILE, kept.join("\n"))], 1)
}

fn detail(sim: &Simulation) -> Vec<u8> {
    sim.context().services.get::<LodState>().detail.clone()
}

#[test]
fn lod_modes_one_and_two_set_every_node() {
    let mut sim = lod_sim(1);
    sim.step().unwrap();
    assert!(detail(&sim).iter().all(|&d| d == 1));
    let mut sim = lod_sim(2);
    sim.step().unwrap();
    assert!(detail(&sim).iter().all(|&d| d == 0));
}

#[test]
fn lod_mode_zero_is_the_players_region_plus_owned_facility_nodes() {
    let mut sim = lod_sim(0);
    sim.step().unwrap();
    let nodes = sim.context().services.get::<NodeTable>();
    let player = sim.world().get::<PlayerState>();
    let facilities = sim.world().get::<FacilityTable>();
    let region = nodes.ancestor_at(player.node, WorldLevel::Region);
    let d = detail(&sim);
    assert_eq!(d[player.node as usize], 1);
    let far = (0..facilities.count())
        .find(|&f| !nodes.is_ancestor_or_self(region, facilities.node[f]))
        .expect("a facility outside the player's region");
    let far_node = facilities.node[far];
    assert_eq!(d[far_node as usize], 0);
    for n in 0..nodes.count() as i32 {
        assert_eq!(
            d[n as usize] == 1,
            nodes.is_ancestor_or_self(region, n),
            "node {n}"
        );
    }

    // Buying the facility makes its node detailed; the policy sees ownership changes at once.
    let ctx = sim.context_mut();
    ctx.world.get_mut::<FacilityTable>().owner[far] = 0;
    evaluate_lod(ctx);
    assert_eq!(
        sim.context().services.get::<LodState>().detail[far_node as usize],
        1
    );
}
