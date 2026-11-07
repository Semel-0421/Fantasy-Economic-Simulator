//! `fes-parity --fixtures <dir> [--run <name>] [--data <dir>]`
use fes_parity::{ported_sections, run, Options};
use std::path::PathBuf;
use std::process::ExitCode;

fn parse() -> Option<Options<'static>> {
    let mut args = std::env::args().skip(1);
    let (mut fixtures, mut data, mut run_name) = (None, None, None);
    while let Some(a) = args.next() {
        let value = args.next()?;
        match a.as_str() {
            "--fixtures" => fixtures = Some(PathBuf::from(value)),
            "--data" => data = Some(PathBuf::from(value)),
            "--run" => run_name = Some(value),
            _ => return None,
        }
    }
    Some(Options {
        build: fes_sim::build_world,
        ported: ported_sections(),
        fixtures: fixtures?,
        data,
        run: run_name,
    })
}

fn main() -> ExitCode {
    let Some(opts) = parse() else {
        eprintln!("usage: fes-parity --fixtures <dir> [--run <name>] [--data <dir>]");
        return ExitCode::from(2);
    };
    let mut out = String::new();
    let code = run(&opts, &mut out);
    print!("{out}");
    ExitCode::from(u8::try_from(code).unwrap_or(1))
}
