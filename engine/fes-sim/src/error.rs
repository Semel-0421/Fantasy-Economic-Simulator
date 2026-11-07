//! The error type shared by the simulation crates.
use fes_core::io::IoError;
use fes_save::SaveError;

/// Failure while building, loading or stepping the simulation.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SimError {
    /// A component with this save key (or of this type) is already registered.
    DuplicateKey(String),
    /// A save section has no matching component.
    UnknownSection(String),
    /// A component has no section in the save.
    MissingSection(String),
    /// A section's bytes could not be read.
    Io(IoError),
    /// The save file itself is malformed.
    Save(SaveError),
    /// Any other invalid input, described in the message.
    Invalid(String),
}

impl std::fmt::Display for SimError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::DuplicateKey(k) => write!(f, "component '{k}' is already registered"),
            Self::UnknownSection(k) => write!(f, "save section '{k}' has no component"),
            Self::MissingSection(k) => write!(f, "save has no section '{k}'"),
            Self::Io(e) => write!(f, "section read failed: {e}"),
            Self::Save(e) => write!(f, "save error: {e}"),
            Self::Invalid(m) => write!(f, "{m}"),
        }
    }
}

impl std::error::Error for SimError {}

impl From<IoError> for SimError {
    fn from(e: IoError) -> Self {
        Self::Io(e)
    }
}

impl From<SaveError> for SimError {
    fn from(e: SaveError) -> Self {
        Self::Save(e)
    }
}
