//! The economy module: markets, event modifiers, currency, households and their cash (C# `FES.Economy`).
pub mod currency;
pub mod currency_system;
pub mod household_cash;
pub mod households;
pub mod layout;
pub mod markets;
pub mod modifier_fold;
pub mod modifiers;
pub mod pricing;
pub mod world_events;

use crate::error::SimError;
use crate::module::{Module, SimBuilder};
use crate::world::{NodeSpecialties, NodeTable};
use crate::SimClock;
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use std::sync::Arc;

pub use currency::{CurrencyState, GoldStandardCurrencyModel};
pub use currency_system::{CurrencyMinted, CurrencyModel, CurrencyModelService, CurrencySystem};
pub use household_cash::HouseholdCash;
pub use households::HouseholdState;
pub use layout::MarketLayoutBuilder;
pub use markets::{MarketFlows, MarketStore};
pub use modifier_fold::ModifierFoldSystem;
pub use modifiers::{MarketModifier, MarketModifierTable};
pub use pricing::{market_view, GoodQuote, MarketView, PricingSystem};
pub use world_events::{WorldEventStarted, WorldEventSystem};

/// Writes a column the way C# `NativeIO.WriteArray` does: its length, then the elements.
pub(crate) fn write_i32s(w: &mut DotNetWriter<Vec<u8>>, column: &[i32]) {
    let _ = w.write_i32(column.len() as i32);
    for v in column {
        let _ = w.write_i32(*v);
    }
}

pub(crate) fn write_i64s(w: &mut DotNetWriter<Vec<u8>>, column: &[i64]) {
    let _ = w.write_i32(column.len() as i32);
    for v in column {
        let _ = w.write_i64(*v);
    }
}

fn read_length(r: &mut DotNetReader, expected: usize) -> Result<(), IoError> {
    let length = r.read_i32()?;
    if length < 0 || length as usize != expected {
        return Err(IoError::InvalidLength);
    }
    Ok(())
}

/// Reads into a column whose length must match the saved one (C# `NativeIO.ReadArray`).
pub(crate) fn read_i32s(r: &mut DotNetReader, column: &mut [i32]) -> Result<(), IoError> {
    read_length(r, column.len())?;
    for v in column.iter_mut() {
        *v = r.read_i32()?;
    }
    Ok(())
}

pub(crate) fn read_i64s(r: &mut DotNetReader, column: &mut [i64]) -> Result<(), IoError> {
    read_length(r, column.len())?;
    for v in column.iter_mut() {
        *v = r.read_i64()?;
    }
    Ok(())
}

/// Registers markets, modifiers, flows, currency, households and household cash (C# `EconomyModule`).
/// Requires the world module. Registers the world-event, modifier-fold and pricing systems.
pub struct EconomyModule;

impl Module for EconomyModule {
    fn id(&self) -> &'static str {
        "economy"
    }

    fn requires(&self) -> &[&'static str] {
        &["world"]
    }

    fn register(&self, b: &mut SimBuilder) -> Result<(), SimError> {
        let db = Arc::clone(&b.services.database);
        let map = Arc::clone(&b.services.map);
        let seed = b.world.get::<SimClock>().seed;
        let nodes = b.services.get::<NodeTable>();
        let specialties = b.services.get::<NodeSpecialties>();
        let markets = MarketLayoutBuilder::build(nodes, &db, specialties)?;
        let flows = MarketFlows::new(nodes, db.categories.len());
        let mut currency = CurrencyState::new(nodes.continent_count() as usize);
        GoldStandardCurrencyModel::initialize(&mut currency, &db.config, seed);
        let households = HouseholdState::new(nodes, &map, &db.config);
        let cash = HouseholdCash::new(nodes.count(), markets.cell_count());
        b.services.add(flows)?;
        b.services
            .add(CurrencyModelService(Box::new(GoldStandardCurrencyModel)))?;
        b.world.add(markets)?;
        b.world.add(MarketModifierTable::default())?;
        b.world.add(currency)?;
        b.world.add(households)?;
        b.world.add(cash)?;
        b.add_system(Box::new(WorldEventSystem))?;
        b.add_system(Box::new(ModifierFoldSystem))?;
        b.add_system(Box::<PricingSystem>::default())?;
        b.add_system(Box::new(CurrencySystem))
    }
}
