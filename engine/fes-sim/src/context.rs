//! Everything a system may touch during a turn (C# `SimContext`).
use crate::clock::SimClock;
use crate::commands::CommandQueue;
use crate::error::SimError;
use crate::events::EventBus;
use crate::schedule::TierSchedule;
use crate::world::WorldState;
use fes_core::det::det_hash;
use fes_core::{DetRandom, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use std::any::{Any, TypeId};
use std::collections::HashMap;
use std::sync::Arc;

/// Shared definitions plus typed extra services registered by modules.
pub struct Services {
    pub database: Arc<GameDatabase>,
    pub map: Arc<MapDef>,
    extras: HashMap<TypeId, Box<dyn Any>>,
}

impl Services {
    pub fn new(database: Arc<GameDatabase>, map: Arc<MapDef>) -> Self {
        Self {
            database,
            map,
            extras: HashMap::new(),
        }
    }

    /// Registers a service; fails when one of this type already exists.
    pub fn add<T: Any>(&mut self, service: T) -> Result<(), SimError> {
        if self.extras.contains_key(&TypeId::of::<T>()) {
            return Err(SimError::DuplicateKey(std::any::type_name::<T>().into()));
        }
        self.extras.insert(TypeId::of::<T>(), Box::new(service));
        Ok(())
    }

    pub fn try_get<T: Any>(&self) -> Option<&T> {
        self.extras.get(&TypeId::of::<T>())?.downcast_ref()
    }

    pub fn try_get_mut<T: Any>(&mut self) -> Option<&mut T> {
        self.extras.get_mut(&TypeId::of::<T>())?.downcast_mut()
    }

    /// Panics when the service was never registered (a wiring bug).
    pub fn get<T: Any>(&self) -> &T {
        self.try_get()
            .unwrap_or_else(|| panic!("service {} not registered", std::any::type_name::<T>()))
    }

    pub fn get_mut<T: Any>(&mut self) -> &mut T {
        self.try_get_mut()
            .unwrap_or_else(|| panic!("service {} not registered", std::any::type_name::<T>()))
    }
}

pub struct SimContext {
    pub world: WorldState,
    pub services: Services,
    pub events: EventBus,
    pub commands: CommandQueue,
    pub schedule: TierSchedule,
}

impl SimContext {
    /// The world must already hold the `core.clock` component.
    pub fn new(
        world: WorldState,
        services: Services,
        schedule: TierSchedule,
    ) -> Result<Self, SimError> {
        if world.try_get::<SimClock>().is_none() {
            return Err(SimError::MissingSection("core.clock".into()));
        }
        Ok(Self {
            world,
            services,
            events: EventBus::new(),
            commands: CommandQueue::new(),
            schedule,
        })
    }

    pub fn clock(&self) -> &SimClock {
        self.world.get::<SimClock>()
    }

    pub fn clock_mut(&mut self) -> &mut SimClock {
        self.world.get_mut::<SimClock>()
    }

    pub fn turn(&self) -> i32 {
        self.clock().turn
    }

    pub fn seed(&self) -> u64 {
        self.clock().seed
    }

    pub fn is_due(&self, level: WorldLevel) -> bool {
        self.schedule.is_due(level, self.turn())
    }

    /// Deterministic random stream keyed by this turn, the stream name and an entity.
    pub fn random_for(&self, stream: &str, entity: i32, salt: u32) -> DetRandom {
        DetRandom::for_key(self.seed(), self.turn(), det_hash::of(stream), entity, salt)
    }
}
