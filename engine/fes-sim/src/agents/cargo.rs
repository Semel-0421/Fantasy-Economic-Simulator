//! Goods held by carts, warehouses and merchant groups (C# `Cargo.cs`, `agents.cargo`, version 1).
use super::columns::native_table;

native_table! {
    /// One row per (holder, good) with a positive quantity.
    CargoTable, "agents.cargo", 1,
    {
        holder: i32,
        good: i32,
        quantity: i32,
    }
}
