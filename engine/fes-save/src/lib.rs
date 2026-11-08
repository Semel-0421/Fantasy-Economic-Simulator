//! Save container matching C# `SaveSerializer`: header, then (key, version, length, bytes) sections.
#![deny(clippy::float_arithmetic)]

use fes_core::io::{DotNetReader, DotNetWriter, FnvSink, IoError};
use std::io::Write;

const MAGIC: u32 = 0x5345_4646;
/// The save container format this crate writes and reads.
pub const FORMAT_VERSION: i32 = 1;

/// The fixed-size save header.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SaveHeader {
    pub format_version: i32,
    pub data_hash: u64,
    pub seed: u64,
    pub turn: i32,
}

/// One persistent component's serialized bytes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RawSection<B = Vec<u8>> {
    pub key: String,
    pub version: i32,
    pub bytes: B,
}

/// A section whose payload refers directly to the input save buffer.
pub type BorrowedSection<'a> = RawSection<&'a [u8]>;

/// Failure while reading a save.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SaveError {
    BadMagic,
    UnsupportedFormat(i32),
    NegativeCount(i32),
    Io(IoError),
}

impl From<IoError> for SaveError {
    fn from(e: IoError) -> Self {
        Self::Io(e)
    }
}

impl std::fmt::Display for SaveError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::BadMagic => write!(f, "not a save file"),
            Self::UnsupportedFormat(v) => write!(f, "unsupported save format {v}"),
            Self::NegativeCount(n) => write!(f, "negative count or length {n}"),
            Self::Io(e) => write!(f, "{e}"),
        }
    }
}

impl std::error::Error for SaveError {}

/// Serializes a save. Sections are written in the order given (callers pass key order).
pub fn write_save(header: &SaveHeader, sections: &[RawSection]) -> Vec<u8> {
    let mut w = DotNetWriter::new(Vec::new());
    // Writing to a Vec cannot fail.
    let _ = write_all(&mut w, header, sections);
    w.into_inner()
}

fn write_all(
    w: &mut DotNetWriter<Vec<u8>>,
    header: &SaveHeader,
    sections: &[RawSection],
) -> std::io::Result<()> {
    w.write_u32(MAGIC)?;
    w.write_i32(header.format_version)?;
    w.write_u64(header.data_hash)?;
    w.write_u64(header.seed)?;
    w.write_i32(header.turn)?;
    w.write_i32(len_i32(sections.len(), "section count"))?;
    for s in sections {
        w.write_string(&s.key)?;
        w.write_i32(s.version)?;
        w.write_i32(len_i32(s.bytes.len(), "section length"))?;
        w.write_bytes(&s.bytes)?;
    }
    Ok(())
}

fn len_i32(len: usize, what: &str) -> i32 {
    i32::try_from(len).unwrap_or_else(|_| panic!("{what} {len} exceeds i32::MAX"))
}

/// Parses a save; any malformed input is an error.
pub fn read_save(data: &[u8]) -> Result<(SaveHeader, Vec<RawSection>), SaveError> {
    let (header, sections) = read_save_borrowed(data)?;
    Ok((
        header,
        sections
            .into_iter()
            .map(|s| RawSection {
                key: s.key,
                version: s.version,
                bytes: s.bytes.to_vec(),
            })
            .collect(),
    ))
}

/// Parses and validates the container without copying its section payloads.
pub fn read_save_borrowed(
    data: &[u8],
) -> Result<(SaveHeader, Vec<BorrowedSection<'_>>), SaveError> {
    let mut r = DotNetReader::new(data);
    if r.read_u32()? != MAGIC {
        return Err(SaveError::BadMagic);
    }
    let format_version = r.read_i32()?;
    if format_version != FORMAT_VERSION {
        return Err(SaveError::UnsupportedFormat(format_version));
    }
    let header = SaveHeader {
        format_version,
        data_hash: r.read_u64()?,
        seed: r.read_u64()?,
        turn: r.read_i32()?,
    };
    let count = r.read_i32()?;
    if count < 0 {
        return Err(SaveError::NegativeCount(count));
    }
    let mut sections = Vec::new();
    for _ in 0..count {
        sections.push(read_section(&mut r)?);
    }
    Ok((header, sections))
}

fn read_section<'a>(r: &mut DotNetReader<'a>) -> Result<BorrowedSection<'a>, SaveError> {
    let key = r.read_string()?;
    let version = r.read_i32()?;
    let len = r.read_i32()?;
    if len < 0 {
        return Err(SaveError::NegativeCount(len));
    }
    let bytes = r.read_bytes(len as usize)?;
    Ok(RawSection {
        key,
        version,
        bytes,
    })
}

/// FNV-1a over every section (sorted by key, ordinal): key string, version, bytes.
/// Equals C# `WorldState.ComputeHash`.
pub fn world_hash(sections: &[RawSection]) -> u64 {
    let mut sorted: Vec<&RawSection> = sections.iter().collect();
    // C# StringComparer.Ordinal compares UTF-16 code units, not UTF-8 bytes.
    sorted.sort_by(|a, b| a.key.encode_utf16().cmp(b.key.encode_utf16()));
    let mut w = DotNetWriter::new(FnvSink::new());
    for s in sorted {
        // FnvSink never fails.
        let _ = w.write_string(&s.key);
        let _ = w.write_i32(s.version);
        let _ = w.write_bytes(&s.bytes);
    }
    w.into_inner().hash()
}

/// FNV-1a of the section bytes only (the `turns.json` convention).
pub fn section_hash(section: &RawSection) -> u64 {
    let mut sink = FnvSink::new();
    let _ = sink.write_all(&section.bytes);
    sink.hash()
}
