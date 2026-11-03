use std::fmt;

/// A data loading failure. `Format` carries the same text as C#'s `FormatException`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DataError {
    Format(String),
    NotFound(String),
    Io(String),
}

impl fmt::Display for DataError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            DataError::Format(m) | DataError::NotFound(m) | DataError::Io(m) => f.write_str(m),
        }
    }
}

impl std::error::Error for DataError {}
