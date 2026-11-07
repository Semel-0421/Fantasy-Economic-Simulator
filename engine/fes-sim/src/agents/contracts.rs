//! Supply contracts (C# `Contracts.cs`, `agents.contracts`, version 2).
use super::columns::native_table;

native_table! {
    /// Standing supply contracts between two parties.
    ContractTable, "agents.contracts", 2,
    {
        seller: i32,
        buyer: i32,
        from_cell: i32,
        to_cell: i32,
        from_node: i32,
        to_node: i32,
        quantity: i32,
        unit_price: i64,
        end_turn: i32,
        prepaid: i64,
    }
}
