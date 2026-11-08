//! Trade licenses held by the player (C# `Licenses.cs`, `agents.licenses`, version 1).
use super::columns::native_table;
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};

native_table! {
    /// One row per (base good, nation); a negative base good is a trade license kind.
    LicenseTable, "agents.licenses", 1,
    {
        base_good: i32,
        nation: i32,
        expires_turn: i32,
    }
}

/// A license lapsed and was dropped (C# `LicenseExpired`); `nation` is the nation's node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LicenseExpired {
    pub base_good: i32,
    pub nation: i32,
    pub turn: i32,
}

/// `agents.licenses` (C# `LicenseSystem`): drops expired licenses at the end of the turn before
/// they expire.
pub struct LicenseSystem;

impl System for LicenseSystem {
    fn id(&self) -> &'static str {
        "agents.licenses"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Cleanup
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let mut expired = Vec::new();
        let licenses = ctx.world.get_mut::<LicenseTable>();
        for r in (0..licenses.count()).rev() {
            if turn + 1 < licenses.expires_turn[r] {
                continue;
            }
            expired.push((licenses.base_good[r], licenses.nation[r]));
            licenses.remove_row_swap_back(r);
        }
        for (base_good, nation) in expired {
            ctx.events.publish(LicenseExpired {
                base_good,
                nation,
                turn,
            });
        }
        Ok(())
    }
}
