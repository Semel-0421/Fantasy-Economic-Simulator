use fes_core::io::DotNetWriter;
use fes_core::testing::{fixture_json, fixtures_dir};
use fes_save::{read_save, section_hash, world_hash, write_save};

const RUNS: [&str; 4] = [
    "Default_Seed1_ThirtyYears",
    "Default_Seed2",
    "Default_Seed3",
    "NoBills",
];

fn start_save(run: &str) -> Vec<u8> {
    let p = fixtures_dir().join("runs").join(run).join("start.save");
    std::fs::read(&p).unwrap_or_else(|e| panic!("{}: {e}", p.display()))
}

fn u(v: &serde_json::Value) -> u64 {
    v.as_str().unwrap().parse().unwrap()
}

#[test]
fn start_save_roundtrips_byte_for_byte() {
    for run in RUNS {
        let bytes = start_save(run);
        let (header, sections) = read_save(&bytes).unwrap();
        assert_eq!(write_save(&header, &sections), bytes, "{run}");
    }
}

#[test]
fn world_hash_matches_turn_zero() {
    // turns.json has no turn-0 entry. This Default_Seed1 value was derived independently: replaying
    // steps/turn_0.bin.gz over start.save reproduces turns[0].world_hash exactly, and the same
    // replay hashes the untouched start sections to this value.
    let (_, sections) = read_save(&start_save("Default_Seed1_ThirtyYears")).unwrap();
    assert_eq!(world_hash(&sections), 16777473626865726879);
}

#[test]
fn section_hashes_match_turns_json() {
    // Sections that turn 1 leaves unchanged hash identically before and after it.
    for run in RUNS {
        let (_, sections) = read_save(&start_save(run)).unwrap();
        let turns = fixture_json(&format!("runs/{run}/turns.json"));
        let after = turns["turns"][0]["sections"].as_object().unwrap();
        let unchanged = sections
            .iter()
            .filter(|s| after.get(&s.key).map(u) == Some(section_hash(s)))
            .count();
        assert!(unchanged > 0, "{run}");
    }
}

#[test]
fn header_matches_data_hash() {
    let hashes = fixture_json("data/hashes.json");
    for run in RUNS {
        let turns = fixture_json(&format!("runs/{run}/turns.json"));
        let (header, _) = read_save(&start_save(run)).unwrap();
        assert_eq!(header.format_version, 1, "{run}");
        // A config override (NoBills) changes the data hash, so only default-config runs match.
        if turns["config"].as_array().unwrap().is_empty() {
            assert_eq!(header.data_hash, u(&hashes["data_hash"]), "{run}");
        } else {
            assert_ne!(header.data_hash, u(&hashes["data_hash"]), "{run}");
        }
        assert_eq!(header.seed, u(&turns["seed"]), "{run}");
        assert_eq!(header.turn, 0, "{run}");
    }
}

#[test]
fn strings_use_seven_bit_lengths() {
    let mut w = DotNetWriter::new(Vec::new());
    w.write_string(&"a".repeat(200)).unwrap();
    let out = w.into_inner();
    assert_eq!(&out[..2], &[0xC8, 0x01]);
    assert_eq!(out.len(), 202);
}

#[test]
fn rejects_damaged_input() {
    let bytes = start_save("NoBills");
    let mut bad_magic = bytes.clone();
    bad_magic[0] ^= 0xFF;
    assert!(read_save(&bad_magic).is_err());

    let mut bad_version = bytes.clone();
    bad_version[4..8].copy_from_slice(&2i32.to_le_bytes());
    assert!(read_save(&bad_version).is_err());

    // First section: key length byte at 32, key, version i32, length i32.
    let key_len = bytes[32] as usize;
    let len_at = 33 + key_len + 4;
    let mut past_end = bytes.clone();
    past_end[len_at..len_at + 4].copy_from_slice(&i32::MAX.to_le_bytes());
    assert!(read_save(&past_end).is_err());
    past_end[len_at..len_at + 4].copy_from_slice(&(-1i32).to_le_bytes());
    assert!(read_save(&past_end).is_err());

    for n in 0..bytes.len() {
        assert!(read_save(&bytes[..n]).is_err(), "prefix {n}");
    }
}
