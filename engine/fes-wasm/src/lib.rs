//! `wasm-bindgen` wrapper around `fes_sim::Simulation` for the browser Worker. Everything crossing
//! the boundary is plain data: commands in, turn reports, events and views out (spec 7.2).
use fes_core::{Fx, NodeId};
use fes_data::database::GameDatabase;
use fes_data::{InMemoryDataSource, MapDef};
use fes_sim::commands::CommandResult;
use fes_sim::economy::pricing::market_view;
use fes_sim::scenario::ScenarioDef;
use fes_sim::{views, PlayerCommand, SimEventRecord, Simulation, SimulationBuilder};
use serde::Serialize;
use std::collections::BTreeMap;
use wasm_bindgen::prelude::*;

#[cfg(feature = "profile")]
#[wasm_bindgen]
extern "C" {
    #[wasm_bindgen(js_namespace = performance, js_name = now)]
    fn performance_now() -> f64;
}

/// One running world.
#[wasm_bindgen]
pub struct WasmSim {
    sim: Simulation,
    /// The scenario's title, intro and length (`None` in free mode).
    scenario: Option<ScenarioInfo>,
}

/// What the page shows about a running scenario (the `scenario` view).
#[derive(Serialize)]
struct ScenarioInfo {
    id: String,
    title: String,
    intro: String,
    weeks: i32,
}

#[derive(Serialize)]
struct TurnReportJs<'a> {
    turn: i32,
    events: &'a [SimEventRecord],
}

#[derive(Serialize)]
struct QuoteJs {
    good: i32,
    price: Fx,
    stock: i64,
    /// What the traders pay per unit when the player sells.
    offer: i32,
}

#[derive(Serialize)]
struct MarketJs {
    quotes: Vec<QuoteJs>,
}

#[derive(Serialize)]
#[serde(tag = "result", content = "message")]
enum PreviewJs {
    Ok,
    Error(String),
}

fn to_js<T: Serialize + ?Sized>(value: &T) -> Result<JsValue, JsError> {
    let serializer = serde_wasm_bindgen::Serializer::json_compatible();
    value
        .serialize(&serializer)
        .map_err(|e| JsError::new(&e.to_string()))
}

fn err(e: impl std::fmt::Display) -> JsError {
    JsError::new(&e.to_string())
}

/// Reads `{name: text}` into the database and map.
fn definitions(files: JsValue) -> Result<(GameDatabase, MapDef), JsError> {
    let files: BTreeMap<String, String> = serde_wasm_bindgen::from_value(files).map_err(err)?;
    let mut source = InMemoryDataSource::new();
    for (name, text) in &files {
        source.add(name, text.strip_prefix('\u{feff}').unwrap_or(text));
    }
    let db = GameDatabase::load(&source).map_err(err)?;
    let map = MapDef::load(&source).map_err(err)?;
    Ok((db, map))
}

fn command(cmd: JsValue) -> Result<PlayerCommand, JsError> {
    serde_wasm_bindgen::from_value(cmd).map_err(err)
}

#[wasm_bindgen]
impl WasmSim {
    /// Builds the turn-0 world from the data files (`{name: text}`) and a seed.
    #[wasm_bindgen(constructor)]
    pub fn new(files: JsValue, seed: u64) -> Result<WasmSim, JsError> {
        let (db, map) = definitions(files)?;
        let sim = SimulationBuilder::new(db, map, seed)
            .with_default_modules()
            .and_then(SimulationBuilder::build)
            .map_err(err)?;
        Ok(WasmSim {
            sim,
            scenario: None,
        })
    }

    /// Builds a scenario's world from the data files and the scenario file's text (RON).
    pub fn new_scenario(files: JsValue, scenario_text: &str) -> Result<WasmSim, JsError> {
        let (db, map) = definitions(files)?;
        let def = ScenarioDef::parse(scenario_text).map_err(|e| JsError::new(&e))?;
        let info = ScenarioInfo {
            id: def.id.clone(),
            title: def.title.clone(),
            intro: def.intro.clone(),
            weeks: def.weeks,
        };
        let sim = SimulationBuilder::new(db, map, def.seed)
            .with_default_modules()
            .map(|b| b.scenario(def))
            .and_then(SimulationBuilder::build)
            .map_err(err)?;
        Ok(WasmSim {
            sim,
            scenario: Some(info),
        })
    }

    /// Accepts a contract the scenario offered.
    pub fn accept_contract(&mut self, id: &str) -> Result<(), JsError> {
        self.sim.accept_contract(id).map_err(err)
    }

    /// Rebuilds a world from the data files and a save.
    pub fn load(files: JsValue, bytes: &[u8]) -> Result<WasmSim, JsError> {
        let (db, map) = definitions(files)?;
        let sim = Simulation::load(db, map, bytes).map_err(err)?;
        Ok(WasmSim {
            sim,
            scenario: None,
        })
    }

    /// Runs one turn; returns `{turn, events}`.
    pub fn step(&mut self) -> Result<JsValue, JsError> {
        let report = self.sim.step().map_err(err)?;
        to_js(&TurnReportJs {
            turn: report.turn,
            events: &report.events,
        })
    }

    /// Runs one turn and returns `[[system id, ms], ...]` timed with `performance.now` (bench only).
    #[cfg(feature = "profile")]
    pub fn step_profiled(&mut self) -> Result<JsValue, JsError> {
        let times = self.sim.step_clocked(&mut performance_now).map_err(err)?;
        to_js(&times)
    }

    /// Queues a `PlayerCommand` for the next turn.
    pub fn queue(&mut self, cmd: JsValue) -> Result<(), JsError> {
        self.sim.queue(command(cmd)?.into_command());
        Ok(())
    }

    /// Validates a command without applying it: `{result: "Ok"}` or `{result: "Error", message}`.
    pub fn preview(&mut self, cmd: JsValue) -> Result<JsValue, JsError> {
        let boxed = command(cmd)?.into_command();
        let result = match self.sim.preview(boxed.as_ref()) {
            CommandResult::Ok => PreviewJs::Ok,
            CommandResult::Error(m) => PreviewJs::Error(m),
        };
        to_js(&result)
    }

    /// The player's position once the commands queued this week and then `cmd` apply, without
    /// changing anything: `{error, purse, cargo, stock}` (spec 5.2a: preview equals applied).
    pub fn preview_queued(&mut self, cmd: JsValue) -> Result<JsValue, JsError> {
        let boxed = command(cmd)?.into_command();
        to_js(&self.sim.preview_queued(boxed.as_ref()))
    }

    /// A named view: purse, cargo, contracts, shares, licences, statement, market (arg = node), map,
    /// goods, or in a scenario results (the scenario statement; null in free mode), objectives and
    /// scenario_contracts, and scenario (`{id, title, intro, weeks}`; null in free mode).
    pub fn view(&self, name: &str, arg: JsValue) -> Result<JsValue, JsError> {
        let ctx = self.sim.context();
        match name {
            "purse" => to_js(&views::purse(ctx)),
            "cargo" => to_js(&views::cargo(ctx)),
            "contracts" => to_js(&views::contracts(ctx)),
            "shares" => to_js(&views::shares(ctx)),
            "licences" => to_js(&views::licences(ctx)),
            "statement" => to_js(&views::statement(ctx)),
            "market" => self.market(arg),
            "map" => to_js(&views::map(ctx)),
            "goods" => to_js(&views::goods(ctx)),
            "results" => to_js(&self.sim.statement()),
            "objectives" => to_js(&self.sim.objectives()),
            "scenario_contracts" => to_js(&self.sim.scenario_contracts()),
            "scenario" => to_js(&self.scenario),
            _ => Err(JsError::new(&format!("Unknown view '{name}'."))),
        }
    }

    /// The save file bytes.
    pub fn save(&self) -> Vec<u8> {
        self.sim.save()
    }

    pub fn turn(&self) -> i32 {
        self.sim.turn()
    }

    /// The world hash as a decimal string (a `u64` does not fit a JS number).
    pub fn world_hash(&self) -> String {
        self.sim.world_hash().to_string()
    }
}

impl WasmSim {
    fn market(&self, arg: JsValue) -> Result<JsValue, JsError> {
        let node = arg
            .as_f64()
            .filter(|n| n.fract() == 0.0 && *n >= 0.0 && *n < f64::from(self.node_count()))
            .ok_or_else(|| JsError::new("The market view needs a node index."))?;
        let view = market_view(&self.sim, NodeId(node as i32));
        let quotes = view
            .quotes
            .iter()
            .map(|q| QuoteJs {
                good: q.good.0,
                price: q.price,
                stock: q.stock,
                offer: views::sell_offer(self.sim.context(), (q.price.raw() >> 16) as i32),
            })
            .collect();
        to_js(&MarketJs { quotes })
    }

    fn node_count(&self) -> i32 {
        self.sim.context().services.map.nodes.len() as i32
    }
}
