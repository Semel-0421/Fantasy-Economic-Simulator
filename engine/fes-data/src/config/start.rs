//! C# `ApplyStart`.

use super::reader::{bad, Reader};
use super::{SimulationConfig, WorldLevel};
use crate::{try_parse_enum_name, DataError};

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    if let Some(focus) = r.get("lod.focus_level") {
        c.lod_focus_level = try_parse_enum_name::<WorldLevel>(focus).ok_or_else(|| {
            bad(&format!(
                "config.csv: lod.focus_level '{focus}' is not a WorldLevel."
            ))
        })?;
    }
    r.record("lod.focus_level", c.lod_focus_level.name());
    if let Some(start) = r.get("player.start_node") {
        c.player_start_node = start.to_string();
    }
    let start_node = c.player_start_node.clone();
    r.record("player.start_node", &start_node);
    c.player_start_cash = r.long("player.start_cash", c.player_start_cash)?;
    c.cart_max_weight = r.long("cargo.cart.max_weight", c.cart_max_weight)?;
    c.cart_max_size = r.long("cargo.cart.max_size", c.cart_max_size)?;
    c.warehouse_max_weight = r.long("cargo.warehouse.max_weight", c.warehouse_max_weight)?;
    c.warehouse_max_size = r.long("cargo.warehouse.max_size", c.warehouse_max_size)?;
    Ok(())
}
