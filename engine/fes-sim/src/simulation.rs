//! One running world (C# `GameSimulation`): step, queue commands, save and load.
use crate::builder::SimulationBuilder;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::error::SimError;
use crate::event_record::SimEventRecord;
use crate::pipeline::TurnPipeline;
use crate::scenario::{ScenarioRun, StatementView};
use crate::world::WorldState;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_save::{read_save, write_save, SaveHeader, FORMAT_VERSION};

/// What one turn did, as plain data: the turn that ran and every event delivered during it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TurnReport {
    pub turn: i32,
    pub events: Vec<SimEventRecord>,
}

pub struct Simulation {
    pub(crate) ctx: SimContext,
    pub(crate) pipeline: TurnPipeline,
    data_hash: u64,
    pub(crate) scenario: Option<ScenarioRun>,
}

impl Simulation {
    pub(crate) fn new(ctx: SimContext, pipeline: TurnPipeline, data_hash: u64) -> Self {
        Self {
            ctx,
            pipeline,
            data_hash,
            scenario: None,
        }
    }

    /// Runs one turn.
    pub fn step(&mut self) -> Result<TurnReport, SimError> {
        let turn = self.turn();
        self.pipeline.advance(&mut self.ctx)?;
        let events: Vec<SimEventRecord> = self
            .ctx
            .events
            .turn_log()
            .iter()
            .map(|e| SimEventRecord::of(e.as_ref()))
            .collect();
        if let Some(run) = self.scenario.as_mut() {
            run.after_turn(&mut self.ctx, &events);
        }
        Ok(TurnReport { turn, events })
    }

    /// Runs one turn and returns how long each system and each phase dispatch took. Native only:
    /// `std::time::Instant` panics on `wasm32-unknown-unknown`; `step` never reads the clock.
    #[cfg(not(target_arch = "wasm32"))]
    pub fn step_timed(&mut self) -> Result<Vec<(&'static str, std::time::Duration)>, SimError> {
        self.pipeline.advance_timed(&mut self.ctx)
    }

    /// Runs one turn timing each system with a caller clock in milliseconds (`performance.now` on
    /// `wasm32`); the turn's results are the same as `step`.
    pub fn step_clocked(
        &mut self,
        now: &mut dyn FnMut() -> f64,
    ) -> Result<Vec<(&'static str, f64)>, SimError> {
        self.pipeline.advance_clocked(&mut self.ctx, now)
    }

    /// What the Commands phase would answer for `cmd` now, without applying it (a UI preview,
    /// C# calling `ICommand.Validate`). Runs `prepare` then `validate` on the live context, so the
    /// unsaved bond-offer cache is refreshed exactly as a C# preview refreshes it.
    pub fn preview(&mut self, cmd: &dyn Command) -> CommandResult {
        cmd.prepare(&mut self.ctx);
        cmd.validate(&self.ctx)
    }

    /// Queues a player command for the next turn's Commands phase.
    pub fn queue(&mut self, cmd: Box<dyn Command>) {
        self.ctx.commands.enqueue_boxed(cmd);
    }

    /// The save file bytes (C# `GameSimulation.Save`).
    pub fn save(&self) -> Vec<u8> {
        let header = SaveHeader {
            format_version: FORMAT_VERSION,
            data_hash: self.data_hash,
            seed: self.ctx.seed(),
            turn: self.turn(),
        };
        write_save(&header, &self.ctx.world.sections())
    }

    /// Builds a world from the definitions and the save's seed, then reads every section of the save
    /// into it. The save must come from the same definitions and map.
    pub fn load(db: GameDatabase, map: MapDef, bytes: &[u8]) -> Result<Simulation, SimError> {
        Self::load_with(db, map, bytes, |b| b)
    }

    /// Like `load`, with `configure` applied to the builder (after the default modules) so a caller
    /// can replace or remove systems.
    pub fn load_with(
        db: GameDatabase,
        map: MapDef,
        bytes: &[u8],
        configure: impl FnOnce(SimulationBuilder) -> SimulationBuilder,
    ) -> Result<Simulation, SimError> {
        let (header, sections) = read_save(bytes)?;
        let builder = SimulationBuilder::new(db, map, header.seed).with_default_modules()?;
        let mut sim = configure(builder).build()?;
        if header.data_hash != sim.data_hash {
            return Err(SimError::Invalid(format!(
                "Save data hash {} differs from the definitions' {}.",
                header.data_hash, sim.data_hash
            )));
        }
        sim.ctx.world.load_sections(&sections)?;
        sim.pipeline.notify_loaded(&mut sim.ctx)?;
        Ok(sim)
    }

    /// True when this world runs a scenario.
    pub fn is_scenario(&self) -> bool {
        self.scenario.is_some()
    }

    /// The scenario's results statement so far (`None` in free mode).
    pub fn statement(&self) -> Option<StatementView> {
        self.scenario.as_ref().map(|s| s.ledger.view(&self.ctx))
    }

    pub fn world_hash(&self) -> u64 {
        self.ctx.world.hash()
    }

    pub fn turn(&self) -> i32 {
        self.ctx.turn()
    }

    pub fn world(&self) -> &WorldState {
        &self.ctx.world
    }

    /// Mutable access for tests that stage a state before stepping.
    #[cfg(feature = "testing")]
    pub fn context_mut(&mut self) -> &mut SimContext {
        &mut self.ctx
    }

    pub fn context(&self) -> &SimContext {
        &self.ctx
    }

    pub fn data_hash(&self) -> u64 {
        self.data_hash
    }
}
