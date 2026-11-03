use fes_core::testing::fixture_json;
use fes_core::Fx;
use std::panic::catch_unwind;

fn num(v: &serde_json::Value, k: &str) -> i64 {
    v[k].as_str().unwrap().parse().unwrap()
}

fn apply(op: &str, a: i64, b: i64) -> i64 {
    let (fa, fb) = (Fx::from_raw(a), Fx::from_raw(b));
    match op {
        "FromInt" => Fx::from_int(a).raw(),
        "FromPermille" => Fx::from_permille(a).raw(),
        "RoundShift" => Fx::round_shift(a),
        "FromRatio" => Fx::from_ratio(a, b).raw(),
        "MulInt" => fa.mul_int(b),
        "MulIntRaw" => Fx::mul_int_raw(a, b),
        "+" => (fa + fb).raw(),
        "-" => (fa - fb).raw(),
        "*" => (fa * fb).raw(),
        "/" => (fa / fb).raw(),
        _ => panic!("unknown op {op}"),
    }
}

#[test]
fn all_ops_match_csharp() {
    let data = fixture_json("primitives.json");
    for row in data["fx"].as_array().unwrap() {
        let op = row["op"].as_str().unwrap();
        let (a, b, want) = (num(row, "a"), num(row, "b"), num(row, "result"));
        let got = apply(op, a, b);
        assert_eq!(got, want, "op {op} a={a} b={b}");
    }
}

#[test]
fn skipped_inputs_fail_instead_of_returning() {
    let data = fixture_json("primitives.json");
    let skipped = data["fx_skipped"].as_array().unwrap();
    assert_eq!(skipped.len(), 81);
    for row in skipped {
        let op = row["op"].as_str().unwrap();
        let (a, b) = (num(row, "a"), num(row, "b"));
        let r = catch_unwind(|| apply(op, a, b));
        assert!(r.is_err(), "op {op} a={a} b={b} returned {r:?}");
    }
}
