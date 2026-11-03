use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_parity::{no_world, run, Options};
use fes_save::{read_save, RawSection};

fn options<'a>(run_name: Option<&str>, ported: &'a [&'a str]) -> Options<'a> {
    Options {
        build: no_world,
        ported,
        fixtures: fixtures_dir(),
        data: None,
        run: run_name.map(str::to_string),
    }
}

fn start_save_keys(run_name: &str) -> Vec<String> {
    let bytes = std::fs::read(
        fixtures_dir()
            .join("runs")
            .join(run_name)
            .join("start.save"),
    )
    .expect("start.save");
    read_save(&bytes)
        .expect("readable")
        .1
        .into_iter()
        .map(|s| s.key)
        .collect()
}

#[test]
fn reports_all_sections_not_ported_and_succeeds() {
    let mut out = String::new();
    let code = run(&options(None, &[]), &mut out);
    assert_eq!(code, 0, "{out}");
    for name in [
        "Default_Seed1_ThirtyYears",
        "Default_Seed2",
        "Default_Seed3",
        "NoBills",
    ] {
        assert!(out.contains(&format!("run {name}")), "{out}");
        for key in start_save_keys(name) {
            assert!(
                out.contains(&format!("{key}: not-ported")),
                "{name}/{key} missing in\n{out}"
            );
        }
    }
    assert!(out.contains("data hash: match"), "{out}");
    assert!(out.contains("round trip: match"), "{out}");
    assert!(out.contains(" 0 ported, "), "{out}");
    assert!(out.contains(", 0 different"), "{out}");
}

fn fake_world(_: &GameDatabase, _: &MapDef, _: u64) -> Result<Vec<RawSection>, String> {
    let key = start_save_keys("Default_Seed2")[0].clone();
    Ok(vec![RawSection {
        key,
        version: 1,
        bytes: vec![0xAB],
    }])
}

fn failing_world(_: &GameDatabase, _: &MapDef, _: u64) -> Result<Vec<RawSection>, String> {
    Err("boom".to_string())
}

#[test]
fn flags_a_wrong_section() {
    let key = start_save_keys("Default_Seed2")[0].clone();
    let ported = [key.as_str()];
    let mut opts = options(Some("Default_Seed2"), &ported);
    opts.build = fake_world;
    let mut out = String::new();
    assert_eq!(run(&opts, &mut out), 1, "{out}");
    assert!(
        out.contains(&format!("{key}: ported-different (first differing byte 0)")),
        "{out}"
    );
    assert!(out.contains("1 different"), "{out}");
}

#[test]
fn builder_error_and_missing_key_fail() {
    let ported = ["no.such.key"];
    let mut opts = options(Some("Default_Seed2"), &ported);
    opts.build = fake_world;
    let mut out = String::new();
    assert_eq!(run(&opts, &mut out), 1, "{out}");
    assert!(
        out.contains("no.such.key: ported-different (missing from save)"),
        "{out}"
    );
    opts.build = failing_world;
    let mut out = String::new();
    assert_eq!(run(&opts, &mut out), 1, "{out}");
    assert!(out.contains("boom"), "{out}");
}

#[test]
fn unknown_run_and_empty_fixtures_fail() {
    let mut out = String::new();
    assert_eq!(run(&options(Some("Nope"), &[]), &mut out), 1, "{out}");
    let dir = std::env::temp_dir().join("fes_parity_empty_runs");
    std::fs::create_dir_all(dir.join("runs")).unwrap();
    let mut opts = options(None, &[]);
    opts.fixtures = dir;
    let mut out = String::new();
    assert_eq!(run(&opts, &mut out), 1, "{out}");
    assert!(out.contains("no runs found"), "{out}");
}
