//! Test helpers (behind the `testing` feature).
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter};
use fes_core::testing::fixtures_dir;
use fes_save::read_save;

/// For every run's `start.save`: reads section `key` into a fresh `C`, writes it back and asserts
/// the bytes are identical (reporting the first differing offset).
///
/// Takes the component type because a fresh instance cannot be made from the key alone.
pub fn assert_round_trip<C: Component + Default>(key: &str) {
    assert_round_trip_with(key, C::default);
}

/// Like `assert_round_trip`, for a component whose fresh instance depends on the map (a saved count
/// must match its size): `make` builds the instance each run is read into.
pub fn assert_round_trip_with<C: Component>(key: &str, make: impl Fn() -> C) {
    let runs = fixtures_dir().join("runs");
    let mut checked = 0;
    for entry in std::fs::read_dir(&runs).unwrap_or_else(|e| panic!("{}: {e}", runs.display())) {
        let path = entry.expect("dir entry").path().join("start.save");
        if !path.exists() {
            continue;
        }
        let data = std::fs::read(&path).expect("read start.save");
        let (_, sections) = read_save(&data).expect("parse save");
        let section = sections
            .iter()
            .find(|s| s.key == key)
            .unwrap_or_else(|| panic!("{}: no section '{key}'", path.display()));
        round_trip_one(
            make(),
            section.version,
            &section.bytes,
            &path.display().to_string(),
        );
        checked += 1;
    }
    assert!(checked > 0, "no runs found under {}", runs.display());
}

fn round_trip_one<C: Component>(mut component: C, version: i32, bytes: &[u8], what: &str) {
    let mut r = DotNetReader::new(bytes);
    component
        .read(&mut r, version)
        .unwrap_or_else(|e| panic!("{what}: read failed: {e}"));
    assert_eq!(r.remaining(), 0, "{what}: {} trailing bytes", r.remaining());
    let mut w = DotNetWriter::new(Vec::new());
    component.write(&mut w);
    let out = w.into_inner();
    let first_diff = bytes.iter().zip(&out).position(|(a, b)| a != b);
    let offset = first_diff.or((bytes.len() != out.len()).then(|| bytes.len().min(out.len())));
    assert!(
        offset.is_none(),
        "{what}: bytes differ at offset {:?} (in {} bytes, out {} bytes)",
        offset,
        bytes.len(),
        out.len()
    );
}
