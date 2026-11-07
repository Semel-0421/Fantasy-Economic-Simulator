//! One running world (C# `GameSimulation`): step, queue commands, save and load.
use crate::builder::SimulationBuilder;
use crate::commands::Command;
use crate::context::SimContext;
use crate::error::SimError;
use crate::events::SimEvent;
use crate::pipeline::TurnPipeline;
use crate::world::WorldState;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use fes_save::{read_save, write_save, SaveHeader, FORMAT_VERSION};

/// One delivered event as plain data. Placeholder: the event's type name and its `Debug` text,
/// until systems define real view events.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct EventRecord {
    pub type_name: String,
    pub debug: String,
}

impl EventRecord {
    pub fn of(e: &dyn SimEvent) -> Self {
        Self {
            type_name: e.type_name().to_string(),
            debug: format!("{e:?}"),
        }
    }
}

/// What one turn did, as plain data: the turn that ran and every event delivered during it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TurnReport {
    pub turn: i32,
    pub events: Vec<EventRecord>,
}

pub struct Simulation {
    ctx: SimContext,
    pipeline: TurnPipeline,
    data_hash: u64,
}

impl Simulation {
    pub(crate) fn new(ctx: SimContext, pipeline: TurnPipeline, data_hash: u64) -> Self {
        Self {
            ctx,
            pipeline,
            data_hash,
        }
    }

    /// Runs one turn.
    pub fn step(&mut self) -> Result<TurnReport, SimError> {
        let turn = self.turn();
        self.pipeline.advance(&mut self.ctx)?;
        Ok(TurnReport {
            turn,
            events: self
                .ctx
                .events
                .turn_log()
                .iter()
                .map(|e| EventRecord::of(e.as_ref()))
                .collect(),
        })
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
        let (header, sections) = read_save(bytes)?;
        let mut sim = SimulationBuilder::new(db, map, header.seed)
            .with_default_modules()?
            .build()?;
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

    pub fn world_hash(&self) -> u64 {
        self.ctx.world.hash()
    }

    pub fn turn(&self) -> i32 {
        self.ctx.turn()
    }

    pub fn world(&self) -> &WorldState {
        &self.ctx.world
    }

    pub fn context(&self) -> &SimContext {
        &self.ctx
    }

    pub fn data_hash(&self) -> u64 {
        self.data_hash
    }
}
