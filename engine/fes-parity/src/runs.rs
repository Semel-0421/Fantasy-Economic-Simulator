//! `--runs` mode: plays each kept golden run in Rust and reports the first turn and section that
//! differ from `turns.json` (or that the whole run matches, final save hash included).
use crate::load_definitions;
use fes_sim::full_run::{play_run, RunFixture};
use std::fmt::Write;
use std::path::{Path, PathBuf};

pub struct RunsOptions {
    pub fixtures: PathBuf,
    pub data: Option<PathBuf>,
    pub run: Option<String>,
    pub until: Option<i32>,
}

fn run_dirs(opts: &RunsOptions) -> Result<Vec<PathBuf>, String> {
    let runs = opts.fixtures.join("runs");
    let mut dirs: Vec<PathBuf> = std::fs::read_dir(&runs)
        .map_err(|e| format!("{}: {e}", runs.display()))?
        .filter_map(Result::ok)
        .map(|e| e.path())
        .filter(|p| p.join("turns.json").is_file())
        .filter(|p| {
            opts.run
                .as_ref()
                .is_none_or(|n| p.file_name().is_some_and(|f| f.to_string_lossy() == *n))
        })
        .collect();
    dirs.sort();
    if dirs.is_empty() {
        return Err("no runs found".to_string());
    }
    Ok(dirs)
}

fn play_dir(opts: &RunsOptions, dir: &Path, out: &mut String) -> Result<bool, String> {
    let (db, map) = load_definitions(dir, &opts.fixtures, opts.data.as_deref())?;
    let fixture = RunFixture::load(dir).map_err(|e| e.to_string())?;
    let report = play_run(&db, &map, &fixture, opts.until).map_err(|e| e.to_string())?;
    let compared = report.turns_compared;
    if let Some((turn, key)) = report.first_divergence() {
        let _ = writeln!(
            out,
            "  first divergence: turn {turn}, section {key} ({compared} comparisons)"
        );
        return Ok(true);
    }
    match report.final_hash_matches {
        Some(true) => {
            let _ = writeln!(out, "  all {compared} comparisons match; final hash match");
            Ok(false)
        }
        Some(false) => {
            let _ = writeln!(out, "  all turns match; final hash MISMATCH");
            Ok(true)
        }
        None => {
            let _ = writeln!(out, "  {compared} comparisons match (stopped early)");
            Ok(false)
        }
    }
}

/// Plays the selected runs; returns the exit code (1 on any divergence or error).
pub fn run_runs(opts: &RunsOptions, out: &mut String) -> i32 {
    let dirs = match run_dirs(opts) {
        Ok(d) => d,
        Err(e) => {
            let _ = writeln!(out, "error: {e}");
            return 1;
        }
    };
    let mut failed = false;
    for dir in dirs {
        let name = dir.file_name().map(|n| n.to_string_lossy().into_owned());
        let _ = writeln!(out, "run {}", name.unwrap_or_default());
        match play_dir(opts, &dir, out) {
            Ok(f) => failed |= f,
            Err(e) => {
                failed = true;
                let _ = writeln!(out, "  error: {e}");
            }
        }
    }
    i32::from(failed)
}
