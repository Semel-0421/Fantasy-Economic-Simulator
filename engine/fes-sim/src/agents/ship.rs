//! The player's own ship (C# `PlayerShip.cs`, `agents.ship`, version 2).
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PlayerShip {
    pub owned: bool,
    /// Continent whose coins paid for it.
    pub bought_continent: i32,
    /// Port where the ship waits; -1 waits at whatever port the player is at.
    pub docked_at: i32,
}

impl Default for PlayerShip {
    fn default() -> Self {
        Self {
            owned: false,
            bought_continent: 0,
            docked_at: -1,
        }
    }
}

impl Component for PlayerShip {
    fn key(&self) -> &'static str {
        "agents.ship"
    }

    fn version(&self) -> i32 {
        2
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_bool(self.owned);
        let _ = w.write_i32(self.bought_continent);
        let _ = w.write_i32(self.docked_at);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        self.owned = r.read_bool()?;
        self.bought_continent = r.read_i32()?;
        self.docked_at = r.read_i32()?;
        Ok(())
    }
}
