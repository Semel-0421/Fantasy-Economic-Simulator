use fes_core::testing::fixtures_dir;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_parity::{run, BuildSection, Options};
use fes_save::read_save;

fn options(run_name: Option<&str>) -> Options {
    Options {
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
    let code = run(&options(None), &[], &mut out);
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

fn changed_byte(_: &GameDatabase, _: &MapDef, _: u64) -> Vec<u8> {
    vec![0xAB]
}

#[test]
fn flags_a_wrong_section() {
    let key = start_save_keys("Default_Seed2")[0].clone();
    let sections: Vec<(&'static str, BuildSection)> =
        vec![(Box::leak(key.clone().into_boxed_str()), changed_byte)];
    let mut out = String::new();
    let code = run(&options(Some("Default_Seed2")), &sections, &mut out);
    assert_eq!(code, 1, "{out}");
    assert!(
        out.contains(&format!("{key}: ported-different (first differing byte 0)")),
        "{out}"
    );
    assert!(out.contains("1 different"), "{out}");
}

#[test]
fn unknown_run_fails() {
    let mut out = String::new();
    assert_eq!(run(&options(Some("Nope")), &[], &mut out), 1, "{out}");
}
