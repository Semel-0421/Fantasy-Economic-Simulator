//! `CsvEnum` for the shared enums, so CSV cells parse by name.
use crate::csv::CsvEnum;
use fes_core::{IndustryTier, QualityGrade, RouteTerrain, WorldLevel};

macro_rules! csv_enum {
    ($($t:ident),+) => {$(
        impl CsvEnum for $t {
            const TYPE_NAME: &'static str = stringify!($t);

            fn from_name(name: &str) -> Option<Self> {
                $t::from_name(name)
            }
        }
    )+};
}

csv_enum!(WorldLevel, QualityGrade, IndustryTier, RouteTerrain);
