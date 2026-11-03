//! Parity runner: compares the Rust engine to the C# fixtures. Checks grow with later tasks.
use std::path::PathBuf;
use std::process::ExitCode;

fn main() -> ExitCode {
    let mut args = std::env::args().skip(1);
    let mut dir = None;
    while let Some(a) = args.next() {
        if a == "--fixtures" {
            dir = args.next().map(PathBuf::from);
        }
    }
    let Some(dir) = dir else {
        eprintln!("usage: fes-parity --fixtures <dir>");
        return ExitCode::from(2);
    };
    if !dir.join("primitives.json").is_file() {
        eprintln!("missing {}", dir.join("primitives.json").display());
        return ExitCode::FAILURE;
    }
    println!("fixtures found at {}", dir.display());
    ExitCode::SUCCESS
}
