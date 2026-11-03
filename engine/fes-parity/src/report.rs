//! Plain-text parity report: one line per section, then a summary.
use std::fmt::Write;

/// How one save section compares to the Rust build of it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SectionStatus {
    NotPorted,
    Equal,
    /// Offset of the first differing byte (the shorter length when one is a prefix of the other).
    Different(usize),
}

/// Compares Rust bytes to the fixture's bytes.
pub fn compare(actual: &[u8], expected: &[u8]) -> SectionStatus {
    match actual.iter().zip(expected).position(|(a, e)| a != e) {
        Some(offset) => SectionStatus::Different(offset),
        None if actual.len() == expected.len() => SectionStatus::Equal,
        None => SectionStatus::Different(actual.len().min(expected.len())),
    }
}

/// Section counts of one run.
#[derive(Debug, Default, Clone, Copy)]
pub struct Tally {
    pub ported: usize,
    pub not_ported: usize,
    pub different: usize,
}

impl Tally {
    pub fn add(&mut self, status: &SectionStatus) {
        match status {
            SectionStatus::NotPorted => self.not_ported += 1,
            SectionStatus::Equal => self.ported += 1,
            SectionStatus::Different(_) => self.different += 1,
        }
    }
}

pub fn section_line(out: &mut String, key: &str, status: &SectionStatus) {
    let text = match status {
        SectionStatus::NotPorted => "not-ported".to_string(),
        SectionStatus::Equal => "ported-equal".to_string(),
        SectionStatus::Different(o) => format!("ported-different (first differing byte {o})"),
    };
    let _ = writeln!(out, "  {key}: {text}");
}

pub fn summary_line(out: &mut String, t: &Tally) {
    let _ = writeln!(
        out,
        "summary: {} ported, {} not ported, {} different",
        t.ported, t.not_ported, t.different
    );
}
