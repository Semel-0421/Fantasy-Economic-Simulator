//! Parity runner: compares the Rust engine to the C# fixtures, section by section.
pub mod report;

use fes_data::database::{data_hash, GameDatabase, CONFIG_FILE};
use fes_data::{DataError, DataSource, FileDataSource, MapDef};
use fes_save::{read_save, write_save, RawSection};
use report::{compare, section_line, summary_line, SectionStatus, Tally};
use std::fmt::Write;
use std::path::{Path, PathBuf};

/// Builds the whole turn-0 world (all sections) from the definitions, the map and the seed.
pub type BuildWorld = fn(&GameDatabase, &MapDef, u64) -> Result<Vec<RawSection>, String>;

/// Save keys whose Rust build is considered ported. Empty until Plan 2.
pub fn ported_sections() -> &'static [&'static str] {
    &[]
}

/// The builder used until a world exists; never called while no section is ported.
pub fn no_world(_: &GameDatabase, _: &MapDef, _: u64) -> Result<Vec<RawSection>, String> {
    Err("no world builder yet".to_string())
}

pub struct Options<'a> {
    pub build: BuildWorld,
    pub ported: &'a [&'a str],
    pub fixtures: PathBuf,
    /// The shipped data directory; defaults to `Assets/StreamingAssets/Data` above the fixtures.
    pub data: Option<PathBuf>,
    pub run: Option<String>,
}

/// The shipped data with `config.csv` rows replaced the way C# `TestWorlds.SampleConfig` does.
struct OverrideSource {
    inner: FileDataSource,
    config: String,
}

impl DataSource for OverrideSource {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        if name == CONFIG_FILE {
            Ok(self.config.clone())
        } else {
            self.inner.read_text(name)
        }
    }
}

// CRLF input is normalised to LF by `lines()`; the data hash is computed from parsed content.
fn apply_overrides(text: &str, lines: &[String]) -> String {
    let mut kept: Vec<&str> = text.lines().collect();
    for line in lines {
        let prefix = format!("{},", line.split(',').next().unwrap_or(""));
        kept.retain(|l| !l.starts_with(&prefix));
    }
    let mut out = kept.join("\n").trim_end().to_string();
    for line in lines {
        let _ = write!(out, "\n{line}");
    }
    out.push('\n');
    out
}

fn config_overrides(run_dir: &Path) -> Result<Vec<String>, String> {
    let path = run_dir.join("turns.json");
    let text = std::fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    let json: serde_json::Value =
        serde_json::from_str(&text).map_err(|e| format!("{}: {e}", path.display()))?;
    let lines = json["config"].as_array().ok_or("turns.json: no config")?;
    Ok(lines
        .iter()
        .filter_map(|v| v.as_str().map(str::to_string))
        .collect())
}

fn run_names(opts: &Options) -> Result<Vec<String>, String> {
    let runs = opts.fixtures.join("runs");
    let mut names: Vec<String> = std::fs::read_dir(&runs)
        .map_err(|e| format!("{}: {e}", runs.display()))?
        .filter_map(Result::ok)
        .filter(|e| e.path().is_dir())
        .filter_map(|e| e.file_name().into_string().ok())
        .collect();
    names.sort();
    match &opts.run {
        Some(wanted) if names.contains(wanted) => Ok(vec![wanted.clone()]),
        Some(wanted) => Err(format!("no run named '{wanted}'")),
        None => Ok(names),
    }
}

/// Checks one run, writing its report; returns the section tally and whether it failed.
fn check_run(opts: &Options, name: &str, out: &mut String) -> Result<(Tally, bool), String> {
    let run_dir = opts.fixtures.join("runs").join(name);
    let data_dir = opts
        .data
        .clone()
        .unwrap_or_else(|| opts.fixtures.join("../../Assets/StreamingAssets/Data"));
    let inner = FileDataSource::new(data_dir);
    let base = inner.read_text(CONFIG_FILE).map_err(|e| e.to_string())?;
    let config = apply_overrides(&base, &config_overrides(&run_dir)?);
    let source = OverrideSource { inner, config };
    let db = GameDatabase::load(&source).map_err(|e| e.to_string())?;
    let map = MapDef::load(&source).map_err(|e| e.to_string())?;
    let path = run_dir.join("start.save");
    let bytes = std::fs::read(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    let (header, raw) = read_save(&bytes).map_err(|e| format!("{name}: {e}"))?;
    let mut failed = false;
    let _ = writeln!(out, "run {name}");
    let expected = data_hash(&db, &map);
    if expected == header.data_hash {
        let _ = writeln!(out, "  data hash: match");
    } else {
        failed = true;
        let _ = writeln!(
            out,
            "  data hash: MISMATCH (rust {expected}, fixture {})",
            header.data_hash
        );
    }
    if write_save(&header, &raw) == bytes {
        let _ = writeln!(out, "  round trip: match");
    } else {
        failed = true;
        let _ = writeln!(out, "  round trip: MISMATCH");
    }
    let built = if opts.ported.is_empty() {
        Vec::new()
    } else {
        (opts.build)(&db, &map, header.seed).map_err(|e| format!("{name}: builder: {e}"))?
    };
    let mut tally = Tally::default();
    for section in &raw {
        let status = if opts.ported.contains(&section.key.as_str()) {
            match built.iter().find(|b| b.key == section.key) {
                Some(b) => compare(&b.bytes, &section.bytes),
                None => SectionStatus::Different(0),
            }
        } else {
            SectionStatus::NotPorted
        };
        tally.add(&status);
        section_line(out, &section.key, &status);
    }
    for key in opts.ported {
        if !raw.iter().any(|s| s.key == *key) {
            tally.add(&SectionStatus::Different(0));
            let _ = writeln!(out, "  {key}: ported-different (missing from save)");
        }
    }
    failed |= tally.different > 0;
    Ok((tally, failed))
}

/// Runs the selected runs, appending the report to `out`; returns the process exit code.
pub fn run(opts: &Options, out: &mut String) -> i32 {
    let names = match run_names(opts) {
        Ok(n) => n,
        Err(e) => {
            let _ = writeln!(out, "error: {e}");
            return 1;
        }
    };
    if names.is_empty() {
        let _ = writeln!(out, "error: no runs found");
        return 1;
    }
    let mut total = Tally::default();
    let mut failed = false;
    for name in &names {
        match check_run(opts, name, out) {
            Ok((t, f)) => {
                total.ported += t.ported;
                total.not_ported += t.not_ported;
                total.different += t.different;
                failed |= f;
            }
            Err(e) => {
                let _ = writeln!(out, "run {name}: error: {e}");
                failed = true;
            }
        }
    }
    summary_line(out, &total);
    i32::from(failed)
}
