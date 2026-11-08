//! Every player command as plain data (`Clone + Send`), for worker messages: a front end builds a
//! `PlayerCommand` and the simulation thread turns it into the command object.
use super::bank::{BankTransfer, Bid, Exchange, RepayLoan, TakeLoan};
use super::bonds::{BuyBond, SellBond};
use super::facility::{
    BuyFacility, ExpandFacility, FoundCompany, MoveFacilityCash, RepurposeFacility,
    SetFacilityControl,
};
use super::licenses::{BuyLicense, BuyShip, BuyTradeLicense, Lobby, SellShip};
use super::places::{BuildRoad, BuyPlot, JoinGuild, MoveGuildHallCargo, SellPlot};
use super::securities::{
    BuyShare, BuyShortLicence, BuyVentureShare, CoverShort, SellShare, SellShort, SellVentureShare,
};
use super::trade::{BuyGoods, MoveCargo, SellGoods};
use super::travel::Travel;
use super::Command;

/// One C# `ICommand` per variant, with the same fields. Ids are dense indices; enum-typed C#
/// fields (`GuidanceAxis`, `ControlMode`, `PolicyKind`, `AssetMarketKind`, `TradeLicenseKind`) are
/// their raw values so an unknown one reaches validation, as in C#.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum PlayerCommand {
    BankTransfer {
        amount: i64,
    },
    Exchange {
        from_continent: i32,
        to_continent: i32,
        amount: i64,
    },
    TakeLoan {
        amount: i64,
    },
    RepayLoan {
        amount: i64,
    },
    Bid {
        facility: i32,
        amount: i64,
    },
    BuyBond {
        issuer: i32,
        amount: i64,
    },
    SellBond {
        issuer: i32,
        amount: i64,
    },
    MoveFacilityCash {
        facility: i32,
        amount: i64,
    },
    SetFacilityControl {
        facility: i32,
        mode: u8,
    },
    BuyFacility {
        facility: i32,
        price: i64,
        axis: u8,
    },
    FoundCompany {
        headquarters: i32,
        focus: u8,
        cost: i64,
    },
    ExpandFacility {
        facility: i32,
    },
    RepurposeFacility {
        facility: i32,
        def_key: String,
    },
    JoinGuild {
        guild: i32,
    },
    MoveGuildHallCargo {
        guild: i32,
        good: i32,
        quantity: i32,
        to_hall: bool,
    },
    BuyPlot,
    SellPlot,
    BuyLicense {
        good: i32,
    },
    BuyTradeLicense {
        kind: i32,
    },
    BuyShip,
    SellShip,
    Lobby {
        nation: i32,
        policy: u8,
        direction: i32,
        amount: i64,
        base_good: i32,
    },
    BuildRoad {
        destination: i32,
    },
    BuyShare {
        facility: i32,
        count: i32,
    },
    SellShare {
        facility: i32,
        count: i32,
    },
    BuyShortLicence {
        kind: u8,
    },
    SellShort {
        venture: bool,
        asset: i32,
        count: i32,
    },
    CoverShort {
        venture: bool,
        asset: i32,
        count: i32,
    },
    BuyGoods {
        good: i32,
        quantity: i32,
        pay_from_deposit: bool,
    },
    SellGoods {
        good: i32,
        quantity: i32,
    },
    MoveCargo {
        facility: i32,
        good: i32,
        quantity: i32,
        to_warehouse: bool,
    },
    Travel {
        destination: i32,
        guards: bool,
        insurance: bool,
    },
    BuyVentureShare {
        venture: i32,
        count: i32,
    },
    SellVentureShare {
        venture: i32,
        count: i32,
    },
}

impl PlayerCommand {
    /// The command object the simulation queues.
    pub fn into_command(self) -> Box<dyn Command> {
        use PlayerCommand as P;
        match self {
            P::BankTransfer { amount } => Box::new(BankTransfer { amount }),
            P::Exchange {
                from_continent,
                to_continent,
                amount,
            } => Box::new(Exchange {
                from_continent,
                to_continent,
                amount,
            }),
            P::TakeLoan { amount } => Box::new(TakeLoan { amount }),
            P::RepayLoan { amount } => Box::new(RepayLoan { amount }),
            P::Bid { facility, amount } => Box::new(Bid { facility, amount }),
            P::BuyBond { issuer, amount } => Box::new(BuyBond { issuer, amount }),
            P::SellBond { issuer, amount } => Box::new(SellBond { issuer, amount }),
            P::MoveFacilityCash { facility, amount } => {
                Box::new(MoveFacilityCash { facility, amount })
            }
            P::SetFacilityControl { facility, mode } => {
                Box::new(SetFacilityControl { facility, mode })
            }
            P::BuyFacility {
                facility,
                price,
                axis,
            } => Box::new(BuyFacility {
                facility,
                price,
                axis,
            }),
            P::FoundCompany {
                headquarters,
                focus,
                cost,
            } => Box::new(FoundCompany {
                headquarters,
                focus,
                cost,
            }),
            P::ExpandFacility { facility } => Box::new(ExpandFacility { facility }),
            P::RepurposeFacility { facility, def_key } => {
                Box::new(RepurposeFacility { facility, def_key })
            }
            P::JoinGuild { guild } => Box::new(JoinGuild { guild }),
            P::MoveGuildHallCargo {
                guild,
                good,
                quantity,
                to_hall,
            } => Box::new(MoveGuildHallCargo {
                guild,
                good,
                quantity,
                to_hall,
            }),
            P::BuyPlot => Box::new(BuyPlot),
            P::SellPlot => Box::new(SellPlot),
            P::BuyLicense { good } => Box::new(BuyLicense { good }),
            P::BuyTradeLicense { kind } => Box::new(BuyTradeLicense { kind }),
            P::BuyShip => Box::new(BuyShip),
            P::SellShip => Box::new(SellShip),
            P::Lobby {
                nation,
                policy,
                direction,
                amount,
                base_good,
            } => Box::new(Lobby {
                nation,
                policy,
                direction,
                amount,
                base_good,
            }),
            other => other.into_market_command(),
        }
    }

    fn into_market_command(self) -> Box<dyn Command> {
        use PlayerCommand as P;
        match self {
            P::BuildRoad { destination } => Box::new(BuildRoad { destination }),
            P::BuyShare { facility, count } => Box::new(BuyShare { facility, count }),
            P::SellShare { facility, count } => Box::new(SellShare { facility, count }),
            P::BuyShortLicence { kind } => Box::new(BuyShortLicence { kind }),
            P::SellShort {
                venture,
                asset,
                count,
            } => Box::new(SellShort {
                venture,
                asset,
                count,
            }),
            P::CoverShort {
                venture,
                asset,
                count,
            } => Box::new(CoverShort {
                venture,
                asset,
                count,
            }),
            P::BuyGoods {
                good,
                quantity,
                pay_from_deposit,
            } => Box::new(BuyGoods {
                good,
                quantity,
                pay_from_deposit,
            }),
            P::SellGoods { good, quantity } => Box::new(SellGoods { good, quantity }),
            P::MoveCargo {
                facility,
                good,
                quantity,
                to_warehouse,
            } => Box::new(MoveCargo {
                facility,
                good,
                quantity,
                to_warehouse,
            }),
            P::Travel {
                destination,
                guards,
                insurance,
            } => Box::new(Travel {
                destination,
                guards,
                insurance,
            }),
            P::BuyVentureShare { venture, count } => Box::new(BuyVentureShare { venture, count }),
            P::SellVentureShare { venture, count } => Box::new(SellVentureShare { venture, count }),
            _ => unreachable!("handled by into_command"),
        }
    }
}
