//! Column and array layouts of C# `NativeIO` (`WriteArray`/`ReadList`) and the hand-written
//! `BinaryWriter` loops of the agents components, plus `native_table!` for C# `NativeTable` classes.
use fes_core::io::{DotNetReader, DotNetWriter, IoError};

/// A fixed-size element of a saved column.
pub trait Elem: Copy + Default {
    const SIZE: usize;
    fn put(self, w: &mut DotNetWriter<Vec<u8>>);
    fn get(r: &mut DotNetReader) -> Result<Self, IoError>;
}

impl Elem for i32 {
    const SIZE: usize = 4;
    fn put(self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self);
    }
    fn get(r: &mut DotNetReader) -> Result<Self, IoError> {
        r.read_i32()
    }
}

impl Elem for i64 {
    const SIZE: usize = 8;
    fn put(self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i64(self);
    }
    fn get(r: &mut DotNetReader) -> Result<Self, IoError> {
        r.read_i64()
    }
}

impl Elem for u8 {
    const SIZE: usize = 1;
    fn put(self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_u8(self);
    }
    fn get(r: &mut DotNetReader) -> Result<Self, IoError> {
        r.read_u8()
    }
}

/// Writes the elements only (a C# `foreach (x) writer.Write(x)` loop).
pub fn write_items<T: Elem>(w: &mut DotNetWriter<Vec<u8>>, items: &[T]) {
    for v in items {
        v.put(w);
    }
}

/// Reads into a slice of fixed length, without a length prefix.
pub fn read_items<T: Elem>(r: &mut DotNetReader, items: &mut [T]) -> Result<(), IoError> {
    for v in items.iter_mut() {
        *v = T::get(r)?;
    }
    Ok(())
}

/// Writes the length, then the elements (`NativeIO.WriteArray`, `writer.Write(Length)` loops).
pub fn write_col<T: Elem>(w: &mut DotNetWriter<Vec<u8>>, items: &[T]) {
    let _ = w.write_i32(items.len() as i32);
    write_items(w, items);
}

/// Reads a length that must equal `expected` (C# `ReadArray` and the `Saved ... count` checks).
pub fn read_len(r: &mut DotNetReader, expected: usize) -> Result<(), IoError> {
    let length = r.read_i32()?;
    if length < 0 || length as usize != expected {
        return Err(IoError::InvalidLength);
    }
    Ok(())
}

/// Reads length and elements into a column of the same length.
pub fn read_col<T: Elem>(r: &mut DotNetReader, items: &mut [T]) -> Result<(), IoError> {
    read_len(r, items.len())?;
    read_items(r, items)
}

/// Replaces a growable column by the saved one (`NativeIO.ReadList`).
pub fn read_list<T: Elem>(r: &mut DotNetReader, items: &mut Vec<T>) -> Result<(), IoError> {
    let length = r.read_i32()?;
    if length < 0 {
        return Err(IoError::InvalidLength);
    }
    let length = length as usize;
    if length.saturating_mul(T::SIZE) > r.remaining() {
        return Err(IoError::UnexpectedEof);
    }
    items.clear();
    items.reserve(length);
    for _ in 0..length {
        items.push(T::get(r)?);
    }
    Ok(())
}

/// Defines a C# `NativeTable`: a struct of parallel columns saved one after another, each as
/// its length and elements, in declaration order. Rows are added by the systems that own the table.
macro_rules! native_table {
    (
        $(#[$meta:meta])*
        $name:ident, $key:expr, $version:expr,
        { $( $(#[$fmeta:meta])* $field:ident : $ty:ty ),+ $(,)? }
    ) => {
        $(#[$meta])*
        #[derive(Clone, Debug, Default, PartialEq, Eq)]
        pub struct $name {
            $( $(#[$fmeta])* pub $field: Vec<$ty>, )+
        }

        impl $name {
            /// Number of rows.
            pub fn count(&self) -> usize {
                [$(self.$field.len()),+][0]
            }
        }

        impl $crate::world::Component for $name {
            fn key(&self) -> &'static str {
                $key
            }

            fn version(&self) -> i32 {
                $version
            }

            fn write(&self, w: &mut fes_core::io::DotNetWriter<Vec<u8>>) {
                $( $crate::agents::columns::write_col(w, &self.$field); )+
            }

            fn read(
                &mut self,
                r: &mut fes_core::io::DotNetReader,
                _version: i32,
            ) -> Result<(), fes_core::io::IoError> {
                $( $crate::agents::columns::read_list(r, &mut self.$field)?; )+
                Ok(())
            }
        }
    };
}
pub(crate) use native_table;
