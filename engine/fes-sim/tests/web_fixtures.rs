//! Writes one JSON sample of every `PlayerCommand` and `SimEventRecord` variant, and of every view
//! of a running world, to `web/tests/fixtures/`. The web app's Vitest suite parses them, so the
//! hand-written TypeScript types in `web/src/sim/types.ts` cannot drift from the serde shapes.
#![cfg(feature = "scenario")]
mod common;

use common::build_sample;
use fes_core::Fx;
use fes_sim::agents::asset_markets::{AssetMarketKind, MarketBurst, MarketHeated};
use fes_sim::agents::bank_system::*;
use fes_sim::agents::contract_system::*;
use fes_sim::agents::credit_system::*;
use fes_sim::agents::finance_system::*;
use fes_sim::agents::groups::GuildMembershipExpired;
use fes_sim::agents::household_growth::HouseholdsUpdated;
use fes_sim::agents::income_tax::*;
use fes_sim::agents::investment::*;
use fes_sim::agents::licenses::LicenseExpired;
use fes_sim::agents::merchants::MerchantBankrupt;
use fes_sim::agents::migration_system::*;
use fes_sim::agents::nobles::NobleContractBroken;
use fes_sim::agents::order_system::MerchantVehicleBought;
use fes_sim::agents::policy_system::*;
use fes_sim::agents::production::{FacilityBankrupt, LostWarehouseCleared, NpcLoanDefaulted};
use fes_sim::agents::roads::*;
use fes_sim::agents::shorts::*;
use fes_sim::agents::state_projects::StateProjectFinished;
use fes_sim::agents::subsidies::SubsidyEnacted;
use fes_sim::agents::travel::PlayerArrived;
use fes_sim::commands::bank::*;
use fes_sim::commands::bonds::BondTraded;
use fes_sim::commands::licenses::*;
use fes_sim::commands::places::GuildJoined;
use fes_sim::commands::trade::*;
use fes_sim::commands::travel::*;
use fes_sim::economy::currency_system::CurrencyMinted;
use fes_sim::economy::pricing::market_view;
use fes_sim::economy::world_events::WorldEventStarted;
use fes_sim::{views, PlayerCommand as P, SimEventRecord as E};
use serde::Serialize;
use std::path::PathBuf;

fn write<T: Serialize>(name: &str, value: &T) {
    let dir = PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../web/tests/fixtures"
    ));
    std::fs::create_dir_all(&dir).expect("fixtures dir");
    let json = serde_json::to_string_pretty(value).expect("serialize");
    std::fs::write(dir.join(name), json + "\n").expect("write fixture");
}

fn commands_a() -> Vec<P> {
    vec![
        P::BankTransfer { amount: 100 },
        P::Exchange {
            from_continent: 0,
            to_continent: 1,
            amount: 500,
        },
        P::TakeLoan { amount: 1000 },
        P::RepayLoan { amount: 400 },
        P::Bid {
            facility: 2,
            amount: 900,
        },
        P::BuyBond {
            issuer: 1,
            amount: 300,
        },
        P::SellBond {
            issuer: 1,
            amount: 300,
        },
        P::MoveFacilityCash {
            facility: 3,
            amount: -50,
        },
        P::SetFacilityControl {
            facility: 3,
            mode: 1,
        },
        P::BuyFacility {
            facility: 4,
            price: 1200,
            axis: 2,
        },
        P::FoundCompany {
            headquarters: 5,
            focus: 1,
            cost: 2000,
        },
        P::ExpandFacility { facility: 3 },
        P::RepurposeFacility {
            facility: 3,
            def_key: "mill".to_string(),
        },
        P::JoinGuild { guild: 1 },
        P::MoveGuildHallCargo {
            guild: 1,
            good: 2,
            quantity: 5,
            to_hall: true,
        },
        P::BuyPlot,
        P::SellPlot,
    ]
}

fn commands_b() -> Vec<P> {
    vec![
        P::BuyLicense { good: 3 },
        P::BuyTradeLicense { kind: 1 },
        P::BuyShip,
        P::SellShip,
        P::Lobby {
            nation: 0,
            policy: 1,
            direction: -1,
            amount: 250,
            base_good: 2,
        },
        P::BuildRoad { destination: 4 },
        P::BuyShare {
            facility: 3,
            count: 10,
        },
        P::SellShare {
            facility: 3,
            count: 10,
        },
        P::BuyShortLicence { kind: 1 },
        P::SellShort {
            venture: false,
            asset: 3,
            count: 2,
        },
        P::CoverShort {
            venture: true,
            asset: 1,
            count: 2,
        },
        P::BuyGoods {
            good: 2,
            quantity: 7,
            pay_from_deposit: true,
        },
        P::SellGoods {
            good: 2,
            quantity: 7,
        },
        P::MoveCargo {
            facility: 3,
            good: 2,
            quantity: 4,
            to_warehouse: false,
        },
        P::Travel {
            destination: 6,
            guards: true,
            insurance: false,
        },
        P::BuyVentureShare {
            venture: 1,
            count: 3,
        },
        P::SellVentureShare {
            venture: 1,
            count: 3,
        },
    ]
}

fn quote() -> TripQuote {
    TripQuote {
        route: 1,
        fee: 10,
        fee_continent: 0,
        toll: 5,
        toll_continent: 1,
        cargo_units: 4,
        passage: 3,
        guards: 2,
        insurance: 1,
        cargo_value: 90,
        encounter_chance: 6554,
    }
}

fn events_a() -> Vec<E> {
    vec![
        E::AuctionClosed(AuctionClosed {
            facility: 1,
            won_by_player: true,
            price: 900,
            turn: 4,
        }),
        E::BondDefaulted(BondDefaulted { group: 2, turn: 5 }),
        E::BondTraded(BondTraded {
            issuer: 1,
            face: 300,
        }),
        E::CargoMoved(CargoMoved {
            facility: 3,
            good: 2,
            to_warehouse: true,
            requested: 5,
            moved: 4,
        }),
        E::CoinsExchanged(CoinsExchanged {
            from_continent: 0,
            to_continent: 1,
            paid: 500,
            received: 480,
            fee: 20,
        }),
        E::CollateralSeized(CollateralSeized {
            nation_node: 2,
            kind: CollateralKind::Land,
            facility: 3,
            value: 700,
            turn: 6,
        }),
        E::CurrencyMinted(CurrencyMinted {
            continent: 1,
            year: 2,
        }),
        E::EmancipationEnacted(EmancipationEnacted { nation: 1, year: 3 }),
        E::FacilityBankrupt(FacilityBankrupt {
            facility: 3,
            was_player_owned: true,
            warehouse_claim_until: -1,
            turn: 7,
        }),
        E::FacilityClosed(FacilityClosed {
            facility: 3,
            turn: 7,
        }),
        E::FacilityExpanded(FacilityExpanded {
            facility: 3,
            level: 2,
        }),
        E::FacilityFounded(FacilityFounded {
            facility: 8,
            group: 1,
        }),
        E::FacilityRepurposed(FacilityRepurposed {
            facility: 3,
            def_index: 4,
        }),
        E::FinancialEventEnded(FinancialEventEnded {
            nation: 1,
            kind: 2,
            turn: 9,
        }),
        E::FinancialEventStarted(FinancialEventStarted {
            nation: 1,
            kind: 2,
            turn: 8,
        }),
    ]
}

fn events_b() -> Vec<E> {
    vec![
        E::GoodsTraded(GoodsTraded {
            market: 1,
            good: 2,
            bought: true,
            requested: 7,
            filled: 7,
            price: 33,
            turn: 3,
        }),
        E::GuildJoined(GuildJoined {
            guild: 1,
            expires_turn: 60,
            fee: 200,
        }),
        E::GuildMembershipExpired(GuildMembershipExpired { guild: 1, turn: 60 }),
        E::HouseholdsUpdated(HouseholdsUpdated { year: 1 }),
        E::IncomeTaxCollected(IncomeTaxCollected {
            year: 1,
            households: 1000,
            companies: 500,
        }),
        E::LicenseBought(LicenseBought {
            base_good: 3,
            nation: 0,
            expires_turn: 80,
            price: 400,
        }),
        E::LicenseExpired(LicenseExpired {
            base_good: 3,
            nation: 0,
            turn: 80,
        }),
        E::LoanRepaid(LoanRepaid {
            nation_node: 2,
            turn: 12,
        }),
        E::LoanTaken(LoanTaken {
            nation_node: 2,
            amount: 1000,
            balance: 1100,
            weekly_payment: 50,
        }),
        E::Lobbied(Lobbied {
            nation: 0,
            policy: 1,
            base_good: 2,
            direction: -1,
            amount: 250,
        }),
    ]
}

fn events_e() -> Vec<E> {
    vec![
        E::LostWarehouseCleared(LostWarehouseCleared {
            facility: 3,
            turn: 20,
        }),
        E::MarginCalled(MarginCalled {
            nation_node: 2,
            shortfall: 90,
            sale_turn: 14,
        }),
        E::MarketBurst(MarketBurst {
            kind: AssetMarketKind::Property,
            node: 1,
            premium: Fx::from_raw(65536),
            turn: 15,
        }),
        E::MarketHeated(MarketHeated {
            kind: AssetMarketKind::Venture,
            node: 1,
            premium: Fx::from_raw(32768),
            turn: 15,
        }),
        E::MerchantBankrupt(MerchantBankrupt {
            merchant: 4,
            turn: 16,
        }),
        E::MerchantVehicleBought(MerchantVehicleBought {
            agent: 4,
            vehicle: 1,
        }),
        E::MintingPolicyChanged(MintingPolicyChanged {
            continent: 1,
            step: -1,
        }),
        E::NobleContractBroken(NobleContractBroken {
            vassal: 1,
            lord: 2,
            turn: 17,
        }),
        E::NpcLoanDefaulted(NpcLoanDefaulted {
            facility: 3,
            merchant: -1,
            nation_node: 2,
            turn: 18,
        }),
        E::PeopleMigrated(PeopleMigrated {
            from: 1,
            to: 2,
            people: 120,
        }),
    ]
}

fn events_c() -> Vec<E> {
    vec![
        E::PlayerArrived(PlayerArrived { node: 4, turn: 9 }),
        E::PolicyReviewed(PolicyReviewed {
            nation: 1,
            year: 2,
            toll_step: 1,
            license_price_step: -1,
            license_added: 2,
            license_removed: -1,
            influence: 40,
        }),
        E::RoadBuilt(RoadBuilt {
            from: 1,
            to: 2,
            turn: 19,
        }),
        E::RoadLost(RoadLost {
            from: 1,
            to: 2,
            turn: 21,
        }),
        E::ShortCalled(ShortCalled {
            nation: 1,
            shortfall: 70,
            cover_turn: 22,
        }),
        E::ShortsCovered(ShortsCovered {
            nation: 1,
            cost: 80,
            lent: 10,
            turn: 23,
        }),
        E::StateProjectFinished(StateProjectFinished {
            kind: 1,
            node: 2,
            turn: 24,
        }),
        E::StatisticsPublished(StatisticsPublished { year: 2 }),
        E::SubsidyEnacted(SubsidyEnacted {
            nation: 1,
            good: 2,
            turn: 25,
        }),
        E::SupplyContractBroken(SupplyContractBroken {
            seller: 1,
            buyer: 2,
        }),
        E::SupplyContractSigned(SupplyContractSigned {
            seller: 1,
            buyer: 2,
            quantity: 5,
            unit_price: 44,
        }),
        E::TripEncounter(TripEncounter {
            destination: 3,
            loss: 1,
            loss_value: 60,
            refund: 10,
            turn: 26,
        }),
        E::TripStarted(TripStarted {
            destination: 3,
            arrival_turn: 28,
            paid: quote(),
            turn: 26,
        }),
        E::WorldEventStarted(WorldEventStarted {
            definition: 1,
            node: 2,
            turn: 27,
        }),
    ]
}

fn events_d() -> Vec<E> {
    let cmd = Some(P::BuyShip);
    vec![
        E::CommandApplied {
            command: cmd.clone(),
            turn: 3,
        },
        E::CommandApplied {
            command: None,
            turn: 3,
        },
        E::CommandRejected {
            command: cmd,
            reason: "no money".to_string(),
            turn: 3,
        },
        E::Other {
            type_name: "TestEvent".to_string(),
        },
        E::ScenarioDialog {
            event: "offer".to_string(),
            text: "Bring grain".to_string(),
            coins: 900,
            continent: 0,
            turn: 4,
        },
        E::ObjectiveDone {
            objective: "deliver".to_string(),
            turn: 5,
        },
        E::ScenarioEnded {
            net_profit: -120,
            objectives_done: 1,
            turn: 20,
        },
    ]
}

#[test]
fn write_web_fixtures() {
    let commands = commands();
    assert_eq!(commands.len(), 34, "one sample per PlayerCommand variant");
    write("commands.json", &commands);
    let events = events();
    // 49 listed event types + 4 samples of the three generic variants + 3 scenario events.
    assert_eq!(events.len(), 49 + 4 + 3);
    write("events.json", &events);

    let mut sim = build_sample(&[]);
    let report = sim.step().expect("step");
    write(
        "turn_report.json",
        &serde_json::json!({ "turn": report.turn, "events": report.events }),
    );
    let ctx = sim.context();
    write("purse.json", &views::purse(ctx));
    write("cargo.json", &views::cargo(ctx));
    write("contracts.json", &views::contracts(ctx));
    write("shares.json", &views::shares(ctx));
    write("licences.json", &views::licences(ctx));
    write("statement.json", &views::statement(ctx));
    write("map.json", &views::map(ctx));
    write("goods.json", &views::goods(ctx));
    let m = market_view(&sim, fes_core::NodeId(views::purse(sim.context()).node));
    let quotes: Vec<_> = m
        .quotes
        .iter()
        .map(|q| {
            let offer = views::sell_offer(sim.context(), (q.price.raw() >> 16) as i32);
            serde_json::json!({"good": q.good.0, "price": q.price, "stock": q.stock, "offer": offer})
        })
        .collect();
    write("market.json", &serde_json::json!({ "quotes": quotes }));
    write_scenario_views();
}

/// The scenario views (results statement, objectives, scenario contracts) of a running scenario.
fn write_scenario_views() {
    let text = r#"(id: "t", title: "T", seed: 3, start_node: "aur_n1_r1_c1_v1", start_coins: 500,
        weeks: 4, objectives: [(id: "o", text: "Profit", goal: NetProfit(10))],
        events: [(id: "e", trigger: Week(1), effects: [OfferContract(id: "c", good: "wheat",
        quantity: 2, node: "aur_n1_r1_c1", reward: 90, deadline_week: 3, text: "Bring")])])"#;
    let def = fes_sim::scenario::ScenarioDef::parse(text).expect("scenario");
    let source = fes_data::FileDataSource::new(common::data_dir());
    let mut sim = fes_sim::SimulationBuilder::from_data(&source, 1)
        .expect("builder")
        .scenario(def)
        .build()
        .expect("build");
    sim.step().expect("step");
    write("results.json", &sim.statement().expect("statement"));
    write("objectives.json", &sim.objectives());
    write("scenario_contracts.json", &sim.scenario_contracts());
    let buy = P::BuyGoods {
        good: 1,
        quantity: 2,
        pay_from_deposit: false,
    };
    sim.queue(buy.clone().into_command());
    let preview = sim.preview_queued(buy.into_command().as_ref());
    write("command_preview.json", &preview);
}

fn commands() -> Vec<P> {
    [commands_a(), commands_b()].concat()
}

fn events() -> Vec<E> {
    [events_a(), events_b(), events_e(), events_c(), events_d()].concat()
}
