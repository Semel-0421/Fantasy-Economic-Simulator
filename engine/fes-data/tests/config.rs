use fes_core::testing::fixture_json;
use fes_data::{CsvTable, DataError, SimulationConfig};
use serde_json::Value;
use std::collections::BTreeMap;

fn shipped_table() -> CsvTable {
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data/config.csv"
    );
    let text = std::fs::read_to_string(path).expect("shipped config.csv");
    CsvTable::parse("config.csv", &text).expect("config.csv parses")
}

fn shipped_config() -> SimulationConfig {
    SimulationConfig::load(Some(&shipped_table())).expect("shipped config loads")
}

fn string_map(json: &Value) -> BTreeMap<String, String> {
    json.as_object()
        .expect("object")
        .iter()
        .map(|(k, v)| (k.clone(), v.as_str().expect("string value").to_string()))
        .collect()
}

fn assert_same_effective(actual: &SimulationConfig, expected: &Value) {
    let expected = string_map(expected);
    let actual = actual.effective_values();
    for (key, want) in &expected {
        assert_eq!(actual.get(key), Some(want), "effective value of '{key}'");
    }
    assert_eq!(actual, &expected, "key sets differ");
}

#[test]
fn effective_values_match_csharp() {
    let expected = fixture_json("data/config_effective.json");
    assert_same_effective(&shipped_config(), &expected);
}

#[cfg(feature = "parity")]
#[test]
fn fields_match_csharp() {
    let expected = fixture_json("data/config_fields.json");
    let actual = shipped_config().to_parity_json();
    let (actual, expected) = (actual.as_object().unwrap(), expected.as_object().unwrap());
    for (name, want) in expected {
        assert_eq!(actual.get(name), Some(want), "field {name}");
    }
    assert_eq!(actual.len(), expected.len(), "field sets differ");
}

#[test]
fn hash_matches_csharp() {
    let hashes = fixture_json("data/hashes.json");
    let want: u64 = hashes["config_hash"].as_str().unwrap().parse().unwrap();
    assert_eq!(shipped_config().compute_hash(), want);
}

#[test]
fn missing_key_takes_default() {
    let expected = fixture_json("data/config_defaults_effective.json");
    let config = SimulationConfig::load(None).unwrap();
    assert_same_effective(&config, &expected);
    assert_same_effective(&SimulationConfig::defaults(), &expected);
}

#[test]
fn unknown_key_is_an_error() {
    let table = CsvTable::parse("config.csv", "key,value\nno.such.key,1\n").unwrap();
    let err = SimulationConfig::load(Some(&table)).unwrap_err();
    assert_eq!(
        err,
        DataError::Format("config.csv: unknown key 'no.such.key'.".to_string())
    );
}

fn load_rows(rows: &str) -> Result<SimulationConfig, DataError> {
    let table = CsvTable::parse("config.csv", &format!("key,value\n{rows}")).unwrap();
    SimulationConfig::load(Some(&table))
}

#[test]
fn restated_default_hashes_like_the_default() {
    let defaults = SimulationConfig::load(None).unwrap();
    let restated = load_rows("demand.max_cheap_permille,2000\nprice.min,1\n").unwrap();
    assert_eq!(restated.compute_hash(), defaults.compute_hash());
}

#[test]
fn validation_errors_use_the_csharp_text() {
    let err = load_rows("bank.loan_term_weeks,0\n").err().unwrap();
    assert_eq!(
        err.to_string(),
        "config.csv: bank.loan_term_weeks must be >= 1."
    );
    let err = load_rows("price.min,abc\n").err().unwrap();
    assert_eq!(
        err.to_string(),
        "config.csv: 'price.min' = 'abc' is not an integer."
    );
    let err = load_rows("price.min,3000000000\n").err().unwrap();
    assert_eq!(
        err.to_string(),
        "config.csv: 'price.min' = 3000000000 is out of int range."
    );
    let err = load_rows("lod.focus_level,Galaxy\n").err().unwrap();
    assert_eq!(
        err.to_string(),
        "config.csv: lod.focus_level 'Galaxy' is not a WorldLevel."
    );
    let err = load_rows("merchants.class1.vehicle,3\n").err().unwrap();
    assert_eq!(
        err.to_string(),
        "config.csv: merchants.class1.vehicle must be 0, 1 or 2."
    );
}

#[test]
fn file_values_are_normalized_like_csharp() {
    let config =
        load_rows("grade.low.price_permille, +500 \nlod.focus_level,city\nmerchants.bids,2\n")
            .unwrap();
    let effective = config.effective_values();
    assert_eq!(effective["grade.low.price_permille"], "fx:32768");
    assert_eq!(effective["lod.focus_level"], "City");
    assert!(config.merchant_bids);
    assert!(config.merchant_bids_while_loaded);
}

#[test]
fn file_data_source_drops_a_leading_byte_order_mark() {
    use fes_data::{DataSource, FileDataSource};
    let dir = std::env::temp_dir().join(format!("fes_bom_{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(dir.join("config.csv"), "\u{feff}key,value\nprice.min,7\n").unwrap();
    let source = FileDataSource::new(dir.clone());
    let text = source.read_text("config.csv").unwrap();
    assert!(text.starts_with("key,value"));
    let table = CsvTable::parse("config.csv", &text).unwrap();
    let config = SimulationConfig::load(Some(&table)).unwrap();
    assert_eq!(config.min_price, 7);
    std::fs::remove_dir_all(&dir).unwrap();
}
