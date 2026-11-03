use fes_core::{GoodId, NodeId, QualityGrade, RouteTerrain, WorldLevel};

#[test]
fn good_id_packs_base_index_and_grade() {
    let id = GoodId::from(7, QualityGrade::High);
    assert_eq!(id.0, 30);
    assert_eq!(id.base_index(), 7);
    assert_eq!(id.grade(), QualityGrade::High);
    assert!(id.is_valid());
    assert!(!GoodId::NONE.is_valid());
    assert!(!NodeId::NONE.is_valid());
    assert_eq!(NodeId::NONE.0, -1);
}

#[test]
fn discriminants_match_csharp() {
    assert_eq!(WorldLevel::Village as u8, 4);
    assert_eq!(WorldLevel::COUNT, 5);
    assert_eq!(QualityGrade::COUNT, 4);
    assert_eq!(RouteTerrain::Sea as u8, 3);
    assert!(RouteTerrain::River.is_water() && !RouteTerrain::Mountain.is_water());
    assert_eq!(WorldLevel::from_name("city"), Some(WorldLevel::City));
    assert_eq!(WorldLevel::from_name("town"), None);
    assert_eq!(QualityGrade::from_u8(2), Some(QualityGrade::High));
    assert_eq!(QualityGrade::from_u8(4), None);
}

#[test]
fn read_string_replaces_invalid_utf8() {
    let mut r = fes_core::io::DotNetReader::new(&[3, b'a', 0xFF, b'b']);
    assert_eq!(r.read_string().unwrap(), "a\u{FFFD}b");
}
