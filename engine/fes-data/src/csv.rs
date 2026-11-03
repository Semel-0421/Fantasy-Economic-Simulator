//! Minimal RFC-4180-style CSV reader matching C# `CsvTable`: header row, quoted fields (a quote only
//! opens a field when it is the field's first character), `#` comment lines, blank lines skipped.
use crate::error::DataError;
use std::collections::HashMap;

/// An enum parsed from a CSV cell by name.
pub trait CsvEnum: Sized {
    /// The C# type name used in error messages.
    const TYPE_NAME: &'static str;
    /// Looks a variant up by name, ignoring case; `None` when not defined.
    fn from_name(name: &str) -> Option<Self>;
}

/// Parses an enum by name only (numeric strings such as "9" are rejected).
pub fn try_parse_enum_name<T: CsvEnum>(s: &str) -> Option<T> {
    let first = s.chars().next()?;
    if first.is_ascii_digit() || first == '-' || first == '+' {
        return None;
    }
    T::from_name(s)
}

pub struct CsvTable {
    name: String,
    columns: HashMap<String, usize>,
    rows: Vec<Vec<String>>,
}

impl CsvTable {
    pub fn parse(name: &str, text: &str) -> Result<CsvTable, DataError> {
        let mut table = CsvTable {
            name: name.to_string(),
            columns: HashMap::new(),
            rows: Vec::new(),
        };
        let mut reader = RowReader::new(name, text);
        let header = reader.next_row()?.unwrap_or_default();
        for (i, f) in header.iter().enumerate() {
            table.columns.insert(f.trim().to_string(), i);
        }
        while let Some(fields) = reader.next_row()? {
            table.rows.push(fields);
        }
        Ok(table)
    }

    pub fn name(&self) -> &str {
        &self.name
    }

    pub fn row_count(&self) -> usize {
        self.rows.len()
    }

    pub fn has_column(&self, column: &str) -> bool {
        self.columns.contains_key(column)
    }

    pub fn get_string(&self, row: usize, column: &str) -> Result<String, DataError> {
        let index = *self.columns.get(column).ok_or_else(|| {
            DataError::Format(format!("{}: missing column '{column}'.", self.name))
        })?;
        let fields = self.rows.get(row).ok_or_else(|| {
            DataError::Format(format!("{} row {}: no such row.", self.name, row + 1))
        })?;
        Ok(fields.get(index).map_or("", |f| f.trim()).to_string())
    }

    pub fn get_string_or(
        &self,
        row: usize,
        column: &str,
        fallback: &str,
    ) -> Result<String, DataError> {
        if self.has_column(column) {
            let s = self.get_string(row, column)?;
            if !s.is_empty() {
                return Ok(s);
            }
        }
        Ok(fallback.to_string())
    }

    pub fn get_int(&self, row: usize, column: &str) -> Result<i32, DataError> {
        let value = self.get_long(row, column)?;
        i32::try_from(value).map_err(|_| {
            DataError::Format(format!(
                "{} row {}: '{column}' = {value} is out of int range.",
                self.name,
                row + 1
            ))
        })
    }

    pub fn get_int_or(&self, row: usize, column: &str, fallback: i32) -> Result<i32, DataError> {
        if self.has_column(column) && !self.get_string(row, column)?.is_empty() {
            return self.get_int(row, column);
        }
        Ok(fallback)
    }

    pub fn get_long(&self, row: usize, column: &str) -> Result<i64, DataError> {
        let s = self.get_string(row, column)?;
        parse_integer(&s).ok_or_else(|| {
            DataError::Format(format!(
                "{} row {}: '{column}' = '{s}' is not an integer.",
                self.name,
                row + 1
            ))
        })
    }

    pub fn get_enum<T: CsvEnum>(&self, row: usize, column: &str) -> Result<T, DataError> {
        let s = self.get_string(row, column)?;
        try_parse_enum_name::<T>(&s).ok_or_else(|| {
            DataError::Format(format!(
                "{} row {}: '{column}' = '{s}' is not a valid {}.",
                self.name,
                row + 1,
                T::TYPE_NAME
            ))
        })
    }
}

/// `NumberStyles.Integer`: optional surrounding whitespace, optional sign, ASCII digits.
fn parse_integer(s: &str) -> Option<i64> {
    let t = s.trim();
    let digits = t.strip_prefix(['+', '-']).unwrap_or(t);
    if digits.is_empty() || !digits.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    t.strip_prefix('+').unwrap_or(t).parse().ok()
}

/// Reads the text one row at a time (the C# `RowReader` state machine).
struct RowReader<'a> {
    name: &'a str,
    text: Vec<char>,
    fields: Vec<String>,
    field: String,
    in_quotes: bool,
    field_started: bool,
    row_started: bool,
    i: usize,
    line: usize,
}

impl<'a> RowReader<'a> {
    fn new(name: &'a str, text: &str) -> Self {
        Self {
            name,
            text: text.chars().collect(),
            fields: Vec::new(),
            field: String::new(),
            in_quotes: false,
            field_started: false,
            row_started: false,
            i: 0,
            line: 1,
        }
    }

    /// The next row, or `None` at the end of the text.
    fn next_row(&mut self) -> Result<Option<Vec<String>>, DataError> {
        let mut row = None;
        while row.is_none() && self.i < self.text.len() {
            row = self.step();
            self.i += 1;
        }
        if row.is_some() {
            return Ok(row);
        }
        if self.in_quotes {
            return Err(DataError::Format(format!(
                "{} line {}: unterminated quoted field.",
                self.name, self.line
            )));
        }
        Ok(self.take_row())
    }

    fn step(&mut self) -> Option<Vec<String>> {
        let c = self.text[self.i];
        if self.in_quotes {
            self.read_quoted(c);
        } else if c == '#' && !self.row_started && self.fields.is_empty() && !self.field_started {
            self.skip_comment();
        } else if c == '"' && !self.field_started {
            self.open_quote();
        } else if c == ',' {
            self.end_field();
        } else if c == '\r' || c == '\n' {
            return self.end_line(c);
        } else {
            self.read_plain(c);
        }
        None
    }

    fn read_quoted(&mut self, c: char) {
        if c == '"' && self.text.get(self.i + 1) == Some(&'"') {
            self.field.push('"');
            self.i += 1;
        } else if c == '"' {
            self.in_quotes = false;
        } else {
            if c == '\n' {
                self.line += 1;
            }
            self.field.push(c);
        }
    }

    fn skip_comment(&mut self) {
        while self.i < self.text.len() && self.text[self.i] != '\n' {
            self.i += 1;
        }
        self.line += 1;
        self.field.clear();
    }

    fn open_quote(&mut self) {
        self.in_quotes = true;
        self.field_started = true;
        self.row_started = true;
        self.field.clear();
    }

    fn end_field(&mut self) {
        self.fields.push(std::mem::take(&mut self.field));
        self.field_started = false;
        self.row_started = true;
    }

    fn end_line(&mut self, c: char) -> Option<Vec<String>> {
        if c == '\n' {
            self.line += 1;
        }
        self.take_row()
    }

    fn read_plain(&mut self, c: char) {
        if !c.is_whitespace() {
            self.field_started = true;
            self.row_started = true;
        }
        self.field.push(c);
    }

    /// Ends the row and returns it, or `None` when it held nothing.
    fn take_row(&mut self) -> Option<Vec<String>> {
        let mut row = None;
        if self.row_started || self.field_started {
            self.fields.push(std::mem::take(&mut self.field));
            row = Some(std::mem::take(&mut self.fields));
        }
        self.fields.clear();
        self.field.clear();
        self.field_started = false;
        self.row_started = false;
        row
    }
}
