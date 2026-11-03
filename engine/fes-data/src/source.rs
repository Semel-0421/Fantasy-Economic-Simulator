use crate::error::DataError;
use std::collections::HashMap;
use std::path::PathBuf;

/// Named text files, read from disk or memory.
pub trait DataSource {
    fn exists(&self, name: &str) -> bool;
    fn read_text(&self, name: &str) -> Result<String, DataError>;
}

/// Reads files under a root directory.
pub struct FileDataSource {
    root: PathBuf,
}

impl FileDataSource {
    pub fn new(root: PathBuf) -> Self {
        Self { root }
    }
}

impl DataSource for FileDataSource {
    fn exists(&self, name: &str) -> bool {
        self.root.join(name).is_file()
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        let path = self.root.join(name);
        let text = std::fs::read_to_string(&path).map_err(|e| {
            if e.kind() == std::io::ErrorKind::NotFound {
                DataError::NotFound(format!("{name}: file not found."))
            } else {
                DataError::Io(format!("{name}: {e}"))
            }
        })?;
        // C# File.ReadAllText drops a leading byte order mark, so this does too.
        Ok(match text.strip_prefix('\u{feff}') {
            Some(rest) => rest.to_string(),
            None => text,
        })
    }
}

/// Holds files in memory, for tests.
#[derive(Default)]
pub struct InMemoryDataSource {
    files: HashMap<String, String>,
}

impl InMemoryDataSource {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn add(&mut self, name: &str, text: &str) {
        self.files.insert(name.to_string(), text.to_string());
    }
}

impl DataSource for InMemoryDataSource {
    fn exists(&self, name: &str) -> bool {
        self.files.contains_key(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        self.files
            .get(name)
            .cloned()
            .ok_or_else(|| DataError::NotFound(format!("{name}: file not found.")))
    }
}
