//! The agents module (C# `FES.Agents`): finance and securities tables so far. The player, merchants,
//! facilities and the other state tables are added by later tasks; the systems arrive with the ports.
pub mod banks;
pub mod columns;
pub mod credit;
pub mod finance;
pub mod labor;
pub mod land;
pub mod ledger;
pub mod loans;
pub mod purses;
pub mod securities;

use crate::economy::CurrencyState;
use crate::error::SimError;
use crate::module::{Module, SimBuilder};
use crate::world::{NodeBanks, NodeTable, RouteTable};
use std::sync::Arc;

pub use banks::{AuctionTable, BankState, LoanTable, MarginState};
pub use credit::{BillRoom, BillTable, BondOffers, BondTable, CreditStats};
pub use finance::{FinanceRecord, FinanceState, IncomeTaxState, MoneyTotals};
pub use labor::LaborState;
pub use land::LandState;
pub use loans::{NpcLoanTable, TraderLoans};
pub use purses::PurseState;
pub use securities::{AssetMarkets, ShareTable, ShortLicences, ShortTable, VentureTable};

/// The finance and state tables of C# `AgentsModule.AddFinanceAndStateTables` that this port has so
/// far, in C# order. The tables the C# method also adds (development, nobles, orders, contracts,
/// demand forecast, merchant trip stats, migration) come with the facility and merchant tasks.
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
    let labor = LaborState::new(purses.count(), db.config.labor_base_employment);
    let room = BillRoom::new(purses.count());
    b.world.add(bank)?;
    b.world.add(LoanTable::default())?;
    b.world.add(MarginState::default())?;
    b.world.add(AuctionTable::default())?;
    b.world.add(NpcLoanTable::default())?;
    b.world.add(purses)?;
    b.world.add(labor)?;
    b.world.add(IncomeTaxState::new(continents))?;
    b.world.add(TraderLoans::new(
        b.services.get::<NodeTable>().count(),
        nations,
    ))?;
    b.world.add(BillTable::default())?;
    b.world.add(room)?;
    b.world.add(BondTable::default())?;
    b.services.add(CreditStats::default())?;
    b.services.add(BondOffers::default())?;
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

/// Registers the agents tables. Requires the world and economy modules; registers no systems yet.
pub struct AgentsModule;

impl Module for AgentsModule {
    fn id(&self) -> &'static str {
        "agents"
    }

    fn requires(&self) -> &[&'static str] {
        &["world", "economy"]
    }

    fn register(&self, b: &mut SimBuilder) -> Result<(), SimError> {
        add_finance_and_state_tables(b)?;
        // The facility task passes the spawned facilities' nodes here.
        add_securities_tables(b, &[])
    }
}
