//! Feature modules and the builder that collects their registrations (C# `ISimModule`, `SimBuilder`).
use crate::context::Services;
use crate::error::SimError;
use crate::pipeline::{System, TurnPipeline};
use crate::world::WorldState;
use std::collections::{HashMap, HashSet};

/// A feature package: registers its components, services and systems.
pub trait Module {
    /// Unique module id.
    fn id(&self) -> &'static str;
    /// Ids of modules whose components and services this module needs; they register first.
    fn requires(&self) -> &[&'static str];
    fn register(&self, b: &mut SimBuilder) -> Result<(), SimError>;
}

/// Orders modules so each registers after the modules it requires (C# `ModuleOrder.Sort`).
/// Stable: among modules whose requirements are met, the one added first goes first.
/// Returns indices into `modules`.
pub fn module_order(modules: &[Box<dyn Module>]) -> Result<Vec<usize>, SimError> {
    let ids: HashSet<&str> = modules.iter().map(|m| m.id()).collect();
    for m in modules {
        for r in m.requires() {
            if !ids.contains(r) {
                return Err(SimError::Invalid(format!(
                    "Module '{}' requires module '{r}', which is not added.",
                    m.id()
                )));
            }
        }
    }
    let mut sorted = Vec::with_capacity(modules.len());
    let mut placed: HashSet<&str> = HashSet::new();
    let mut pending: Vec<usize> = (0..modules.len()).collect();
    while !pending.is_empty() {
        let next = pending
            .iter()
            .position(|&i| modules[i].requires().iter().all(|r| placed.contains(r)));
        let Some(next) = next else {
            let names: Vec<&str> = pending.iter().map(|&i| modules[i].id()).collect();
            return Err(SimError::Invalid(format!(
                "Modules form a dependency cycle: {}.",
                names.join(", ")
            )));
        };
        let i = pending.remove(next);
        placed.insert(modules[i].id());
        sorted.push(i);
    }
    Ok(sorted)
}

/// Collects module registrations, then produces the ordered pipeline.
pub struct SimBuilder {
    systems: Vec<Box<dyn System>>,
    system_ids: HashSet<&'static str>,
    /// System id -> replacement registered before modules ran; `None` removes the system.
    overrides: HashMap<&'static str, Option<Box<dyn System>>>,
    pub world: WorldState,
    pub services: Services,
}

impl SimBuilder {
    pub fn new(world: WorldState, services: Services) -> Self {
        Self {
            systems: Vec::new(),
            system_ids: HashSet::new(),
            overrides: HashMap::new(),
            world,
            services,
        }
    }

    /// Call before modules register: a module adding a system with the replacement's id gets the
    /// replacement instead, which keeps the id (and so the RNG stream).
    pub fn replace_system(&mut self, replacement: Box<dyn System>) -> Result<(), SimError> {
        self.set_override(replacement.id(), Some(replacement))
    }

    /// Call before modules register: the system with this id is not run.
    pub fn remove_system(&mut self, id: &'static str) -> Result<(), SimError> {
        self.set_override(id, None)
    }

    fn set_override(
        &mut self,
        id: &'static str,
        system: Option<Box<dyn System>>,
    ) -> Result<(), SimError> {
        if self.system_ids.contains(id) {
            return Err(SimError::Invalid(format!(
                "System '{id}' is already registered; override it before modules register."
            )));
        }
        if self.overrides.contains_key(id) {
            return Err(SimError::Invalid(format!(
                "System '{id}' is replaced or removed twice."
            )));
        }
        self.overrides.insert(id, system);
        Ok(())
    }

    pub fn add_system(&mut self, system: Box<dyn System>) -> Result<(), SimError> {
        let id = system.id();
        if !self.system_ids.insert(id) {
            return Err(SimError::Invalid(format!(
                "System id '{id}' is registered twice."
            )));
        }
        match self.overrides.get_mut(id) {
            Some(slot) => {
                // The override is consumed; the key stays so build_pipeline sees it was used.
                if let Some(replacement) = slot.take() {
                    self.systems.push(replacement);
                }
            }
            None => self.systems.push(system),
        }
        Ok(())
    }

    /// Fails when an override names a system no module registered.
    pub fn build_pipeline(&mut self) -> Result<TurnPipeline, SimError> {
        for id in self.overrides.keys() {
            if !self.system_ids.contains(id) {
                return Err(SimError::Invalid(format!(
                    "Cannot replace or remove system '{id}': no module registers it."
                )));
            }
        }
        Ok(TurnPipeline::new(std::mem::take(&mut self.systems)))
    }
}
