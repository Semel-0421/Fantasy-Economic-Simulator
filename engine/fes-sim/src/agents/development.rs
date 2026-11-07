//! What the states have built and the projects under way (C# `DevelopmentState.cs`,
//! `agents.development`, version 1).
use super::columns::{read_col, read_items, write_col, write_items};
use crate::world::Component;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

/// A state project under way (C# `StateProject`); `kind` is the `StateProjectKind` byte.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct StateProject {
    pub kind: u8,
    pub nation: i32,
    pub node: i32,
    /// The route, or -1 for a project on a node.
    pub route: i32,
    pub def_index: i32,
    /// -1 while reserved.
    pub done_turn: i32,
}

/// A nation pays producers of a good by what they make, until `end_year`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct SubsidyPolicy {
    pub nation: i32,
    pub good: i32,
    pub end_year: i32,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DevelopmentState {
    pub land_level: Vec<i32>,
    pub harbor_level: Vec<i32>,
    /// Per route: 1 when it is a highway.
    pub route_grade: Vec<i32>,
    pub year_budget: Vec<i64>,
    pub defense_spent: Vec<i64>,
    /// Raw Fx, per nation.
    pub security: Vec<i64>,
    /// Per float: value paid this year to other floats for goods flowing in.
    pub imports: Vec<i64>,
    pub projects: Vec<StateProject>,
    pub subsidies: Vec<SubsidyPolicy>,
    /// Raw Fx, indexed float * goods + good. Not saved.
    pub subsidy_rate: Vec<i64>,
    /// Per float: what this week's subsidies may still pay. Not saved.
    pub subsidy_left: Vec<i64>,
}

impl DevelopmentState {
    pub fn new(nodes: usize, routes: usize, nations: usize, floats: usize, goods: usize) -> Self {
        Self {
            land_level: vec![0; nodes],
            harbor_level: vec![0; nodes],
            route_grade: vec![0; routes],
            year_budget: vec![0; nations],
            defense_spent: vec![0; nations],
            security: vec![0; nations],
            imports: vec![0; floats],
            projects: Vec::new(),
            subsidies: Vec::new(),
            subsidy_rate: vec![0; floats * goods],
            subsidy_left: vec![0; floats],
        }
    }
}

impl Component for DevelopmentState {
    fn key(&self) -> &'static str {
        "agents.development"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        write_items(w, &self.land_level);
        write_items(w, &self.harbor_level);
        write_items(w, &self.route_grade);
        write_items(w, &self.year_budget);
        write_items(w, &self.defense_spent);
        write_items(w, &self.security);
        let _ = w.write_i32(self.subsidies.len() as i32);
        for p in &self.subsidies {
            let _ = w.write_i32(p.nation);
            let _ = w.write_i32(p.good);
            let _ = w.write_i32(p.end_year);
        }
        write_col(w, &self.imports);
        let _ = w.write_i32(self.projects.len() as i32);
        for p in &self.projects {
            let _ = w.write_u8(p.kind);
            let _ = w.write_i32(p.nation);
            let _ = w.write_i32(p.node);
            let _ = w.write_i32(p.route);
            let _ = w.write_i32(p.def_index);
            let _ = w.write_i32(p.done_turn);
        }
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_items(r, &mut self.land_level)?;
        read_items(r, &mut self.harbor_level)?;
        read_items(r, &mut self.route_grade)?;
        read_items(r, &mut self.year_budget)?;
        read_items(r, &mut self.defense_spent)?;
        read_items(r, &mut self.security)?;
        self.subsidies.clear();
        for _ in 0..checked_count(r, 12)? {
            self.subsidies.push(SubsidyPolicy {
                nation: r.read_i32()?,
                good: r.read_i32()?,
                end_year: r.read_i32()?,
            });
        }
        read_col(r, &mut self.imports)?;
        self.projects.clear();
        for _ in 0..checked_count(r, 21)? {
            self.projects.push(StateProject {
                kind: r.read_u8()?,
                nation: r.read_i32()?,
                node: r.read_i32()?,
                route: r.read_i32()?,
                def_index: r.read_i32()?,
                done_turn: r.read_i32()?,
            });
        }
        Ok(())
    }
}

/// Reads a list count and checks the bytes for that many `size`-byte records remain.
pub(super) fn checked_count(r: &mut DotNetReader, size: usize) -> Result<usize, IoError> {
    let count = r.read_i32()?;
    if count < 0 {
        return Err(IoError::InvalidLength);
    }
    if (count as usize).saturating_mul(size) > r.remaining() {
        return Err(IoError::UnexpectedEof);
    }
    Ok(count as usize)
}
