//! Step-replay harness (behind the `testing` feature): replays one recorded C# turn
//! (`steps/turn_<t>.bin.gz`) record by record, running ported Rust systems against the recorded
//! state and applying the recorded changes for everything not yet ported.
use crate::error::SimError;
use crate::simulation::Simulation;
use crate::SimulationBuilder;
use fes_core::io::DotNetReader;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_save::{read_save, RawSection};
use std::collections::BTreeMap;
use std::io::Read;
use std::path::Path;
use std::time::{Duration, Instant};

const MAGIC: &[u8; 8] = b"FESSTEP1";

/// One recorded step: the label, the world hash after it and the sections whose bytes changed.
#[derive(Clone, Debug)]
pub struct StepRecord {
    pub label: String,
    pub world_hash: u64,
    pub sections: Vec<RawSection>,
}

/// A recorded turn: the save before it and every step's effect.
#[derive(Clone, Debug)]
pub struct StepFile {
    pub turn: i32,
    pub save: Vec<u8>,
    pub records: Vec<StepRecord>,
}

fn invalid(msg: impl Into<String>) -> SimError {
    SimError::Invalid(msg.into())
}

impl StepFile {
    pub fn read(path: &Path) -> Result<StepFile, SimError> {
        let file =
            std::fs::File::open(path).map_err(|e| invalid(format!("{}: {e}", path.display())))?;
        let mut bytes = Vec::new();
        flate2::read::GzDecoder::new(file)
            .read_to_end(&mut bytes)
            .map_err(|e| invalid(format!("{}: {e}", path.display())))?;
        Self::parse(&bytes)
    }

    pub fn parse(bytes: &[u8]) -> Result<StepFile, SimError> {
        let mut r = DotNetReader::new(bytes);
        if r.read_bytes(MAGIC.len())? != MAGIC {
            return Err(invalid("not a step file (bad magic)"));
        }
        let turn = r.read_i32()?;
        let save_len = len(r.read_i32()?)?;
        let save = r.read_bytes(save_len)?.to_vec();
        let count = len(r.read_i32()?)?;
        let mut records = Vec::new();
        for _ in 0..count {
            records.push(read_record(&mut r)?);
        }
        if r.remaining() != 0 {
            return Err(invalid(format!("{} trailing bytes", r.remaining())));
        }
        Ok(StepFile {
            turn,
            save,
            records,
        })
    }
}

fn len(n: i32) -> Result<usize, SimError> {
    usize::try_from(n).map_err(|_| invalid("negative length in step file"))
}

fn read_record(r: &mut DotNetReader) -> Result<StepRecord, SimError> {
    let label = r.read_string()?;
    let world_hash = r.read_u64()?;
    let n = len(r.read_i32()?)?;
    let mut sections = Vec::new();
    for _ in 0..n {
        let key = r.read_string()?;
        let version = r.read_i32()?;
        let size = len(r.read_i32()?)?;
        sections.push(RawSection {
            key,
            version,
            bytes: r.read_bytes(size)?.to_vec(),
        });
    }
    Ok(StepRecord {
        label,
        world_hash,
        sections,
    })
}

/// What one record's replay found.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum StepResult {
    /// The recorded changes were applied without running Rust code.
    Applied { label: String },
    /// Rust ran and every section equals the recorded state.
    Equal { label: String },
    /// Rust ran and `key` differs from the recorded state, first at byte `offset`.
    Different {
        label: String,
        key: String,
        offset: usize,
    },
}

#[derive(Clone, Debug, Default)]
pub struct ReplayReport {
    pub steps: Vec<StepResult>,
    /// Wall time of each ported system's run (id, time), in record order; excludes the compare.
    pub timings: Vec<(String, Duration)>,
}

impl ReplayReport {
    pub fn first_difference(&self) -> Option<&StepResult> {
        self.steps
            .iter()
            .find(|s| matches!(s, StepResult::Different { .. }))
    }
}

/// Replays `step` with the default systems. `ported` lists the system ids (without the `system:`
/// prefix) that run in Rust; every `dispatch:` and `clock` record also runs in Rust.
pub fn replay_turn(
    step: &StepFile,
    db: &GameDatabase,
    map: &MapDef,
    ported: &[&str],
) -> Result<ReplayReport, SimError> {
    replay_turn_with(step, db, map, ported, |b| b)
}

/// Like `replay_turn`, with `configure` applied to the simulation builder (to swap in test systems).
pub fn replay_turn_with(
    step: &StepFile,
    db: &GameDatabase,
    map: &MapDef,
    ported: &[&str],
    configure: impl FnOnce(SimulationBuilder) -> SimulationBuilder,
) -> Result<ReplayReport, SimError> {
    let mut sim = Simulation::load_with(db.clone(), map.clone(), &step.save, configure)?;
    let (_, sections) = read_save(&step.save)?;
    let mut expected: BTreeMap<String, RawSection> =
        sections.into_iter().map(|s| (s.key.clone(), s)).collect();
    let mut report = ReplayReport::default();
    for record in &step.records {
        for s in &record.sections {
            expected.insert(s.key.clone(), s.clone());
        }
        let (result, took) = replay_record(&mut sim, record, &expected, ported)?;
        if let Some(t) = took {
            report.timings.push((record.label.clone(), t));
        }
        report.steps.push(result);
        if sim.world_hash() != record.world_hash {
            return Err(invalid(format!(
                "{}: world hash {} differs from recorded {} after resync",
                record.label,
                sim.world_hash(),
                record.world_hash
            )));
        }
    }
    Ok(report)
}

fn replay_record(
    sim: &mut Simulation,
    record: &StepRecord,
    expected: &BTreeMap<String, RawSection>,
    ported: &[&str],
) -> Result<(StepResult, Option<Duration>), SimError> {
    let label = record.label.clone();
    let mut took = None;
    let runs_rust = match label.split_once(':') {
        Some(("system", id)) if ported.contains(&id) => {
            let start = Instant::now();
            sim.pipeline.run_system(id, &mut sim.ctx)?;
            took = Some(start.elapsed());
            true
        }
        Some(("system", _)) => false,
        Some(("dispatch", _)) => {
            sim.ctx.dispatch_events()?;
            true
        }
        _ if label == "clock" => {
            sim.ctx.clock_mut().advance();
            true
        }
        _ => return Err(invalid(format!("unknown record label '{label}'"))),
    };
    if !runs_rust {
        for s in &record.sections {
            sim.ctx.world.load_section(s)?;
        }
        return Ok((StepResult::Applied { label }, None));
    }
    let mut first = None;
    // Resync: the recorded state wins from here on.
    for s in sim.ctx.world.sections() {
        let Some(e) = expected.get(&s.key) else {
            first = first.or(Some((s.key, 0)));
            continue;
        };
        if let Some(offset) = differ_at(&s.bytes, &e.bytes) {
            first = first.or(Some((s.key.clone(), offset)));
            sim.ctx.world.load_section(e)?;
        }
    }
    let result = match first {
        Some((key, offset)) => StepResult::Different { label, key, offset },
        None => StepResult::Equal { label },
    };
    Ok((result, took))
}

/// Offset of the first differing byte (the shorter length when one is a prefix of the other).
fn differ_at(a: &[u8], b: &[u8]) -> Option<usize> {
    a.iter()
        .zip(b)
        .position(|(x, y)| x != y)
        .or((a.len() != b.len()).then(|| a.len().min(b.len())))
}
