use fes_core::testing::{fixture_json, fixtures_dir};
use fes_core::{RouteTerrain, WorldLevel};
use fes_data::database::{GameDatabase, CONFIG_FILE};
use fes_data::map::{MapNodeDef, RouteDef};
use fes_data::{DataError, DataSource, FileDataSource, MapDef, SimulationConfig};
use fes_save::read_save;
use fes_sim::testing::assert_round_trip_with;
use fes_sim::world::{build_nodes, build_routes, NodeTable, RoadState, RouteTable, WorldModule};
use fes_sim::{Module, Services, SimBuilder, SimError, WorldState};
use std::path::PathBuf;
use std::sync::Arc;

fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

fn shipped() -> (GameDatabase, MapDef) {
    let source = FileDataSource::new(data_dir());
    (
        GameDatabase::load(&source).expect("database"),
        MapDef::load(&source).expect("map"),
    )
}

fn built_tables() -> (NodeTable, RouteTable) {
    let (db, map) = shipped();
    let nodes = build_nodes(&map, &db.config).expect("nodes");
    let routes = build_routes(&map, &nodes, &db.config).expect("routes");
    (nodes, routes)
}

fn ints(value: &serde_json::Value) -> Vec<i32> {
    value
        .as_array()
        .expect("array")
        .iter()
        .map(|v| v.as_i64().expect("int") as i32)
        .collect()
}

fn slice(all: &[i32], start: i32, count: i32) -> Vec<i32> {
    all[start as usize..(start + count) as usize].to_vec()
}

#[test]
fn nodes_match_csharp() {
    let (nodes, _) = built_tables();
    let fixture = fixture_json("build/nodes.json");
    let rows = fixture["nodes"].as_array().expect("nodes");
    assert_eq!(rows.len(), nodes.count());
    for (i, row) in rows.iter().enumerate() {
        let n = i as i32;
        let at = |name: &str| row[name].as_i64().expect(name) as i32;
        assert_eq!(row["key"].as_str().unwrap(), nodes.key_of(n), "key {i}");
        assert_eq!(at("level"), i32::from(nodes.level[i]), "level {i}");
        assert_eq!(at("parent"), nodes.parent[i], "parent {i}");
        assert_eq!(at("continent"), nodes.continent[i], "continent {i}");
        assert_eq!(at("core"), nodes.core[i], "core {i}");
        assert_eq!(at("child_start"), nodes.child_start[i], "child_start {i}");
        assert_eq!(at("child_count"), nodes.child_count[i], "child_count {i}");
        let children = slice(&nodes.children, nodes.child_start[i], nodes.child_count[i]);
        assert_eq!(ints(&row["children"]), children, "children {i}");
    }
}

#[test]
fn routes_match_csharp() {
    let (nodes, routes) = built_tables();
    let fixture = fixture_json("build/nodes.json");
    let rows = fixture["routes"].as_array().expect("routes");
    assert_eq!(rows.len(), routes.count());
    for (r, row) in rows.iter().enumerate() {
        let at = |name: &str| row[name].as_i64().expect(name) as i32;
        assert_eq!(at("from"), routes.from[r], "from {r}");
        assert_eq!(at("to"), routes.to[r], "to {r}");
        assert_eq!(at("travel_turns"), routes.travel_turns[r], "turns {r}");
        assert_eq!(at("cost_per_unit"), routes.cost_per_unit[r], "cost {r}");
        assert_eq!(at("base_fee"), routes.base_fee[r], "fee {r}");
        assert_eq!(at("terrain"), i32::from(routes.terrain[r]), "terrain {r}");
    }
    let outgoing = fixture["outgoing"].as_array().expect("outgoing");
    assert_eq!(outgoing.len(), nodes.count());
    for (n, row) in outgoing.iter().enumerate() {
        let start = row["out_start"].as_i64().expect("out_start") as i32;
        let count = row["out_count"].as_i64().expect("out_count") as i32;
        assert_eq!(start, routes.out_start[n], "out_start {n}");
        assert_eq!(count, routes.out_count[n], "out_count {n}");
        let listed = slice(&routes.route_index, start, count);
        assert_eq!(ints(&row["routes"]), listed, "routes of {n}");
    }
}

#[test]
fn roads_round_trips_start_save() {
    let (_, routes) = built_tables();
    assert_round_trip_with("world.roads", || RoadState::with_routes(routes.count()));
}

/// The shipped data with `config.csv` rows replaced the way C# `TestWorlds.SampleConfig` does.
struct Overridden {
    inner: FileDataSource,
    config: String,
}

impl DataSource for Overridden {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        if name == CONFIG_FILE {
            Ok(self.config.clone())
        } else {
            self.inner.read_text(name)
        }
    }
}

fn overridden_source(run: &std::path::Path) -> Overridden {
    let text = std::fs::read_to_string(run.join("turns.json")).expect("turns.json");
    let json: serde_json::Value = serde_json::from_str(&text).expect("json");
    let lines: Vec<String> = json["config"]
        .as_array()
        .expect("config")
        .iter()
        .map(|v| v.as_str().expect("line").to_string())
        .collect();
    let inner = FileDataSource::new(data_dir());
    let base = inner.read_text(CONFIG_FILE).expect("config.csv");
    let mut kept: Vec<&str> = base.lines().collect();
    for line in &lines {
        let prefix = format!("{},", line.split(',').next().unwrap_or(""));
        kept.retain(|l| !l.starts_with(&prefix));
    }
    let mut config = kept.join("\n").trim_end().to_string();
    for line in &lines {
        config.push('\n');
        config.push_str(line);
    }
    config.push('\n');
    Overridden { inner, config }
}

#[test]
fn roads_build_matches_start_save() {
    let runs = fixtures_dir().join("runs");
    let mut checked = 0;
    for entry in std::fs::read_dir(&runs).expect("runs dir") {
        let dir = entry.expect("entry").path();
        if !dir.join("start.save").exists() {
            continue;
        }
        let source = overridden_source(&dir);
        let db = GameDatabase::load(&source).expect("database");
        let map = MapDef::load(&source).expect("map");
        let mut builder = SimBuilder::new(
            WorldState::new(),
            Services::new(Arc::new(db), Arc::new(map)),
        );
        WorldModule.register(&mut builder).expect("register");
        let built = builder.world.sections();
        assert_eq!(built.len(), 1, "world registers one saved section");
        let data = std::fs::read(dir.join("start.save")).expect("start.save");
        let (_, saved) = read_save(&data).expect("save");
        let expected = saved
            .iter()
            .find(|s| s.key == "world.roads")
            .expect("roads");
        assert_eq!(built[0].key, expected.key);
        assert_eq!(built[0].version, expected.version);
        assert_eq!(built[0].bytes, expected.bytes, "run {}", dir.display());
        checked += 1;
    }
    assert_eq!(checked, 4);
}

fn map_of(nodes: &[(&str, WorldLevel, &str)]) -> MapDef {
    let mut map = MapDef::default();
    for (key, level, parent) in nodes {
        map.nodes.push(MapNodeDef {
            key: (*key).to_string(),
            name: String::new(),
            level: *level,
            parent_key: (*parent).to_string(),
            core_key: String::new(),
            tags: Vec::new(),
            tariff_permille: -1,
            specialty_surcharge_permille: -1,
            international_bank: false,
            danger_permille: -1,
            port: false,
            population: -1,
        });
    }
    map
}

fn config() -> SimulationConfig {
    shipped().0.config
}

fn error_message(result: Result<impl Sized, SimError>) -> String {
    match result {
        Ok(_) => panic!("expected an error"),
        Err(e) => e.to_string(),
    }
}

#[test]
fn orders_by_level_and_allows_village_under_region() {
    let map = map_of(&[
        ("v_region", WorldLevel::Village, "r"),
        ("v_city", WorldLevel::Village, "c"),
        ("c", WorldLevel::City, "r"),
        ("r", WorldLevel::Region, "n"),
        ("n", WorldLevel::Nation, "k"),
        ("k", WorldLevel::Continent, ""),
    ]);
    let nodes = build_nodes(&map, &config()).expect("nodes");
    assert_eq!(nodes.count(), 6);
    for l in WorldLevel::ALL {
        let start = nodes.level_start(*l);
        for i in start..start + nodes.level_count(*l) {
            assert_eq!(nodes.level[i as usize], *l as u8);
        }
    }
    let region = nodes.try_find("r").expect("r").0;
    let hamlet = nodes.try_find("v_region").expect("v_region").0;
    assert_eq!(nodes.parent[hamlet as usize], region);
    assert_eq!(nodes.child_count[region as usize], 2); // city + hamlet
    let village = nodes.try_find("v_city").expect("v_city").0;
    assert_eq!(nodes.ancestor_at(village, WorldLevel::Region), region);
    assert_eq!(nodes.continent[village as usize], 0);
}

#[test]
fn rejects_invalid_parent() {
    let map = map_of(&[
        ("k", WorldLevel::Continent, ""),
        ("n", WorldLevel::Nation, "k"),
        ("v", WorldLevel::Village, "n"),
    ]);
    assert_eq!(
        error_message(build_nodes(&map, &config())),
        "Node 'v' (Village) cannot be a child of 'n' (Nation)."
    );
}

#[test]
fn rejects_bad_route_without_allocating() {
    let mut map = map_of(&[("k", WorldLevel::Continent, "")]);
    let config = config();
    let route = |to: &str, turns: i32, cost: i32| RouteDef {
        from_key: "k".into(),
        to_key: to.into(),
        travel_turns: turns,
        cost_per_unit: cost,
        base_fee: 0,
        terrain: RouteTerrain::Road,
        paved: false,
    };
    let nodes = build_nodes(&map, &config).expect("nodes");
    map.routes.push(route("missing", 1, 0));
    assert_eq!(
        error_message(build_routes(&map, &nodes, &config)),
        "Route to unknown node 'missing'."
    );
    map.routes[0] = route("k", 0, 0);
    assert_eq!(
        error_message(build_routes(&map, &nodes, &config)),
        "Route k->k: travel_turns must be >= 1."
    );
    map.routes[0] = route("k", 1, -1);
    assert_eq!(
        error_message(build_routes(&map, &nodes, &config)),
        "Route k->k: cost_per_unit must be >= 0."
    );
}
