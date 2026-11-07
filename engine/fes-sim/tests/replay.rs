use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_save::RawSection;
use fes_sim::agents::lod_system::evaluate_lod;
use fes_sim::agents::LodState;
use fes_sim::economy::modifier_fold::rebuild_flows;
use fes_sim::economy::MarketFlows;
use fes_sim::replay::{ReplayReport, StepFile, StepResult};
use fes_sim::{SimClock, SimContext, SimError, SimPhase, System};
use std::path::PathBuf;

const TURNS: [i32; 8] = [0, 1, 3, 51, 52, 53, 103, 104];

fn step_path(t: i32) -> PathBuf {
    fixtures_dir().join(format!(
        "runs/Default_Seed1_ThirtyYears/steps/turn_{t}.bin.gz"
    ))
}

/// The `world_hash` of `turns.json`'s entry for clock turn `turn`.
fn recorded_hash(turn: i32) -> u64 {
    let json = fes_core::testing::fixture_json("runs/Default_Seed1_ThirtyYears/turns.json");
    let entry = json["turns"]
        .as_array()
        .unwrap()
        .iter()
        .find(|e| e["turn"] == turn)
        .expect("turn entry");
    entry["world_hash"].as_str().unwrap().parse().unwrap()
}

fn definitions() -> (GameDatabase, MapDef) {
    let dir = PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ));
    let source = FileDataSource::new(dir);
    (
        GameDatabase::load(&source).expect("database"),
        MapDef::load(&source).expect("map"),
    )
}

#[test]
fn step_files_parse() {
    for t in TURNS {
        let step = StepFile::read(&step_path(t)).expect("parse");
        assert_eq!(step.turn, t);
        assert!(!step.save.is_empty());
        let labels: Vec<&str> = step.records.iter().map(|r| r.label.as_str()).collect();
        assert_eq!(labels.last(), Some(&"clock"), "turn {t}");
        assert!(
            labels.iter().any(|l| l.starts_with("system:")),
            "turn {t}: no system records"
        );
        assert!(
            labels.iter().filter(|l| l.starts_with("dispatch:")).count() >= 1,
            "turn {t}: no dispatch records"
        );
        // Every record carries a hash; the clock record changes the core.clock section.
        let clock = step.records.last().unwrap();
        assert!(clock.sections.iter().any(|s| s.key == "core.clock"));
        assert_eq!(clock.world_hash, recorded_hash(t + 1), "turn {t}");
        // 40 systems + 9 phase dispatches + the clock.
        assert_eq!(step.records.len(), 50, "turn {t}");
    }
}

#[test]
fn replay_with_nothing_ported_reaches_every_recorded_hash() {
    let (db, map) = definitions();
    for t in TURNS {
        let step = StepFile::read(&step_path(t)).unwrap();
        let report: ReplayReport =
            fes_sim::replay::replay_turn(&step, &db, &map, &[]).expect("replay");
        assert_eq!(report.steps.len(), step.records.len(), "turn {t}");
        assert!(report.first_difference().is_none(), "turn {t}");
    }
}

struct Wrong;

impl System for Wrong {
    fn id(&self) -> &'static str {
        "core.commands"
    }
    fn phase(&self) -> SimPhase {
        SimPhase::Commands
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        ctx.world.get_mut::<SimClock>().seed ^= 1;
        Ok(())
    }
}

#[test]
fn a_wrong_fake_system_is_reported_with_offset() {
    let (db, map) = definitions();
    let step = StepFile::read(&step_path(1)).unwrap();
    let report = fes_sim::replay::replay_turn_with(&step, &db, &map, &["core.commands"], |b| {
        b.replace_system(Box::new(Wrong))
    })
    .expect("replay");
    match report.first_difference() {
        Some(StepResult::Different { label, key, offset }) => {
            assert_eq!(label, "system:core.commands");
            assert_eq!(key, "core.clock");
            assert_eq!(*offset, 4);
        }
        other => panic!("expected a difference, got {other:?}"),
    }
}

/// A fold that leaves one multiplier off by one raw unit; correct on load.
struct WrongFold;

impl System for WrongFold {
    fn id(&self) -> &'static str {
        "economy.modifier_fold"
    }
    fn phase(&self) -> SimPhase {
        SimPhase::Environment
    }
    fn order(&self) -> i32 {
        fes_sim::sim_order::LATE
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        rebuild_flows(ctx);
        ctx.services.get_mut::<MarketFlows>().multipliers[3] ^= 1;
        Ok(())
    }
    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        rebuild_flows(ctx);
        Ok(())
    }
}

/// A lod system that flips one node's detail; correct on load.
struct WrongLod;

impl System for WrongLod {
    fn id(&self) -> &'static str {
        "agents.lod"
    }
    fn phase(&self) -> SimPhase {
        SimPhase::Lod
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        evaluate_lod(ctx);
        ctx.services.get_mut::<LodState>().detail[5] ^= 1;
        Ok(())
    }
    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        evaluate_lod(ctx);
        Ok(())
    }
}

fn first_difference_with(system: Box<dyn System>, ported: &[&str]) -> Option<StepResult> {
    let (db, map) = definitions();
    let step = StepFile::read(&step_path(1)).unwrap();
    let report =
        fes_sim::replay::replay_turn_with(&step, &db, &map, ported, |b| b.replace_system(system))
            .expect("replay");
    report.first_difference().cloned()
}

#[test]
fn a_wrong_service_is_reported_against_its_rebuild() {
    let wrong = |label: &str, key: &str, offset: usize| StepResult::Different {
        label: label.to_string(),
        key: key.to_string(),
        offset,
    };
    assert_eq!(
        first_difference_with(Box::new(WrongFold), &["economy.modifier_fold"]),
        Some(wrong(
            "system:economy.modifier_fold",
            "service:MarketFlows.multipliers",
            3
        ))
    );
    assert_eq!(
        first_difference_with(Box::new(WrongLod), &["agents.lod"]),
        Some(wrong("system:agents.lod", "service:LodState.detail", 5))
    );
}

#[test]
fn a_recorded_section_rust_lacks_is_reported() {
    let (db, map) = definitions();
    let mut step = StepFile::read(&step_path(1)).unwrap();
    let record = step
        .records
        .iter_mut()
        .find(|r| r.label.starts_with("dispatch:"))
        .unwrap();
    record.sections.push(RawSection {
        key: "zz.only_in_the_record".to_string(),
        version: 1,
        bytes: vec![1, 2, 3],
    });
    let label = record.label.clone();
    let report = fes_sim::replay::replay_turn(&step, &db, &map, &[]).expect("replay");
    assert_eq!(
        report.first_difference(),
        Some(&StepResult::Different {
            label,
            key: "zz.only_in_the_record".to_string(),
            offset: 0
        })
    );
}
