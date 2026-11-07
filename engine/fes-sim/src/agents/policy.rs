//! Policy as the yearly reviews left it (C# `PolicyState.cs`, `agents.policy`, version 1).
use super::columns::{read_col, read_list, write_col};
use super::development::checked_count;
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{GoodId, QualityGrade};
use fes_data::database::GameDatabase;

/// Money the player spent lobbying this year (C# `LobbyEntry`); `policy` is the `PolicyKind` byte.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LobbyEntry {
    pub group: i32,
    pub nation: i32,
    pub policy: u8,
    /// For licensed goods the base good, otherwise -1.
    pub base_good: i32,
    pub direction: i32,
    pub amount: i64,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PolicyState {
    pub nation_count: usize,
    pub continent_count: usize,
    pub base_good_count: usize,
    pub toll_step: Vec<i32>,
    pub license_price_step: Vec<i32>,
    pub mint_step: Vec<i32>,
    /// Per (nation row, base good): 1 when selling it needs a license. Row `nation_count` holds
    /// the goods.csv flags for nodes outside any nation.
    pub licensed: Vec<u8>,
    /// Per group: cash of the group and everything it owns at the last review.
    pub last_worth: Vec<i64>,
    pub lobbying: Vec<LobbyEntry>,
    /// Per nation: total influence at the last review.
    pub last_influence: Vec<i64>,
}

impl PolicyState {
    pub fn new(nation_count: usize, continent_count: usize, db: &GameDatabase) -> Self {
        let bases = db.base_good_count();
        let mut licensed = Vec::with_capacity((nation_count + 1) * bases);
        for _row in 0..=nation_count {
            for b in 0..bases {
                let good = GoodId::from(b as i32, QualityGrade::Mid);
                licensed.push(u8::from(db.good(good).traits.requires_license));
            }
        }
        Self {
            nation_count,
            continent_count,
            base_good_count: bases,
            toll_step: vec![0; nation_count],
            license_price_step: vec![0; nation_count],
            mint_step: vec![0; continent_count],
            licensed,
            last_worth: Vec::new(),
            lobbying: Vec::new(),
            last_influence: vec![0; nation_count],
        }
    }
}

impl Component for PolicyState {
    fn key(&self) -> &'static str {
        "agents.policy"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_col(w, &self.toll_step);
        write_col(w, &self.license_price_step);
        write_col(w, &self.mint_step);
        write_col(w, &self.licensed);
        write_col(w, &self.last_worth);
        let _ = w.write_i32(self.lobbying.len() as i32);
        for e in &self.lobbying {
            let _ = w.write_i32(e.group);
            let _ = w.write_i32(e.nation);
            let _ = w.write_u8(e.policy);
            let _ = w.write_i32(e.base_good);
            let _ = w.write_i32(e.direction);
            let _ = w.write_i64(e.amount);
        }
        write_col(w, &self.last_influence);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_col(r, &mut self.toll_step)?;
        read_col(r, &mut self.license_price_step)?;
        read_col(r, &mut self.mint_step)?;
        read_col(r, &mut self.licensed)?;
        read_list(r, &mut self.last_worth)?;
        self.lobbying.clear();
        for _ in 0..checked_count(r, 25)? {
            self.lobbying.push(LobbyEntry {
                group: r.read_i32()?,
                nation: r.read_i32()?,
                policy: r.read_u8()?,
                base_good: r.read_i32()?,
                direction: r.read_i32()?,
                amount: r.read_i64()?,
            });
        }
        read_col(r, &mut self.last_influence)
    }
}
