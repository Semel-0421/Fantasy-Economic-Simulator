//! Per-node level of detail for this turn (C# `LodState` in `Lod.cs`). Recomputed every turn, so
//! not saved; it is a service here, like the other unsaved agents state.

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LodState {
    /// Per node: the `SimDetail` byte (0 = aggregate).
    pub detail: Vec<u8>,
}

impl LodState {
    pub fn new(node_count: usize) -> Self {
        Self {
            detail: vec![0; node_count],
        }
    }
}
