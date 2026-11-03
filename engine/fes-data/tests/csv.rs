use fes_data::{CsvEnum, CsvTable, DataError, DataSource, InMemoryDataSource};

#[derive(Debug, PartialEq)]
enum WorldLevel {
    Village,
    Town,
}

impl CsvEnum for WorldLevel {
    const TYPE_NAME: &'static str = "WorldLevel";
    fn from_name(name: &str) -> Option<Self> {
        match name.to_ascii_lowercase().as_str() {
            "village" => Some(Self::Village),
            "town" => Some(Self::Town),
            _ => None,
        }
    }
}

#[test]
fn csv_handles_quotes_comments_and_blank_lines() {
    let t = CsvTable::parse(
        "t.csv",
        "# comment\nkey,name\n\na,\"Hello, \"\"World\"\"\"\nb,plain\n",
    )
    .unwrap();
    assert_eq!(t.row_count(), 2);
    assert_eq!(t.get_string(0, "name").unwrap(), "Hello, \"World\"");
    assert_eq!(t.get_string(1, "name").unwrap(), "plain");
}

#[test]
fn csv_stray_quotes_in_comments_and_fields_do_not_merge_rows() {
    let t = CsvTable::parse(
        "t.csv",
        "key,name\n# a 5\" plank, \"oops\na,Wolf \"Fang\nb,c\n",
    )
    .unwrap();
    assert_eq!(t.row_count(), 2);
    assert_eq!(t.get_string(0, "name").unwrap(), "Wolf \"Fang");
    assert_eq!(t.get_string(1, "name").unwrap(), "c");
}

#[test]
fn csv_rejects_malformed_values() {
    assert!(CsvTable::parse("t.csv", "a\n\"open\n").is_err());
    let t = CsvTable::parse("t.csv", "n,level\n3000000000,9\n").unwrap();
    assert!(t.get_int(0, "n").is_err());
    assert_eq!(t.get_long(0, "n").unwrap(), 3_000_000_000);
    assert!(t.get_enum::<WorldLevel>(0, "level").is_err());
}

#[test]
fn crlf_quotes_comments_blank_lines_short_rows() {
    let t = CsvTable::parse(
        "t.csv",
        "#c\r\nkey,value\r\n\r\na,\"x, \"\"y\"\"\r\nz\"\r\nb\r\n",
    )
    .unwrap();
    assert_eq!(t.row_count(), 2);
    assert_eq!(t.get_string(0, "value").unwrap(), "x, \"y\"\r\nz");
    assert_eq!(t.get_string(1, "value").unwrap(), "");
}

#[test]
fn unterminated_quote_is_an_error() {
    let e = CsvTable::parse("t.csv", "a\n\"open\n").err().unwrap();
    assert_eq!(e.to_string(), "t.csv line 3: unterminated quoted field.");
}

#[test]
fn numeric_enum_name_is_rejected() {
    let t = CsvTable::parse("t.csv", "level\n9\nTOWN\n").unwrap();
    let e = t.get_enum::<WorldLevel>(0, "level").err().unwrap();
    assert_eq!(
        e.to_string(),
        "t.csv row 1: 'level' = '9' is not a valid WorldLevel."
    );
    assert_eq!(
        t.get_enum::<WorldLevel>(1, "level").unwrap(),
        WorldLevel::Town
    );
    assert_eq!(WorldLevel::from_name("village"), Some(WorldLevel::Village));
}

#[test]
fn accessors_and_fallbacks() {
    let t = CsvTable::parse("t.csv", "a, b\n 5 ,\n").unwrap();
    assert!(t.has_column("b"));
    assert_eq!(t.get_int(0, "a").unwrap(), 5);
    assert_eq!(t.get_int_or(0, "b", 7).unwrap(), 7);
    assert_eq!(t.get_int_or(0, "zz", 8).unwrap(), 8);
    assert_eq!(t.get_string_or(0, "b", "d").unwrap(), "d");
    let e = t.get_string(0, "zz").err().unwrap();
    assert_eq!(e, DataError::Format("t.csv: missing column 'zz'.".into()));
    assert_eq!(t.name(), "t.csv");
}

#[test]
fn in_memory_source() {
    let mut s = InMemoryDataSource::new();
    s.add("a.csv", "x\n1\n");
    assert!(s.exists("a.csv"));
    assert!(!s.exists("b.csv"));
    assert_eq!(s.read_text("a.csv").unwrap(), "x\n1\n");
    assert!(s.read_text("b.csv").is_err());
}
