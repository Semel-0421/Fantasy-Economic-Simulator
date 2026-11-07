//! How likely each trait is in a continent's next generation of merchant groups (C#
//! `MerchantGenetics`, section `agents.merchant_genetics`). Starts uniform.
use super::columns::{read_items, write_items};
use super::merchants::TRAIT_COUNT;
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{DetRandom, Fx};

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

impl MerchantGenetics {
    /// C# `RecordBankruptcy`.
    pub fn record_bankruptcy(&mut self, continent: i32, trait_id: u8) {
        self.year_bankruptcies[(continent * TRAIT_COUNT) as usize + usize::from(trait_id)] += 1;
    }

    /// C# `Draw`: any trait with the mutation chance, else by the continent's weights.
    pub fn draw(&self, rng: &mut DetRandom, continent: i32, mutation: Fx) -> u8 {
        if rng.next_fx01().raw() < mutation.raw() {
            return rng.next_int(TRAIT_COUNT) as u8;
        }
        let first = (continent * TRAIT_COUNT) as usize;
        let weights = &self.weight[first..first + TRAIT_COUNT as usize];
        let total: i64 = weights.iter().sum();
        if total <= 0 {
            return rng.next_int(TRAIT_COUNT) as u8;
        }
        let mut pick = (u64::from(rng.next_u32()) % total as u64) as i64;
        for (t, w) in weights.iter().enumerate() {
            pick -= w;
            if pick < 0 {
                return t as u8;
            }
        }
        (TRAIT_COUNT - 1) as u8
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
