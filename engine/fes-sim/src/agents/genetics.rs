//! How likely each trait is in a continent's next generation of merchant groups (C#
//! `MerchantGenetics`, section `agents.merchant_genetics`). Starts uniform.
use super::columns::{read_items, write_items};
use super::merchants::TRAIT_COUNT;
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MerchantGenetics {
    pub continent_count: i32,
    /// Raw Fx, indexed continent * traits + trait.
    pub weight: Vec<i64>,
    pub year_bankruptcies: Vec<i32>,
}

impl MerchantGenetics {
    pub fn new(continent_count: i32) -> Self {
        let cells = (continent_count * TRAIT_COUNT) as usize;
        Self {
            continent_count,
            weight: vec![Fx::ONE_RAW / i64::from(TRAIT_COUNT); cells],
            year_bankruptcies: vec![0; cells],
        }
    }
}

impl Component for MerchantGenetics {
    fn key(&self) -> &'static str {
        "agents.merchant_genetics"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.continent_count);
        write_items(w, &self.weight);
        write_items(w, &self.year_bankruptcies);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        if r.read_i32()? != self.continent_count {
            return Err(IoError::InvalidLength);
        }
        read_items(r, &mut self.weight)?;
        read_items(r, &mut self.year_bankruptcies)
    }
}
