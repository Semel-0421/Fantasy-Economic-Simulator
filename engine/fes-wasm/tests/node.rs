//! Runs in Node through `wasm-bindgen-test-runner`:
//! `cargo test -p fes-wasm --target wasm32-unknown-unknown --release`.
#![cfg(target_arch = "wasm32")]
use fes_wasm::WasmSim;
use wasm_bindgen::JsValue;
use wasm_bindgen_test::wasm_bindgen_test;

macro_rules! fullscale {
    ($($name:literal),*) => {
        [$(($name, include_str!(concat!("../../fixtures/fullscale/", $name)))),*]
    };
}

const FILES: [(&str, &str); 8] = fullscale!(
    "config.csv",
    "events.csv",
    "facilities.csv",
    "goods.csv",
    "map_nodes.csv",
    "map_routes.csv",
    "market_profiles.csv",
    "recipes.csv"
);
const TURNS: &str = include_str!("../../fixtures/fullscale/turns.json");

fn files() -> JsValue {
    let obj = js_sys::Object::new();
    for (name, text) in FILES {
        js_sys::Reflect::set(&obj, &name.into(), &text.into()).expect("set");
    }
    obj.into()
}

fn expected_hash(turn: usize) -> String {
    let rows: serde_json::Value = serde_json::from_str(TURNS).expect("turns.json");
    rows["turns"][turn]["world_hash"]
        .as_str()
        .expect("hash")
        .to_string()
}

#[wasm_bindgen_test]
fn wasm_sim_matches_native_hash_after_11_turns() {
    let mut sim = WasmSim::new(files(), 1).expect("build");
    assert_eq!(sim.world_hash(), expected_hash(0));
    for _ in 0..11 {
        sim.step().expect("step");
    }
    assert_eq!(sim.turn(), 11);
    assert_eq!(sim.world_hash(), expected_hash(11));
}

#[wasm_bindgen_test]
fn save_and_load_keep_the_hash() {
    let mut sim = WasmSim::new(files(), 1).expect("build");
    sim.step().expect("step");
    let loaded = WasmSim::load(files(), &sim.save()).expect("load");
    assert_eq!(loaded.world_hash(), sim.world_hash());
    assert_eq!(loaded.turn(), sim.turn());
}

#[wasm_bindgen_test]
fn views_and_bad_commands_are_plain_values() {
    let mut sim = WasmSim::new(files(), 1).expect("build");
    assert!(sim.view("purse", JsValue::NULL).expect("purse").is_object());
    assert!(sim.view("market", 0.into()).expect("market").is_object());
    assert!(sim.view("nope", JsValue::NULL).is_err());
    assert!(sim.queue(JsValue::from_str("not a command")).is_err());
}
