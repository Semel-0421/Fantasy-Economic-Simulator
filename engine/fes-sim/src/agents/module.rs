//! The agents module (C# `AgentsModule.Register`): every agents component, in C# order.
use super::cargo::CargoTable;
use super::facilities::FacilityTable;
use super::genetics::MerchantGenetics;
use super::groups::{GroupTable, GuildMembershipTable};
use super::licenses::LicenseTable;
use super::merchants::AgentTable;
use super::player::PlayerState;
use super::seeding::seed_money;
use super::ship::PlayerShip;
use super::{add_finance_and_state_tables, add_securities_tables, add_society_tables};
use crate::clock::SimClock;
use crate::error::SimError;
use crate::module::{Module, SimBuilder};
use crate::world::NodeTable;
use std::sync::Arc;

/// Registers the agents tables. Requires the world and economy modules; registers the production system.
pub struct AgentsModule;

impl Module for AgentsModule {
    fn id(&self) -> &'static str {
        "agents"
    }

    fn requires(&self) -> &[&'static str] {
        &["world", "economy"]
    }

    /// C# order: player, cargo, licenses, ship, finance and state tables, facilities, securities,
    /// merchants, groups (founded, then owners assigned), guild memberships, policy, guidance,
    /// genetics, the money seed, LOD state.
    fn register(&self, b: &mut SimBuilder) -> Result<(), SimError> {
        let db = Arc::clone(&b.services.database);
        let seed = b.world.get::<SimClock>().seed;
        let player = PlayerState::at_start(b.services.get::<NodeTable>(), &db.config, seed)?;
        b.world.add(player)?;
        b.world.add(CargoTable::default())?;
        b.world.add(LicenseTable::default())?;
        b.world.add(PlayerShip::default())?;
        add_finance_and_state_tables(b)?;

        let nodes = b.services.get::<NodeTable>();
        let mut facilities = FacilityTable::spawn(nodes, &db, seed)?;
        let facility_nodes = facilities.node.clone();
        add_securities_tables(b, &facility_nodes)?;

        let nodes = b.services.get::<NodeTable>();
        let mut merchants = AgentTable::spawn(nodes, &db.config, seed);
        let mut groups = GroupTable::found(nodes, &db, &facilities);
        groups.assign_owners(nodes, &db, &mut facilities, &mut merchants);
        let continents = nodes.continent_count();
        b.world.add(facilities)?;
        b.world.add(merchants)?;
        b.world.add(groups)?;
        b.world.add(GuildMembershipTable::default())?;
        add_society_tables(b)?;
        b.world.add(MerchantGenetics::new(continents))?;
        let nodes = b.services.get::<NodeTable>();
        seed_money(&mut b.world, nodes, &db.config);
        b.add_system(Box::new(super::ProductionSystem))?;
        b.add_system(Box::new(super::HouseholdSpendingSystem))?;
        b.add_system(Box::new(super::HomeSurplusSystem))
    }
}
