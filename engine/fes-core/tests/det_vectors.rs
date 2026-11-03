use fes_core::det::det_hash::{self, FNV_OFFSET, FNV_PRIME};
use fes_core::det::DetRandom;
use fes_core::testing::fixture_json;
use fes_core::Fx;

fn u(v: &serde_json::Value) -> u64 {
    v.as_str().unwrap().parse().unwrap()
}

fn i(v: &serde_json::Value) -> i64 {
    v.as_i64().unwrap()
}

#[test]
fn hash_functions_match_csharp() {
    let data = fixture_json("primitives.json");
    let h = &data["det_hash"];
    let rows = |k: &str| h[k].as_array().unwrap().clone();
    for k in ["mix", "combine", "hash", "of"] {
        assert_eq!(rows(k).len(), 100, "{k}");
    }
    for r in rows("mix") {
        assert_eq!(det_hash::mix(u(&r[0])), u(&r[1]));
    }
    for r in rows("combine") {
        assert_eq!(det_hash::combine(u(&r[0]), u(&r[1])), u(&r[2]));
    }
    for r in rows("hash") {
        let got = det_hash::hash(
            u(&r[0]),
            i(&r[1]) as i32,
            i(&r[2]) as u32,
            i(&r[3]) as i32,
            i(&r[4]) as u32,
        );
        assert_eq!(got, u(&r[5]), "{r}");
    }
    for r in rows("of") {
        assert_eq!(
            u64::from(det_hash::of(r[0].as_str().unwrap())),
            i(&r[1]) as u64,
            "{r}"
        );
    }
}

fn fnv_units(units: &[u16]) -> u32 {
    let mut h = FNV_OFFSET;
    for &c in units {
        h = (h ^ u64::from(c)).wrapping_mul(FNV_PRIME);
    }
    (h ^ (h >> 32)) as u32
}

#[test]
fn of_hashes_utf16_units() {
    // Non-BMP: a surrogate pair, two code units.
    assert_eq!("\u{1D504}".encode_utf16().count(), 2);
    assert_eq!(det_hash::of("\u{1D504}"), fnv_units(&[0xD835, 0xDD04]));
    assert_eq!(det_hash::of("Ærø"), fnv_units(&[0xC6, 0x72, 0xF8]));
    assert_eq!(det_hash::of("\u{FEFF}a"), fnv_units(&[0xFEFF, 0x61]));
    assert_eq!(det_hash::of(""), 1339080641);
}

#[test]
fn random_draws_match_csharp() {
    let data = fixture_json("primitives.json");
    let rows = data["det_random"].as_array().unwrap();
    assert_eq!(rows.len(), 10);
    for r in rows {
        let seed = u(&r["seed"]);
        let list = |k: &str| r[k].as_array().unwrap().clone();
        let mut g = DetRandom::new(seed);
        for w in list("next_ulong") {
            assert_eq!(g.next_u64(), u(&w), "ulong seed {seed}");
        }
        let mut g = DetRandom::new(seed);
        for w in list("next_int_1000") {
            assert_eq!(i64::from(g.next_int(1000)), i(&w), "int seed {seed}");
        }
        let mut g = DetRandom::new(seed);
        for w in list("next_fx01_raw") {
            assert_eq!(
                g.next_fx01(),
                Fx::from_raw(u(&w) as i64),
                "fx01 seed {seed}"
            );
        }
        let mut g = DetRandom::new(seed);
        for w in list("chance_300") {
            assert_eq!(
                g.chance_permille(300),
                w.as_bool().unwrap(),
                "chance seed {seed}"
            );
        }
        let mut g = DetRandom::new(seed);
        for w in list("spread_250_raw") {
            assert_eq!(
                g.next_spread(250),
                Fx::from_raw(u(&w) as i64),
                "spread seed {seed}"
            );
        }
    }
}

#[test]
fn edge_behaviour() {
    let mut g = DetRandom::new(1);
    assert_eq!(g.next_int(0), 0);
    assert_eq!(g.next_int(-5), 0);
    let a = DetRandom::for_key(7, 3, 2, 5, 0);
    let b = DetRandom::new(det_hash::hash(7, 3, 2, 5, 0));
    assert_eq!(a, b);
    let mut g = DetRandom::new(9);
    let v = g.next_int_range(10, 20);
    assert!((10..20).contains(&v));
}
