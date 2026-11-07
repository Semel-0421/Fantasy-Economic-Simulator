//! Active market modifiers (C# `MarketModifierTable`, section `economy.modifiers`, version 1).
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;
use fes_data::defs::ModifierTarget;

/// A temporary multiplier on supply, demand or value for a node's subtree and one or all categories.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MarketModifier {
    pub node: i32,
    /// Category index, or -1 for all categories.
    pub category: i32,
    pub target: ModifierTarget,
    pub multiplier: Fx,
    /// The modifier is removed at the start of this turn.
    pub expires_turn: i32,
    /// Source event definition index, or -1 when created by code.
    pub source_event: i32,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MarketModifierTable {
    active: Vec<MarketModifier>,
}

impl MarketModifierTable {
    pub fn active(&self) -> &[MarketModifier] {
        &self.active
    }

    pub fn add(&mut self, modifier: MarketModifier) {
        self.active.push(modifier);
    }

    /// Removes expired modifiers, preserving the order of the rest.
    pub fn remove_expired(&mut self, turn: i32) {
        self.active.retain(|m| m.expires_turn > turn);
    }
}

/// C# casts the saved byte without checking it; a value outside the enum is rejected here.
fn target_of(byte: u8) -> Result<ModifierTarget, IoError> {
    match byte {
        0 => Ok(ModifierTarget::Supply),
        1 => Ok(ModifierTarget::Demand),
        2 => Ok(ModifierTarget::Value),
        _ => Err(IoError::InvalidLength),
    }
}

impl Component for MarketModifierTable {
    fn key(&self) -> &'static str {
        "economy.modifiers"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.active.len() as i32);
        for m in &self.active {
            let _ = w.write_i32(m.node);
            let _ = w.write_i32(m.category);
            let _ = w.write_u8(m.target as u8);
            let _ = w.write_i64(m.multiplier.raw());
            let _ = w.write_i32(m.expires_turn);
            let _ = w.write_i32(m.source_event);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        self.active.clear();
        let count = r.read_i32()?;
        for _ in 0..count {
            self.active.push(MarketModifier {
                node: r.read_i32()?,
                category: r.read_i32()?,
                target: target_of(r.read_u8()?)?,
                multiplier: Fx::from_raw(r.read_i64()?),
                expires_turn: r.read_i32()?,
                source_event: r.read_i32()?,
            });
        }
        Ok(())
    }
}
