//! `--steps` mode: replays the recorded C# turns (`runs/*/steps/turn_<t>.bin.gz`) against the Rust
//! systems and reports, per turn and per ported system, equal or different (section, offset).
use crate::load_definitions;
use fes_sim::replay::{replay_turn, StepFile, StepResult};
use std::fmt::Write;
use std::path::{Path, PathBuf};

/// Rust systems that run in step replays; every other recorded system is applied from the record.
pub const PORTED_SYSTEMS: &[&str] = &[
    "core.commands",
    "agents.lod",
    "economy.world_events",
    "economy.modifier_fold",
    "agents.production",
    "economy.pricing",
    "agents.household_spending",
    "agents.home_surplus",
    "economy.currency",
    "agents.households",
    "agents.travel",
    "agents.merchants",
    "agents.state_spending",
    "agents.labor",
    "agents.banks",
    "agents.roads",
    "agents.defense",
    "agents.ventures",
    "agents.asset_markets",
    "agents.finance",
    "agents.shorts",
    "agents.income_tax",
    "agents.nobles",
    "agents.migration",
    "agents.state_projects",
    "agents.subsidies",
    "agents.investment",
    "agents.merchant_vehicles",
    "agents.orders",
    "agents.demand_forecast",
    "agents.merchant_membership",
    "agents.contracts",
    "agents.strategic_subsidy",
];

pub struct StepOptions {
    pub fixtures: PathBuf,
    pub data: Option<PathBuf>,
    /// Only this system runs in Rust (default: every ported system).
    pub system: Option<String>,
}

fn step_files(run_dir: &Path) -> Vec<(i32, PathBuf)> {
    let Ok(entries) = std::fs::read_dir(run_dir.join("steps")) else {
        return Vec::new();
    };
    let mut files: Vec<(i32, PathBuf)> = entries
        .filter_map(Result::ok)
        .filter_map(|e| {
            let name = e.file_name().into_string().ok()?;
            let t = name
                .strip_prefix("turn_")?
                .strip_suffix(".bin.gz")?
                .parse()
                .ok()?;
            Some((t, e.path()))
        })
        .collect();
    files.sort();
    files
}

fn run_dirs(fixtures: &Path) -> Vec<PathBuf> {
    let mut dirs: Vec<PathBuf> = std::fs::read_dir(fixtures.join("runs"))
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .map(|e| e.path())
        .filter(|p| p.join("steps").is_dir())
        .collect();
    dirs.sort();
    dirs
}

/// Replays every step file; returns the exit code (1 on any difference or error).
pub fn run_steps(opts: &StepOptions, out: &mut String) -> i32 {
    let dirs = run_dirs(&opts.fixtures);
    if dirs.is_empty() {
        let _ = writeln!(out, "error: no step files found");
        return 1;
    }
    let mut failed = false;
    for dir in dirs {
        failed |= run_dir(opts, &dir, out);
    }
    i32::from(failed)
}

fn run_dir(opts: &StepOptions, dir: &Path, out: &mut String) -> bool {
    let name = dir.file_name().map(|n| n.to_string_lossy().into_owned());
    let _ = writeln!(out, "run {}", name.unwrap_or_default());
    let (db, map) = match load_definitions(dir, &opts.fixtures, opts.data.as_deref()) {
        Ok(d) => d,
        Err(e) => {
            let _ = writeln!(out, "  error: {e}");
            return true;
        }
    };
    let ported: Vec<&str> = match &opts.system {
        Some(id) => vec![id.as_str()],
        None => PORTED_SYSTEMS.to_vec(),
    };
    let mut failed = false;
    for (turn, path) in step_files(dir) {
        let result = StepFile::read(&path).and_then(|s| replay_turn(&s, &db, &map, &ported));
        match result {
            Ok(report) => failed |= report_turn(out, turn, &report.steps),
            Err(e) => {
                failed = true;
                let _ = writeln!(out, "  turn {turn}: error: {e}");
            }
        }
    }
    failed
}

fn report_turn(out: &mut String, turn: i32, steps: &[StepResult]) -> bool {
    let mut failed = false;
    for step in steps {
        match step {
            StepResult::Applied { .. } => {}
            StepResult::Equal { label } => {
                let _ = writeln!(out, "  turn {turn} {label}: equal");
            }
            StepResult::Different { label, key, offset } => {
                failed = true;
                let _ = writeln!(
                    out,
                    "  turn {turn} {label}: different (section {key}, first differing byte {offset})"
                );
            }
        }
    }
    failed
}
