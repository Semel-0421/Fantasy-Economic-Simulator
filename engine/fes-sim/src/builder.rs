//! The composition root (C# `SimulationBuilder`): modules in dependency order, system overrides,
//! then the pipeline and its initialization.
use crate::agents::AgentsModule;
use crate::clock::SimClock;
use crate::commands::CommandSystem;
use crate::context::{Services, SimContext};
use crate::economy::EconomyModule;
use crate::error::SimError;
use crate::module::{module_order, Module, SimBuilder};
use crate::pipeline::System;
use crate::scenario::{ScenarioDef, ScenarioRules, ScenarioRun};
use crate::schedule::TierSchedule;
use crate::simulation::Simulation;
use crate::world::{WorldModule, WorldState};
use fes_data::database::{data_hash, GameDatabase};
use fes_data::{DataSource, MapDef};
use std::sync::Arc;

enum SystemOverride {
    Replace(Box<dyn System>),
    Remove(&'static str),
}

pub struct SimulationBuilder {
    database: Arc<GameDatabase>,
    map: Arc<MapDef>,
    seed: u64,
    modules: Vec<Box<dyn Module>>,
    overrides: Vec<SystemOverride>,
    scenario: Option<ScenarioRules>,
    scenario_def: Option<ScenarioDef>,
}

impl SimulationBuilder {
    /// A builder with no modules; add them with `add_module` or `with_default_modules`.
    pub fn new(database: GameDatabase, map: MapDef, seed: u64) -> Self {
        Self::shared(Arc::new(database), Arc::new(map), seed)
    }

    pub fn shared(database: Arc<GameDatabase>, map: Arc<MapDef>, seed: u64) -> Self {
        Self {
            database,
            map,
            seed,
            modules: Vec::new(),
            overrides: Vec::new(),
            scenario: None,
            scenario_def: None,
        }
    }

    /// Loads the definitions and the map from a data source and adds the default modules.
    pub fn from_data(source: &dyn DataSource, seed: u64) -> Result<Self, SimError> {
        let db = GameDatabase::load(source).map_err(|e| SimError::Invalid(e.to_string()))?;
        let map = MapDef::load(source).map_err(|e| SimError::Invalid(e.to_string()))?;
        Self::new(db, map, seed).with_default_modules()
    }

    /// Adds the world, economy and agents modules that are not added yet.
    pub fn with_default_modules(mut self) -> Result<Self, SimError> {
        let defaults: [Box<dyn Module>; 3] = [
            Box::new(WorldModule),
            Box::new(EconomyModule),
            Box::new(AgentsModule),
        ];
        for module in defaults {
            if !self.modules.iter().any(|m| m.id() == module.id()) {
                self = self.add_module(module)?;
            }
        }
        Ok(self)
    }

    /// Adds a module; ids must be unique.
    pub fn add_module(mut self, module: Box<dyn Module>) -> Result<Self, SimError> {
        if self.modules.iter().any(|m| m.id() == module.id()) {
            return Err(SimError::Invalid(format!(
                "Module '{}' is added twice.",
                module.id()
            )));
        }
        self.modules.push(module);
        Ok(self)
    }

    /// Runs `replacement` instead of the module system with the same id.
    pub fn replace_system(mut self, replacement: Box<dyn System>) -> Self {
        self.overrides.push(SystemOverride::Replace(replacement));
        self
    }

    /// Drops the module system with this id from the pipeline.
    pub fn remove_system(mut self, id: &'static str) -> Self {
        self.overrides.push(SystemOverride::Remove(id));
        self
    }

    /// Runs the world as a scenario with these teaching rules (spec 5.7); free mode never calls it.
    pub fn scenario_rules(mut self, rules: ScenarioRules) -> Self {
        self.scenario = Some(rules);
        self
    }

    /// Runs the world as this scenario: its seed, the player at its start with its coins, its
    /// script and objectives, and the scenario rules (no random events or encounters).
    pub fn scenario(mut self, def: ScenarioDef) -> Self {
        self.seed = def.seed;
        self.scenario = Some(ScenarioRules::scenario());
        self.scenario_def = Some(def);
        self
    }

    /// C# `SimulationBuilder.Build`: clock, services, overrides, the command system, the modules in
    /// dependency order, the pipeline, then `initialize`.
    pub fn build(self) -> Result<Simulation, SimError> {
        let mut services = Services::new(Arc::clone(&self.database), Arc::clone(&self.map));
        let scenario = self.scenario.is_some();
        if let Some(rules) = self.scenario {
            services.add(rules)?;
        }
        let mut world = WorldState::new();
        world.add(SimClock::new(self.seed))?;
        let mut builder = SimBuilder::new(world, services);
        for o in self.overrides {
            match o {
                SystemOverride::Replace(s) => builder.replace_system(s)?,
                SystemOverride::Remove(id) => builder.remove_system(id)?,
            }
        }
        builder.add_system(Box::new(CommandSystem))?;
        for i in module_order(&self.modules)? {
            self.modules[i].register(&mut builder)?;
        }
        let mut pipeline = builder.build_pipeline()?;
        let schedule = TierSchedule::new(&self.database.config.tier_cadences)?;
        let mut ctx = SimContext::new(builder.world, builder.services, schedule)?;
        pipeline.initialize(&mut ctx)?;
        let hash = data_hash(&self.database, &self.map);
        let mut sim = Simulation::new(ctx, pipeline, hash);
        if scenario {
            let run = ScenarioRun::start(&mut sim.ctx, self.scenario_def.as_ref())
                .map_err(SimError::Invalid)?;
            sim.scenario = Some(run);
        }
        Ok(sim)
    }
}
