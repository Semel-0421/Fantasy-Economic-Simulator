use fes_core::det::det_hash;
use fes_core::testing::fixture_json;
use fes_data::database::{data_hash, hash_text, GameDatabase};
use fes_data::{DataError, DataSource, FileDataSource, InMemoryDataSource, MapDef};
use std::cell::RefCell;
use std::path::PathBuf;

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

fn shipped() -> FileDataSource {
    FileDataSource::new(data_dir())
}

/// The first path where `actual` differs from `expected`, if any.
#[cfg(feature = "parity")]
fn first_diff(
    path: &str,
    actual: &serde_json::Value,
    expected: &serde_json::Value,
) -> Option<String> {
    use serde_json::Value;
    match (actual, expected) {
        (Value::Object(a), Value::Object(e)) => {
            for (k, ev) in e {
                let Some(av) = a.get(k) else {
                    return Some(format!("{path}.{k}: missing in actual"));
                };
                if let Some(d) = first_diff(&format!("{path}.{k}"), av, ev) {
                    return Some(d);
                }
            }
            a.keys()
                .find(|k| !e.contains_key(*k))
                .map(|k| format!("{path}.{k}: unexpected in actual"))
        }
        (Value::Array(a), Value::Array(e)) => {
            for (i, (av, ev)) in a.iter().zip(e).enumerate() {
                if let Some(d) = first_diff(&format!("{path}[{i}]"), av, ev) {
                    return Some(d);
                }
            }
            (a.len() != e.len())
                .then(|| format!("{path}: length {} vs expected {}", a.len(), e.len()))
        }
        _ => (actual != expected).then(|| format!("{path}: {actual} vs expected {expected}")),
    }
}

#[cfg(feature = "parity")]
#[test]
fn definitions_match_csharp() {
    let db = GameDatabase::load(&shipped()).unwrap();
    let map = MapDef::load(&shipped()).unwrap();
    let text = std::fs::read_to_string(data_dir().join("specialties.csv")).unwrap();
    let table = fes_data::CsvTable::parse("specialties.csv", &text).unwrap();
    let mut tags: Vec<String> = Vec::new();
    for row in 0..table.row_count() {
        let tag = table.get_string(row, "tag").unwrap();
        if !tags.contains(&tag) {
            tags.push(tag);
        }
    }
    let mut actual = db.to_parity_json(&tags);
    actual
        .as_object_mut()
        .unwrap()
        .insert("Map".to_string(), map.to_parity_json());
    let expected = fixture_json("data/definitions.json");
    if let Some(diff) = first_diff("$", &actual, &expected) {
        panic!("first difference at {diff}");
    }
}

fn hash_of(json: &serde_json::Value, key: &str) -> u64 {
    json[key].as_str().unwrap().parse().unwrap()
}

#[test]
fn hashes_match_csharp() {
    let expected = fixture_json("data/hashes.json");
    let db = GameDatabase::load(&shipped()).unwrap();
    let map = MapDef::load(&shipped()).unwrap();
    assert_eq!(db.content_hash(), hash_of(&expected, "content_hash"));
    assert_eq!(map.compute_hash(), hash_of(&expected, "map_hash"));
    assert_eq!(data_hash(&db, &map), hash_of(&expected, "data_hash"));
}

#[test]
fn crlf_and_bom_do_not_change_hash() {
    assert_eq!(
        hash_text("goods.csv", "\u{FEFF}a\r\nb\r"),
        hash_text("goods.csv", "a\nb\n")
    );
    assert_ne!(hash_text("goods.csv", "a"), hash_text("recipes.csv", "a"));
}

struct Recording {
    inner: InMemoryDataSource,
    reads: RefCell<Vec<String>>,
}

impl DataSource for Recording {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        self.reads.borrow_mut().push(name.to_string());
        self.inner.read_text(name)
    }
}

const DEFINITION_FILES: [&str; 6] = [
    "goods.csv",
    "recipes.csv",
    "facilities.csv",
    "events.csv",
    "market_profiles.csv",
    "specialties.csv",
];

fn recording_with(config_text: Option<&str>) -> Recording {
    let mut inner = InMemoryDataSource::new();
    for file in DEFINITION_FILES {
        let text = std::fs::read_to_string(data_dir().join(file)).unwrap();
        inner.add(file, &text);
    }
    let config = std::fs::read_to_string(data_dir().join("config.csv")).unwrap();
    match config_text {
        Some(prefix) => inner.add("config.csv", &format!("{prefix}{config}")),
        None => inner.add("config.csv", &config),
    }
    Recording {
        inner,
        reads: RefCell::new(Vec::new()),
    }
}

#[test]
fn file_order_is_csharp_order() {
    let source = recording_with(None);
    GameDatabase::load(&source).unwrap();
    let mut expected = vec!["config.csv"];
    expected.extend(DEFINITION_FILES);
    assert_eq!(*source.reads.borrow(), expected);
}

#[test]
fn config_text_is_not_hashed_but_its_effective_values_are() {
    let source = recording_with(None);
    let db = GameDatabase::load(&source).unwrap();
    // A comment line changes the config text but not its effective values.
    let commented = recording_with(Some("# note\r\n"));
    let other = GameDatabase::load(&commented).unwrap();
    assert_eq!(db.content_hash(), other.content_hash());
    // Built by hand: the config's effective hash first, then each file in order.
    let mut hash = det_hash::combine(0, db.config.compute_hash());
    for file in DEFINITION_FILES {
        let text = source.inner.read_text(file).unwrap();
        hash = det_hash::combine(hash, hash_text(file, &text));
    }
    assert_eq!(db.content_hash(), hash);
}

#[test]
fn missing_required_file_is_not_found() {
    let source = InMemoryDataSource::new();
    let err = GameDatabase::load(&source).err().unwrap();
    assert_eq!(
        err,
        DataError::NotFound("Required data file 'goods.csv' is missing.".to_string())
    );
}

fn load_with(goods: &str, extra: &[(&str, &str)]) -> Result<GameDatabase, DataError> {
    let mut source = InMemoryDataSource::new();
    source.add("goods.csv", goods);
    source.add("market_profiles.csv", "level,category,grades\n");
    for (name, text) in extra {
        source.add(name, text);
    }
    GameDatabase::load(&source)
}

fn format_error(result: Result<GameDatabase, DataError>) -> String {
    match result.expect_err("load fails") {
        DataError::Format(m) => m,
        other => panic!("not a format error: {other:?}"),
    }
}

const GOODS: &str = "key,category,base_price,base_demand\nwheat,grain,10,4\n";
const EVENT_HEADER: &str = "key,level,category,target,mul_permille,duration,chance_permille\n";

/// Goods text, extra files and the expected message.
type Case<'a> = (String, Vec<(&'a str, String)>, &'a str);

#[test]
fn validation_messages_match_csharp() {
    let cases: Vec<Case> = vec![
        (
            "key,category,base_price\nwheat,grain,1\nwheat,grain,1\n".to_string(),
            vec![],
            "goods.csv: duplicate key 'wheat'.",
        ),
        (
            "key,category,base_price,requires_license\nwheat,grain,1,2\n".to_string(),
            vec![],
            "goods.csv row 1: requires_license must be 0 or 1.",
        ),
        (
            "key,category,base_price,class\nwheat,grain,1,royal\n".to_string(),
            vec![],
            "goods.csv row 1: class must be necessity, semi or luxury, not 'royal'.",
        ),
        (
            GOODS.to_string(),
            vec![(
                "recipes.csv",
                "recipe,role,good,grade,qty\nr,in,oats,Mid,1\n".to_string(),
            )],
            "recipes.csv row 1: unknown good 'oats'.",
        ),
        (
            GOODS.to_string(),
            vec![(
                "recipes.csv",
                "recipe,role,good,grade,qty\nr,in,wheat,Mid,1\nr,in,wheat,Mid,2\n".to_string(),
            )],
            "recipes.csv row 2: 'wheat' is listed twice as 'in' in recipe 'r'.",
        ),
        (
            GOODS.to_string(),
            vec![(
                "events.csv",
                format!("{EVENT_HEADER}e,Region,nope,Supply,500,3,1\n"),
            )],
            "events.csv row 1: unknown category 'nope'.",
        ),
        (
            GOODS.to_string(),
            vec![(
                "specialties.csv",
                "tag,category,good\nt,grain,rye\n".to_string(),
            )],
            "specialties.csv row 1: unknown good 'rye'.",
        ),
    ];
    for (goods, extra, message) in cases {
        let extra: Vec<(&str, &str)> = extra.iter().map(|(n, t)| (*n, t.as_str())).collect();
        assert_eq!(format_error(load_with(&goods, &extra)), message);
    }
}

#[test]
fn lookups_work() {
    let db = GameDatabase::load(&shipped()).unwrap();
    let id = db
        .try_get_good("wheat", fes_core::QualityGrade::Mid)
        .unwrap();
    assert_eq!(id.0, 1);
    assert!(db
        .try_get_good("no_such", fes_core::QualityGrade::Mid)
        .is_none());
    assert!(db.specialty("plains").is_some());
    assert!(db.specialty("no_such_tag").is_none());
}

#[test]
fn map_without_routes_file_loads() {
    let mut source = InMemoryDataSource::new();
    source.add("map_nodes.csv", "key,level,parent\na,Continent,\n");
    let map = MapDef::load(&source).unwrap();
    assert_eq!(map.nodes.len(), 1);
    assert!(map.routes.is_empty());
}
