//! Typed reads of `config.csv` values, recording each effective value like the C# reader.

use crate::DataError;
use fes_core::Fx;
use std::collections::{BTreeMap, HashMap};

/// Lower-case `WorldLevel` names, in enum order.
pub(super) const LEVELS: [&str; 5] = ["continent", "nation", "region", "city", "village"];
/// Lower-case `IndustryTier` names.
pub(super) const TIERS: [&str; 3] = ["primary", "secondary", "tertiary"];
/// Lower-case `QualityGrade` names.
pub(super) const GRADES: [&str; 4] = ["low", "mid", "high", "best"];
/// Lower-case `RouteTerrain` names.
pub(super) const TERRAINS: [&str; 4] = ["road", "mountain", "river", "sea"];

/// A `FormatException` with the given text.
pub(super) fn bad(message: &str) -> DataError {
    DataError::Format(message.to_string())
}

/// The file's key/value pairs plus the effective values recorded while the fields are applied.
pub(super) struct Reader {
    values: HashMap<String, String>,
    order: Vec<String>,
    effective: BTreeMap<String, String>,
}

/// `long.TryParse(s, NumberStyles.Integer, InvariantCulture)`: optional surrounding white space and sign.
fn parse(key: &str, s: &str) -> Result<i64, DataError> {
    s.trim()
        .parse::<i64>()
        .map_err(|_| DataError::Format(format!("config.csv: '{key}' = '{s}' is not an integer.")))
}

impl Reader {
    /// Pairs in file order; a repeated key keeps its first position and takes the last value.
    pub(super) fn new(pairs: Vec<(String, String)>) -> Self {
        let mut values = HashMap::new();
        let mut order = Vec::new();
        for (key, value) in pairs {
            if values.insert(key.clone(), value).is_none() {
                order.push(key);
            }
        }
        Self {
            values,
            order,
            effective: BTreeMap::new(),
        }
    }

    pub(super) fn get(&self, key: &str) -> Option<&str> {
        self.values.get(key).map(String::as_str)
    }

    pub(super) fn record(&mut self, key: &str, value: &str) {
        self.effective.insert(key.to_string(), value.to_string());
    }

    pub(super) fn long(&mut self, key: &str, fallback: i64) -> Result<i64, DataError> {
        let value = match self.get(key) {
            Some(s) => parse(key, s)?,
            None => fallback,
        };
        self.record(key, &value.to_string());
        Ok(value)
    }

    pub(super) fn int(&mut self, key: &str, fallback: i32) -> Result<i32, DataError> {
        let value = self.long(key, i64::from(fallback))?;
        i32::try_from(value).map_err(|_| {
            DataError::Format(format!(
                "config.csv: '{key}' = {value} is out of int range."
            ))
        })
    }

    /// Recorded as the raw Fx value, so a file value of 1000 and the default `Fx::ONE` hash the same.
    pub(super) fn permille(&mut self, key: &str, fallback: Fx) -> Result<Fx, DataError> {
        let value = match self.get(key) {
            Some(s) => Fx::from_permille(parse(key, s)?),
            None => fallback,
        };
        self.record(key, &format!("fx:{}", value.raw()));
        Ok(value)
    }

    /// `Int(key, fallback ? 1 : 0) != 0`.
    pub(super) fn flag(&mut self, key: &str, fallback: bool) -> Result<bool, DataError> {
        Ok(self.int(key, i32::from(fallback))? != 0)
    }

    /// The first file key (in file order) that no field recorded.
    pub(super) fn unknown_key(&self) -> Option<&str> {
        self.order
            .iter()
            .find(|k| !self.effective.contains_key(*k))
            .map(String::as_str)
    }

    pub(super) fn into_effective(self) -> BTreeMap<String, String> {
        self.effective
    }
}
