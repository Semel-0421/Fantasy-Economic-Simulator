//! Test helpers shared by every crate (behind the `testing` feature).
use std::path::PathBuf;

/// `FES_FIXTURES` if set, else `engine/fixtures`.
pub fn fixtures_dir() -> PathBuf {
    match std::env::var_os("FES_FIXTURES") {
        Some(p) => PathBuf::from(p),
        None => PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../fixtures"),
    }
}

/// Parses a JSON file under the fixtures directory.
pub fn fixture_json(rel: &str) -> serde_json::Value {
    let path = fixtures_dir().join(rel);
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    serde_json::from_str(&text).unwrap_or_else(|e| panic!("{}: {e}", path.display()))
}
