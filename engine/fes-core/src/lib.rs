//! Deterministic simulation core.
#![deny(clippy::float_arithmetic)]

pub mod fx;
#[cfg(feature = "testing")]
pub mod testing;

pub use fx::Fx;
