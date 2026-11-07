//! `fes-parity --fixtures <dir> [--run <name>] [--data <dir>]`
//! `fes-parity --fixtures <dir> --steps [--system <id>] [--data <dir>]`
//! `fes-parity --fixtures <dir> --runs [--run <name>] [--until <turn>] [--data <dir>]`
use fes_parity::runs::{run_runs, RunsOptions};
use fes_parity::steps::{run_steps, StepOptions};
use fes_parity::{ported_sections, run, Options};
use std::path::PathBuf;
use std::process::ExitCode;

enum Mode {
    Sections(Options<'static>),
    Steps(StepOptions),
    Runs(RunsOptions),
}

fn parse() -> Option<Mode> {
    let mut args = std::env::args().skip(1);
    let (mut fixtures, mut data, mut run_name, mut system) = (None, None, None, None);
    let (mut steps, mut runs, mut until) = (false, false, None);
    while let Some(a) = args.next() {
        match a.as_str() {
            "--steps" => {
                steps = true;
                continue;
            }
            "--runs" => {
                runs = true;
                continue;
            }
            _ => {}
        }
        let value = args.next()?;
        match a.as_str() {
            "--fixtures" => fixtures = Some(PathBuf::from(value)),
            "--data" => data = Some(PathBuf::from(value)),
            "--run" => run_name = Some(value),
            "--system" => system = Some(value),
            "--until" => until = Some(value.parse().ok()?),
            _ => return None,
        }
    }
    let fixtures = fixtures?;
    if runs && !steps && system.is_none() {
        return Some(Mode::Runs(RunsOptions {
            fixtures,
            data,
            run: run_name,
            until,
        }));
    }
    if runs || until.is_some() {
        return None;
    }
    if steps {
        return Some(Mode::Steps(StepOptions {
            fixtures,
            data,
            system,
        }));
    }
    if system.is_some() {
        return None;
    }
    Some(Mode::Sections(Options {
        build: fes_sim::build_world,
        ported: ported_sections(),
        fixtures,
        data,
        run: run_name,
    }))
}

fn main() -> ExitCode {
    let Some(mode) = parse() else {
        eprintln!(
            "usage: fes-parity --fixtures <dir> [--run <name>] [--data <dir>]\n       fes-parity --fixtures <dir> --steps [--system <id>] [--data <dir>]
       fes-parity --fixtures <dir> --runs [--run <name>] [--until <turn>] [--data <dir>]"
        );
        return ExitCode::from(2);
    };
    let mut out = String::new();
    let code = match mode {
        Mode::Sections(opts) => run(&opts, &mut out),
        Mode::Steps(opts) => run_steps(&opts, &mut out),
        Mode::Runs(opts) => run_runs(&opts, &mut out),
    };
    print!("{out}");
    ExitCode::from(u8::try_from(code).unwrap_or(1))
}
