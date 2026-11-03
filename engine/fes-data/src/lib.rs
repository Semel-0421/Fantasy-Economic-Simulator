//! Data loading: CSV tables and data sources.
#![deny(clippy::float_arithmetic)]

pub mod csv;
pub mod error;
pub mod source;

pub use csv::{try_parse_enum_name, CsvEnum, CsvTable};
pub use error::DataError;
pub use source::{DataSource, FileDataSource, InMemoryDataSource};
