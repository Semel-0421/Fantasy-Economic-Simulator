//! .NET `BinaryWriter` / `BinaryReader` compatible binary IO (little-endian, 7-bit string lengths).
use crate::det::det_hash::{fnv_step, FNV_OFFSET};
use std::io::{self, Write};

/// Failure while reading .NET binary data.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum IoError {
    /// Fewer bytes remain than the read needs.
    UnexpectedEof,
    /// A 7-bit encoded length is malformed or too large.
    InvalidLength,
    /// A string's bytes are not valid UTF-8.
    InvalidUtf8,
    /// A bool byte other than 0 or 1.
    InvalidBool(u8),
}

impl std::fmt::Display for IoError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::UnexpectedEof => write!(f, "unexpected end of data"),
            Self::InvalidLength => write!(f, "invalid length prefix"),
            Self::InvalidUtf8 => write!(f, "invalid UTF-8 string"),
            Self::InvalidBool(b) => write!(f, "invalid bool byte {b}"),
        }
    }
}

impl std::error::Error for IoError {}

/// Writes primitives the way .NET `BinaryWriter` does.
pub struct DotNetWriter<W: Write> {
    inner: W,
}

impl<W: Write> DotNetWriter<W> {
    pub fn new(inner: W) -> Self {
        Self { inner }
    }

    pub fn into_inner(self) -> W {
        self.inner
    }

    pub fn write_u8(&mut self, v: u8) -> io::Result<()> {
        self.inner.write_all(&[v])
    }

    pub fn write_bool(&mut self, v: bool) -> io::Result<()> {
        self.write_u8(u8::from(v))
    }

    pub fn write_i32(&mut self, v: i32) -> io::Result<()> {
        self.inner.write_all(&v.to_le_bytes())
    }

    pub fn write_u32(&mut self, v: u32) -> io::Result<()> {
        self.inner.write_all(&v.to_le_bytes())
    }

    pub fn write_i64(&mut self, v: i64) -> io::Result<()> {
        self.inner.write_all(&v.to_le_bytes())
    }

    pub fn write_u64(&mut self, v: u64) -> io::Result<()> {
        self.inner.write_all(&v.to_le_bytes())
    }

    /// 7-bit encoded UTF-8 byte length, then the bytes.
    pub fn write_string(&mut self, s: &str) -> io::Result<()> {
        let mut n = s.len() as u32;
        while n >= 0x80 {
            self.write_u8((n & 0x7F) as u8 | 0x80)?;
            n >>= 7;
        }
        self.write_u8(n as u8)?;
        self.inner.write_all(s.as_bytes())
    }

    /// Raw bytes with no length prefix (`BinaryWriter.Write(byte[])`).
    pub fn write_bytes(&mut self, b: &[u8]) -> io::Result<()> {
        self.inner.write_all(b)
    }
}

/// Reads primitives the way .NET `BinaryReader` does, never panicking on bad input.
pub struct DotNetReader<'a> {
    data: &'a [u8],
    pos: usize,
}

impl<'a> DotNetReader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    pub fn position(&self) -> usize {
        self.pos
    }

    pub fn remaining(&self) -> usize {
        self.data.len() - self.pos
    }

    /// Takes `n` raw bytes.
    pub fn read_bytes(&mut self, n: usize) -> Result<&'a [u8], IoError> {
        if n > self.remaining() {
            return Err(IoError::UnexpectedEof);
        }
        let out = &self.data[self.pos..self.pos + n];
        self.pos += n;
        Ok(out)
    }

    fn array<const N: usize>(&mut self) -> Result<[u8; N], IoError> {
        let mut out = [0u8; N];
        out.copy_from_slice(self.read_bytes(N)?);
        Ok(out)
    }

    pub fn read_u8(&mut self) -> Result<u8, IoError> {
        Ok(self.array::<1>()?[0])
    }

    pub fn read_bool(&mut self) -> Result<bool, IoError> {
        match self.read_u8()? {
            0 => Ok(false),
            1 => Ok(true),
            b => Err(IoError::InvalidBool(b)),
        }
    }

    pub fn read_i32(&mut self) -> Result<i32, IoError> {
        Ok(i32::from_le_bytes(self.array()?))
    }

    pub fn read_u32(&mut self) -> Result<u32, IoError> {
        Ok(u32::from_le_bytes(self.array()?))
    }

    pub fn read_i64(&mut self) -> Result<i64, IoError> {
        Ok(i64::from_le_bytes(self.array()?))
    }

    pub fn read_u64(&mut self) -> Result<u64, IoError> {
        Ok(u64::from_le_bytes(self.array()?))
    }

    fn read_7bit_len(&mut self) -> Result<usize, IoError> {
        let mut value: u32 = 0;
        for shift in (0..35).step_by(7) {
            let b = self.read_u8()?;
            if shift == 28 && b > 0x07 {
                return Err(IoError::InvalidLength);
            }
            value |= u32::from(b & 0x7F) << shift;
            if b & 0x80 == 0 {
                return Ok(value as usize);
            }
        }
        Err(IoError::InvalidLength)
    }

    pub fn read_string(&mut self) -> Result<String, IoError> {
        let n = self.read_7bit_len()?;
        let bytes = self.read_bytes(n)?;
        String::from_utf8(bytes.to_vec()).map_err(|_| IoError::InvalidUtf8)
    }
}

/// `Write` sink that accumulates an FNV-1a 64-bit hash (C# `HashingStream`).
pub struct FnvSink {
    hash: u64,
}

impl FnvSink {
    pub fn new() -> Self {
        Self { hash: FNV_OFFSET }
    }

    pub fn hash(&self) -> u64 {
        self.hash
    }
}

impl Default for FnvSink {
    fn default() -> Self {
        Self::new()
    }
}

impl Write for FnvSink {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        for &b in buf {
            self.hash = fnv_step(self.hash, b);
        }
        Ok(buf.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
