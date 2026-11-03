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

/// One override per C# `FormatException`, with the exact message text from `SimulationConfigReader.cs`.
const FAILURES: &[(&str, &str)] = &[
    ("households.village.population,0", "households.village.population and income must be positive."),
    ("merchants.bankrupt_weeks,0", "merchants.bankrupt_weeks must be >= 1."),
    ("merchants.reserve_upkeep_weeks,-1", "merchants.reserve_upkeep_weeks must be >= 0."),
    ("merchants.class1.vehicle,3", "merchants.class1.vehicle must be 0, 1 or 2."),
    ("merchants.class2.vehicle,-1", "merchants.class2.vehicle must be 0, 1 or 2."),
    ("facility.shares,10", "facility.shares must be positive and facility.max_listed_shares below it."),
    ("facility.max_listed_shares,-1", "facility.shares must be positive and facility.max_listed_shares below it."),
    ("terrain.sea.extra_turns,-1", "terrain.sea.extra_turns must not be negative."),
    ("danger.value_scale,0", "danger.value_scale must be >= 1."),
    ("road.build_weeks,0", "road.build_weeks must be >= 1."),
    ("bank.loan_term_weeks,0", "bank.loan_term_weeks must be >= 1."),
    ("bank.margin_call_permille,100", "bank.margin_call_permille must be >= bank.loan_to_value_permille, and bank.margin_call_weeks >= 0."),
    ("bank.margin_call_weeks,-1", "bank.margin_call_permille must be >= bank.loan_to_value_permille, and bank.margin_call_weeks >= 0."),
    ("bank.auction_weeks,0", "bank.auction_weeks must be >= 1."),
    ("policy.max_steps,20", "policy.step_permille x policy.max_steps must stay below 1000."),
    ("policy.max_steps,-1", "policy.step_permille x policy.max_steps must stay below 1000."),
    ("demand.necessity_choke_permille,1000", "demand.necessity_choke_permille must be above 1000."),
    ("demand.max_cheap_permille,999", "demand.max_cheap_permille must be >= 1000."),
    ("households.q1.income_share_permille,0", "household income shares must be positive."),
    ("households.q1.income_share_permille,500", "households.q1..q5.income_share_permille must sum to 1000."),
    ("households.need_memory_weeks,-1", "households.need_memory_weeks must be >= 0."),
    ("households.budget_weeks,0", "households.budget_weeks must be >= 1."),
    ("households.luxury_wealth_cap_permille,999", "households.luxury_wealth_cap_permille must be at least 1000."),
    ("traders.loan_weeks,0", "traders.loan_weeks must be >= 1."),
    ("state.max_per_node,-1", "state.max_per_node must be >= 0."),
    ("state.defense_interval_weeks,0", "state.defense_interval_weeks must be >= 1."),
    ("bank.max_rate_multiplier_permille,999", "bank.max_rate_multiplier_permille must be >= 1000."),
    ("bank.npc_loan_term_weeks,0", "bank.npc_loan_term_weeks must be >= 1."),
    ("finance.history_quarters,3", "finance.history_quarters must be >= 4."),
    ("land.rent_ppm,-1", "land rent must not be negative, and groups list above where they buy back."),
    ("land.rent_years,-1", "land rent must not be negative, and groups list above where they buy back."),
    ("share.group_list_premium_permille,700", "land rent must not be negative, and groups list above where they buy back."),
    ("short.licence_fee,-1", "short selling needs open >= call >= 1000 permille, and no negative values."),
    ("short.call_permille,999", "short selling needs open >= call >= 1000 permille, and no negative values."),
    ("short.open_permille,1200", "short selling needs open >= call >= 1000 permille, and no negative values."),
    ("short.max_lent_shares,-1", "short selling needs open >= call >= 1000 permille, and no negative values."),
    ("venture.shares,0", "venture values out of range."),
    ("venture.loss_permille,1001", "venture values out of range."),
    ("market.premium_floor_permille,0", "market lines must keep floor <= 1 <= heated <= burst <= cap, and a drop below 1."),
    ("market.heated_permille,3000", "market lines must keep floor <= 1 <= heated <= burst <= cap, and a drop below 1."),
    ("market.burst_drop_permille,1000", "market lines must keep floor <= 1 <= heated <= burst <= cap, and a drop below 1."),
    ("specialty.grade_up_permille,1001", "specialty.grade_up_permille must be within 0..1000."),
    ("merchants.judge_hops,0", "merchants.judge_hops must be >= 1."),
    ("credit.bill_weeks,0", "credit weeks must be >= 1."),
    ("credit.state_bond_weeks,0", "credit weeks must be >= 1."),
    ("credit.company_bond_weeks,0", "credit weeks must be >= 1."),
    ("merchants.cautious.range_level,Galaxy", "merchants.cautious.range_level 'Galaxy' is not a WorldLevel."),
    ("merchants.peddler.distance_power,3", "merchants.peddler.distance_power must be 0, 1 or 2."),
    ("facility.dividend_buffer_weeks,-1", "facility.dividend_buffer_weeks must be >= 0."),
    ("facility.bankrupt_weeks,0", "facility.bankrupt_weeks must be >= 1."),
    ("cargo.lost_warehouse_weeks,-1", "cargo.lost_warehouse_weeks must be >= 0."),
    ("lod.focus_level,Galaxy", "lod.focus_level 'Galaxy' is not a WorldLevel."),
    ("price.min,3000000000", "'price.min' = 3000000000 is out of int range."),
    ("price.min,abc", "'price.min' = 'abc' is not an integer."),
];

#[test]
fn every_validation_error_uses_the_csharp_text() {
    for (row, message) in FAILURES {
        let err = load_rows(&format!(
            "{row}
"
        ))
        .err()
        .unwrap_or_else(|| panic!("{row}: no error"));
        assert_eq!(err.to_string(), format!("config.csv: {message}"), "{row}");
    }
}

#[test]
fn repeated_key_takes_the_last_value_and_reports_the_first_unknown() {
    let config = load_rows(
        "price.min,5
price.min,9
",
    )
    .unwrap();
    assert_eq!(config.min_price, 9);
    let err = load_rows(
        "no.a,1
price.min,2
no.b,2
no.a,3
",
    )
    .err()
    .unwrap();
    assert_eq!(err.to_string(), "config.csv: unknown key 'no.a'.");
}

#[test]
fn default_trait_gives_the_code_defaults() {
    assert_eq!(SimulationConfig::default(), SimulationConfig::defaults());
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
