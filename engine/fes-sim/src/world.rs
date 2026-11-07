//! The component trait and the typed world container (C# `WorldState`).
use crate::error::SimError;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_save::{world_hash, RawSection};
use std::any::{Any, TypeId};
use std::collections::HashMap;

/// State that is saved, loaded and included in the determinism hash.
pub trait Component: Any {
    /// Stable save-section key, e.g. `"economy.markets"`. Never rename once shipped.
    fn key(&self) -> &'static str;
    /// Schema version of the section.
    fn version(&self) -> i32;
    fn write(&self, w: &mut DotNetWriter<Vec<u8>>);
    fn read(&mut self, r: &mut DotNetReader, version: i32) -> Result<(), IoError>;
}

/// Every piece of simulation state, registered once by its module.
#[derive(Default)]
pub struct WorldState {
    components: Vec<Box<dyn Component>>,
    by_type: HashMap<TypeId, usize>,
    by_key: HashMap<&'static str, usize>,
}

impl WorldState {
    pub fn new() -> Self {
        Self::default()
    }

    /// Registers a component; a duplicate type or save key is an error.
    pub fn add<C: Component>(&mut self, component: C) -> Result<(), SimError> {
        let key = component.key();
        if self.by_key.contains_key(key) || self.by_type.contains_key(&TypeId::of::<C>()) {
            return Err(SimError::DuplicateKey(key.to_string()));
        }
        let index = self.components.len();
        self.by_key.insert(key, index);
        self.by_type.insert(TypeId::of::<C>(), index);
        self.components.push(Box::new(component));
        Ok(())
    }

    /// The registered component of this type. Panics if it was never added (a module-order bug).
    pub fn get<C: Component>(&self) -> &C {
        let index = self.index_of::<C>();
        let any: &dyn Any = self.components[index].as_ref();
        any.downcast_ref::<C>()
            .expect("type index matches component")
    }

    pub fn get_mut<C: Component>(&mut self) -> &mut C {
        let index = self.index_of::<C>();
        let any: &mut dyn Any = self.components[index].as_mut();
        any.downcast_mut::<C>()
            .expect("type index matches component")
    }

    pub fn try_get<C: Component>(&self) -> Option<&C> {
        let index = *self.by_type.get(&TypeId::of::<C>())?;
        let any: &dyn Any = self.components[index].as_ref();
        any.downcast_ref::<C>()
    }

    fn index_of<C: Component>(&self) -> usize {
        match self.by_type.get(&TypeId::of::<C>()) {
            Some(i) => *i,
            None => panic!(
                "Component {} is not registered. Check module order.",
                std::any::type_name::<C>()
            ),
        }
    }

    /// Serialized sections ordered by key (UTF-16 code units, like C# `StringComparer.Ordinal`).
    pub fn sections(&self) -> Vec<RawSection> {
        let mut out: Vec<RawSection> = self
            .components
            .iter()
            .map(|c| {
                let mut w = DotNetWriter::new(Vec::new());
                c.write(&mut w);
                RawSection {
                    key: c.key().to_string(),
                    version: c.version(),
                    bytes: w.into_inner(),
                }
            })
            .collect();
        out.sort_by(|a, b| a.key.encode_utf16().cmp(b.key.encode_utf16()));
        out
    }

    /// Reads every component from its section. An unknown or missing key is an error.
    pub fn load_sections(&mut self, sections: &[RawSection]) -> Result<(), SimError> {
        let mut seen = vec![false; self.components.len()];
        for s in sections {
            let index = *self
                .by_key
                .get(s.key.as_str())
                .ok_or_else(|| SimError::UnknownSection(s.key.clone()))?;
            let mut r = DotNetReader::new(&s.bytes);
            self.components[index].read(&mut r, s.version)?;
            seen[index] = true;
        }
        match seen.iter().position(|ok| !ok) {
            Some(i) => Err(SimError::MissingSection(
                self.components[i].key().to_string(),
            )),
            None => Ok(()),
        }
    }

    /// FNV-1a over every section; equals C# `WorldState.ComputeHash`.
    pub fn hash(&self) -> u64 {
        world_hash(&self.sections())
    }
}
