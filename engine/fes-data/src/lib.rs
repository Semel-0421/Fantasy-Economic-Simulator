//! Data loading: CSV tables and data sources.
#![deny(clippy::float_arithmetic)]

pub mod config;
pub mod csv;
pub mod error;
pub mod source;

pub use config::{SimulationConfig, TierCadence, WorldLevel};
pub use csv::{try_parse_enum_name, CsvEnum, CsvTable};
pub use error::DataError;
pub use source::{DataSource, FileDataSource, InMemoryDataSource};
