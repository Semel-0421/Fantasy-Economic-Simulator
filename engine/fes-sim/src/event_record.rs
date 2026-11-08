//! Every event the systems and commands publish, as one plain-data enum for front ends.
use crate::commands::player_command::PlayerCommand;
use crate::commands::{CommandApplied, CommandRejected};
use crate::events::SimEvent;

macro_rules! event_records {
    ($($variant:ident => $ty:path,)*) => {
        /// One delivered event as plain data. Each event struct is wrapped as is; the command
        /// events carry the command as a `PlayerCommand` (`None` for a command that has none).
        #[derive(Clone, Debug, PartialEq, Eq)]
        #[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
        pub enum SimEventRecord {
            $($variant($ty),)*
            CommandApplied {
                command: Option<PlayerCommand>,
                turn: i32,
            },
            CommandRejected {
                command: Option<PlayerCommand>,
                reason: String,
                turn: i32,
            },
            /// A scripted scenario event fired (or a scenario contract was delivered, `event` then
            /// being the contract id): its text from the scenario file, and the coins it paid the
            /// player (`continent` is their currency, -1 when none).
            ScenarioDialog {
                event: String,
                text: String,
                coins: i64,
                continent: i32,
                turn: i32,
            },
            /// A scenario objective was reached.
            ObjectiveDone { objective: String, turn: i32 },
            /// The scenario's last week ended; the results screen follows.
            ScenarioEnded {
                net_profit: i64,
                objectives_done: i32,
                turn: i32,
            },
            /// An event type this enum does not list (test events): its Rust type name.
            Other { type_name: String },
        }

        impl SimEventRecord {
            pub fn of(e: &dyn SimEvent) -> Self {
                let any = e.as_any();
                $(if let Some(x) = any.downcast_ref::<$ty>() {
                    return Self::$variant(*x);
                })*
                if let Some(x) = any.downcast_ref::<CommandApplied>() {
                    return Self::CommandApplied {
                        command: PlayerCommand::from_command(x.command.as_ref()),
                        turn: x.turn,
                    };
                }
                if let Some(x) = any.downcast_ref::<CommandRejected>() {
                    return Self::CommandRejected {
                        command: PlayerCommand::from_command(x.command.as_ref()),
                        reason: x.reason.clone(),
                        turn: x.turn,
                    };
                }
                Self::Other {
                    type_name: e.type_name().to_string(),
                }
            }

            /// The event's name (the variant name; the type name for `Other`).
            pub fn name(&self) -> &str {
                match self {
                    $(Self::$variant(_) => stringify!($variant),)*
                    Self::CommandApplied { .. } => "CommandApplied",
                    Self::CommandRejected { .. } => "CommandRejected",
                    Self::ScenarioDialog { .. } => "ScenarioDialog",
                    Self::ObjectiveDone { .. } => "ObjectiveDone",
                    Self::ScenarioEnded { .. } => "ScenarioEnded",
                    Self::Other { type_name } => type_name,
                }
            }
        }
    };
}

event_records! {
    AuctionClosed => crate::agents::bank_system::AuctionClosed,
    BondDefaulted => crate::agents::credit_system::BondDefaulted,
    BondTraded => crate::commands::bonds::BondTraded,
    CargoMoved => crate::commands::trade::CargoMoved,
    CoinsExchanged => crate::commands::bank::CoinsExchanged,
    CollateralSeized => crate::agents::bank_system::CollateralSeized,
    CurrencyMinted => crate::economy::currency_system::CurrencyMinted,
    EmancipationEnacted => crate::agents::migration_system::EmancipationEnacted,
    FacilityBankrupt => crate::agents::production::FacilityBankrupt,
    FacilityClosed => crate::agents::income_tax::FacilityClosed,
    FacilityExpanded => crate::agents::investment::FacilityExpanded,
    FacilityFounded => crate::agents::investment::FacilityFounded,
    FacilityRepurposed => crate::agents::investment::FacilityRepurposed,
    FinancialEventEnded => crate::agents::finance_system::FinancialEventEnded,
    FinancialEventStarted => crate::agents::finance_system::FinancialEventStarted,
    GoodsTraded => crate::commands::trade::GoodsTraded,
    GuildJoined => crate::commands::places::GuildJoined,
    GuildMembershipExpired => crate::agents::groups::GuildMembershipExpired,
    HouseholdsUpdated => crate::agents::household_growth::HouseholdsUpdated,
    IncomeTaxCollected => crate::agents::income_tax::IncomeTaxCollected,
    LicenseBought => crate::commands::licenses::LicenseBought,
    LicenseExpired => crate::agents::licenses::LicenseExpired,
    LoanRepaid => crate::agents::bank_system::LoanRepaid,
    LoanTaken => crate::commands::bank::LoanTaken,
    Lobbied => crate::commands::licenses::Lobbied,
    LostWarehouseCleared => crate::agents::production::LostWarehouseCleared,
    MarginCalled => crate::agents::bank_system::MarginCalled,
    MarketBurst => crate::agents::asset_markets::MarketBurst,
    MarketHeated => crate::agents::asset_markets::MarketHeated,
    MerchantBankrupt => crate::agents::merchants::MerchantBankrupt,
    MerchantVehicleBought => crate::agents::order_system::MerchantVehicleBought,
    MintingPolicyChanged => crate::agents::policy_system::MintingPolicyChanged,
    NobleContractBroken => crate::agents::nobles::NobleContractBroken,
    NpcLoanDefaulted => crate::agents::production::NpcLoanDefaulted,
    PeopleMigrated => crate::agents::migration_system::PeopleMigrated,
    PlayerArrived => crate::agents::travel::PlayerArrived,
    PolicyReviewed => crate::agents::policy_system::PolicyReviewed,
    RoadBuilt => crate::agents::roads::RoadBuilt,
    RoadLost => crate::agents::roads::RoadLost,
    ShortCalled => crate::agents::shorts::ShortCalled,
    ShortsCovered => crate::agents::shorts::ShortsCovered,
    StateProjectFinished => crate::agents::state_projects::StateProjectFinished,
    StatisticsPublished => crate::agents::finance_system::StatisticsPublished,
    SubsidyEnacted => crate::agents::subsidies::SubsidyEnacted,
    SupplyContractBroken => crate::agents::contract_system::SupplyContractBroken,
    SupplyContractSigned => crate::agents::contract_system::SupplyContractSigned,
    TripEncounter => crate::commands::travel::TripEncounter,
    TripStarted => crate::commands::travel::TripStarted,
    WorldEventStarted => crate::economy::world_events::WorldEventStarted,
}
