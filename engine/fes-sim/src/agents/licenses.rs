//! Trade licenses held by the player (C# `Licenses.cs`, `agents.licenses`, version 1).
use super::columns::native_table;

native_table! {
    /// One row per (base good, nation); a negative base good is a trade license kind.
    LicenseTable, "agents.licenses", 1,
    {
        base_good: i32,
        nation: i32,
        expires_turn: i32,
    }
}
