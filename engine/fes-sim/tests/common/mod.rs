//! Sample-world helpers shared by the merchant tests (C# `TestWorlds`).
#![allow(dead_code)]
use fes_data::database::CONFIG_FILE;
use fes_data::{DataError, DataSource, FileDataSource};
use fes_sim::{Simulation, SimulationBuilder};
use std::path::PathBuf;

pub fn data_dir() -> PathBuf {
    PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ))
}

/// The shipped data with `config.csv` replaced.
pub struct Sample {
    inner: FileDataSource,
    config: String,
}

impl DataSource for Sample {
    fn exists(&self, name: &str) -> bool {
        self.inner.exists(name)
    }

    fn read_text(&self, name: &str) -> Result<String, DataError> {
        if name == CONFIG_FILE {
            Ok(self.config.clone())
        } else {
            self.inner.read_text(name)
        }
    }
}

/// C# `TestWorlds.SampleConfig`: the shipped config with these `key,value` rows replaced.
pub fn sample_config(lines: &[&str]) -> Sample {
    let inner = FileDataSource::new(data_dir());
    let base = inner.read_text(CONFIG_FILE).expect("config.csv");
    let mut kept: Vec<&str> = base.lines().collect();
    for line in lines {
        let prefix = format!("{},", line.split(',').next().unwrap_or(""));
        kept.retain(|l| !l.starts_with(&prefix));
    }
    let mut config = kept.join("\n").trim_end().to_string();
    for line in lines {
        config.push('\n');
        config.push_str(line);
    }
    config.push('\n');
    Sample { inner, config }
}

/// C# `TestWorlds.SampleWith(ConfigFile, text)`: the whole config replaced by `text`.
pub fn sample_with_config(text: &str) -> Sample {
    Sample {
        inner: FileDataSource::new(data_dir()),
        config: text.to_string(),
    }
}

pub fn build(source: &dyn DataSource, seed: u64) -> Simulation {
    SimulationBuilder::from_data(source, seed)
        .expect("builder")
        .build()
        .expect("build")
}

/// C# `TestWorlds.BuildSample`.
pub fn build_sample(lines: &[&str]) -> Simulation {
    build(&sample_config(lines), 42)
}

/// C# `MoneyLedger.ContinentMoney(sim.World, continent)`.
pub fn continent_money(sim: &Simulation, continent: usize) -> i64 {
    fes_sim::agents::seeding::continent_money(
        sim.world(),
        sim.context().services.get::<fes_sim::world::NodeTable>(),
        continent,
    )
}

/// The continent count of the built world.
pub fn continent_count(sim: &Simulation) -> usize {
    sim.world()
        .get::<fes_sim::economy::CurrencyState>()
        .continent_count()
}
