//! Deterministic simulation core.
#![deny(clippy::float_arithmetic)]

pub mod det;
pub mod fx;
#[cfg(feature = "testing")]
pub mod testing;

pub use det::DetRandom;
pub use fx::Fx;
