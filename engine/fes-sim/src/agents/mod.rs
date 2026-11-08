//! The agents module (C# `FES.Agents`): the player, merchants, facilities, groups, finance, society,
//! policy and securities tables. The systems arrive with the ports.
pub mod asset_markets;
pub mod asset_prices;
pub mod balance_sheet;
pub mod bank_npc;
pub mod bank_seizure;
pub mod bank_system;
pub mod banks;
pub mod bond_desk;
pub mod cargo;
pub mod columns;
pub mod contract_system;
pub mod contracts;
pub mod credit;
pub mod credit_system;
pub mod defense;
pub mod demand_forecast;
pub mod development;
pub mod facilities;
pub mod finance;
pub mod finance_system;
pub mod genetics;
pub mod groups;
pub mod guidance;
pub mod household_growth;
pub mod income_tax;
pub mod investment;
pub mod labor;
pub mod labor_market;
pub mod land;
pub mod ledger;
pub mod licenses;
pub mod loans;
pub mod lod;
pub mod lod_system;
pub mod merchant_membership;
pub mod merchant_vehicles;
pub mod merchants;
pub mod migration;
pub mod migration_system;
pub mod module;
pub mod money_ledger;
pub mod nobles;
pub mod order_system;
pub mod orders;
pub mod places;
pub mod player;
pub mod policy;
pub mod production;
pub mod purses;
pub mod purses_systems;
pub mod roads;
pub mod securities;
pub mod seeding;
pub mod ship;
pub mod shorts;
pub mod state_projects;
pub mod state_spending;
pub mod strategic_subsidy;
pub mod subsidies;
pub mod trader_loans;
pub mod travel;
pub mod travel_costs;
pub mod ventures;

use crate::economy::{CurrencyState, MarketStore};
use crate::error::SimError;
use crate::module::SimBuilder;
use crate::world::{NodeBanks, NodeTable, RouteTable};
use std::sync::Arc;

pub use banks::{AuctionTable, BankState, LoanTable, MarginState};
pub use cargo::CargoTable;
pub use contracts::ContractTable;
pub use credit::{BillRoom, BillTable, BondOffers, BondTable, CreditStats};
pub use development::DevelopmentState;
pub use facilities::FacilityTable;
pub use finance::{FinanceRecord, FinanceState, IncomeTaxState, MoneyTotals};
pub use genetics::MerchantGenetics;
pub use groups::{GroupTable, GuildMembershipTable};
pub use guidance::GuidanceState;
pub use household_growth::{HouseholdGrowthSystem, HouseholdsUpdated};
pub use labor::LaborState;
pub use land::LandState;
pub use licenses::LicenseTable;
pub use loans::{NpcLoanTable, TraderLoans};
pub use lod::LodState;
pub use lod_system::{DefaultLodPolicy, LodPolicy, LodPolicyService, LodSystem};
pub use merchants::{AgentTable, MerchantBankrupt, MerchantSystem, MerchantTraitTable, NodeMemory};
pub use migration::MigrationState;
pub use module::AgentsModule;
pub use nobles::NobleState;
pub use orders::{DemandForecast, MerchantTripStats, OrderTable};
pub use player::PlayerState;
pub use policy::PolicyState;
pub use production::ProductionSystem;
pub use purses::PurseState;
pub use purses_systems::{HomeSurplusSystem, HouseholdSpendingSystem};
pub use securities::{AssetMarkets, ShareTable, ShortLicences, ShortTable, VentureTable};
pub use ship::PlayerShip;
pub use travel::{PlayerArrived, TravelSystem};

/// C# `AddFinanceAndStateTables`, in C# order: banks, loans, margin, auctions, NPC loans, purses,
/// labor, income tax, development, nobles, orders, contracts, demand forecast, trip stats, trader
/// loans, bills, bill room, bonds, credit stats, bond offers, migration, finance, money totals.
pub fn add_finance_and_state_tables(b: &mut SimBuilder) -> Result<(), SimError> {
    let db = Arc::clone(&b.services.database);
    let nodes = b.services.get::<NodeTable>();
    let banks = b.services.get::<NodeBanks>();
    let nations = banks.nation_count(nodes) as usize;
    let continents = nodes.continent_count() as usize;
    let mut bank = BankState::new(nations, continents);
    bank.set_up_reserves(
        banks,
        nodes,
        b.world.get::<CurrencyState>(),
        db.config.bank_initial_reserve_share,
    );
    let purses = PurseState::new(nodes, banks);
    let floats = purses.count();
    let labor = LaborState::new(floats, db.config.labor_base_employment);
    let room = BillRoom::new(floats);
    let routes = b.services.get::<RouteTable>().count();
    let markets = b.world.get::<MarketStore>();
    let development = DevelopmentState::new(
        nodes.count(),
        routes,
        nations,
        floats,
        markets.good_base_price.len(),
    );
    let forecast = DemandForecast::new(markets.cell_count());
    let migration = MigrationState::new(nodes, nations, &db.config);
    let nobles = NobleState::new(nodes);
    b.world.add(bank)?;
    b.world.add(LoanTable::default())?;
    b.world.add(MarginState::default())?;
    b.world.add(AuctionTable::default())?;
    b.world.add(NpcLoanTable::default())?;
    b.world.add(purses)?;
    b.world.add(labor)?;
    b.world.add(IncomeTaxState::new(continents))?;
    b.world.add(development)?;
    b.world.add(nobles)?;
    b.world.add(OrderTable::default())?;
    b.world.add(ContractTable::default())?;
    b.world.add(forecast)?;
    b.services.add(MerchantTripStats::default())?;
    b.world.add(TraderLoans::new(
        b.services.get::<NodeTable>().count(),
        nations,
    ))?;
    b.world.add(BillTable::default())?;
    b.world.add(room)?;
    b.world.add(BondTable::default())?;
    b.services.add(CreditStats::default())?;
    b.services.add(BondOffers::default())?;
    b.world.add(migration)?;
    b.world.add(FinanceState::new(
        nations,
        continents,
        db.config.loan_interest_per_year,
    ))?;
    b.services.add(MoneyTotals::new(nations, continents))
}

/// The asset-market tables C# `AgentsModule.Register` adds after the facilities: asset markets,
/// land, shares, shorts, short licences and the founded ventures. `facility_nodes` is the node of
/// every facility, which land's used plots count.
pub fn add_securities_tables(b: &mut SimBuilder, facility_nodes: &[i32]) -> Result<(), SimError> {
    let db = Arc::clone(&b.services.database);
    let nodes = b.services.get::<NodeTable>();
    let banks = b.services.get::<NodeBanks>();
    let routes = b.services.get::<RouteTable>();
    let nations = banks.nation_count(nodes) as usize;
    let markets = AssetMarkets::new(nodes, nations);
    let land = LandState::new(nodes, &db.config, facility_nodes);
    let licences = ShortLicences::new(markets.count(), nations);
    let mut ventures = VentureTable::default();
    ventures.found(nodes, routes, banks, &db.config);
    b.world.add(markets)?;
    b.world.add(land)?;
    b.world.add(ShareTable::default())?;
    b.world.add(ShortTable::default())?;
    b.world.add(licences)?;
    b.world.add(ventures)
}

/// The policy and guidance tables and the unsaved LOD state, which C# adds after the merchants,
/// groups and guild memberships (the LOD state after the ledger seed; it is unsaved, so the
/// position does not change any section).
pub fn add_society_tables(b: &mut SimBuilder) -> Result<(), SimError> {
    let db = Arc::clone(&b.services.database);
    let nodes = b.services.get::<NodeTable>();
    let nations = b.services.get::<NodeBanks>().nation_count(nodes) as usize;
    let policy = PolicyState::new(nations, nodes.continent_count() as usize, &db);
    let lod = LodState::new(nodes.count());
    b.world.add(policy)?;
    b.world.add(GuidanceState::default())?;
    b.services.add(lod)?;
    b.services
        .add(LodPolicyService(Box::new(DefaultLodPolicy)))?;
    b.add_system(Box::new(LodSystem))
}
