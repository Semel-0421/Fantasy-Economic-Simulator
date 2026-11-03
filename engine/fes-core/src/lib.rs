//! Deterministic simulation core.
#![deny(clippy::float_arithmetic)]

pub mod det;
pub mod enums;
pub mod fx;
pub mod ids;
pub mod io;
#[cfg(feature = "testing")]
pub mod testing;

pub use det::DetRandom;
pub use enums::{
    ControlMode, GuidanceAxis, IndustryTier, QualityGrade, RouteTerrain, SimDetail, WorldLevel,
};
pub use fx::Fx;
pub use ids::{GoodId, NodeId};
