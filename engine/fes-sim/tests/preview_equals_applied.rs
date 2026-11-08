//! Spec 5.2a: a command's preview, made after other commands were queued the same week, equals the
//! player's position once the next Commands phase applied them all (coins, cargo, stock).
#![cfg(feature = "scenario")]
mod common;

use common::data_dir;
use fes_data::FileDataSource;
use fes_sim::agents::facilities::FacilityTable;
use fes_sim::pipeline::{SimPhase, System};
use fes_sim::preview::{position, CommandPreview};
use fes_sim::scenario::ScenarioDef;
use fes_sim::world::NodeTable;
use fes_sim::{
    Module, PlayerCommand as P, SimBuilder, SimContext, SimError, SimEventRecord as E, Simulation,
    SimulationBuilder,
};

const VILLAGE: &str = "aur_n1_r1_c1_v1";
const CITY: &str = "aur_n1_r1_c1";

/// The player's position right after the Commands phase and its event dispatch.
#[derive(Default)]
struct Probe(Option<CommandPreview>);

struct ProbeSystem;

impl System for ProbeSystem {
    fn id(&self) -> &'static str {
        "test.probe"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Lod
    }

    fn order(&self) -> i32 {
        i32::MIN
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let seen = position(ctx, None);
        ctx.services.get_mut::<Probe>().0 = Some(seen);
        Ok(())
    }
}

struct ProbeModule;

impl Module for ProbeModule {
    fn id(&self) -> &'static str {
        "test.probe"
    }

    fn requires(&self) -> &[&'static str] {
        &[]
    }

    fn register(&self, b: &mut SimBuilder) -> Result<(), SimError> {
        b.services.add(Probe::default())?;
        b.add_system(Box::new(ProbeSystem))
    }
}

fn build(start: &str) -> Simulation {
    let text = format!(
        r#"(id: "p", title: "P", seed: 5, start_node: "{start}", start_coins: 40000, weeks: 30,
        objectives: [], events: [])"#
    );
    let def = ScenarioDef::parse(&text).expect("scenario");
    let source = FileDataSource::new(data_dir());
    let mut sim = SimulationBuilder::from_data(&source, 1)
        .and_then(|b| b.add_module(Box::new(ProbeModule)))
        .expect("builder")
        .scenario(def)
        .build()
        .expect("build");
    sim.step().expect("first week");
    sim
}

fn node(sim: &Simulation, key: &str) -> i32 {
    let nodes = sim.context().services.get::<NodeTable>();
    nodes.try_find(key).expect(key).0
}

/// Queues all but the last command, previews the last, then applies the week and compares.
/// Returns whether the last command applied.
fn check(start: &str, family: &str, commands: &[P]) -> bool {
    let mut sim = build(start);
    let (last, before) = commands.split_last().expect("a command");
    for c in before {
        sim.queue(c.clone().into_command());
    }
    let hash = sim.world_hash();
    let preview = sim.preview_queued(last.clone().into_command().as_ref());
    assert_eq!(
        sim.world_hash(),
        hash,
        "{family}: the preview changed the world"
    );
    sim.queue(last.clone().into_command());
    let report = sim.step().expect("step");
    let applied = sim
        .context()
        .services
        .get::<Probe>()
        .0
        .clone()
        .expect("probe");
    let rejected = report.events.iter().find_map(|e| match e {
        E::CommandRejected {
            command: Some(c),
            reason,
            ..
        } if c == last => Some(reason.clone()),
        _ => None,
    });
    assert_eq!(preview.error, rejected, "{family}: answer");
    assert_eq!(preview.purse, applied.purse, "{family}: purse");
    assert_eq!(preview.cargo, applied.cargo, "{family}: cargo");
    assert_eq!(preview.stock, applied.stock, "{family}: stock");
    preview.error.is_none()
}

fn buyable(start: &str) -> i32 {
    let mut sim = build(start);
    let goods = sim.context().services.database.goods.len() as i32;
    (0..goods)
        .find(|&good| {
            let buy = P::BuyGoods {
                good,
                quantity: 3,
                pay_from_deposit: false,
            };
            sim.preview_queued(buy.into_command().as_ref())
                .error
                .is_none()
        })
        .expect("a good to buy")
}

#[test]
fn trade_orders_on_the_same_stock() {
    let good = buyable(VILLAGE);
    let buy = |quantity| P::BuyGoods {
        good,
        quantity,
        pay_from_deposit: false,
    };
    let sell = P::SellGoods { good, quantity: 4 };
    assert!(check(VILLAGE, "buy", &[buy(3)]));
    assert!(check(VILLAGE, "buy twice", &[buy(3), buy(5)]));
    assert!(check(VILLAGE, "buy, buy, sell", &[buy(3), buy(5), sell]));
    // Selling more than the cart will hold once the queue ran: the preview sees the queue.
    check(
        VILLAGE,
        "sell too much",
        &[buy(2), P::SellGoods { good, quantity: 9 }],
    );
}

#[test]
fn travel_after_buying() {
    let good = buyable(VILLAGE);
    let sim = build(VILLAGE);
    let destination = node(&sim, CITY);
    let buy = P::BuyGoods {
        good,
        quantity: 2,
        pay_from_deposit: false,
    };
    let travel = |guards, insurance| P::Travel {
        destination,
        guards,
        insurance,
    };
    assert!(check(VILLAGE, "travel", &[buy.clone(), travel(true, true)]));
    check(VILLAGE, "trade on the road", &[travel(false, false), buy]);
}

#[test]
fn bank_orders() {
    let deposit = P::BankTransfer { amount: 1_000 };
    let withdraw = P::BankTransfer { amount: -400 };
    assert!(check(CITY, "deposit then withdraw", &[deposit, withdraw]));
    check(
        CITY,
        "loan and repay",
        &[P::TakeLoan { amount: 2_000 }, P::RepayLoan { amount: 500 }],
    );
    check(
        CITY,
        "exchange",
        &[P::Exchange {
            from_continent: 0,
            to_continent: 1,
            amount: 1_000,
        }],
    );
}

#[test]
fn bond_and_share_orders() {
    check(
        CITY,
        "bonds",
        &[
            P::BuyBond {
                issuer: -1,
                amount: 500,
            },
            P::SellBond {
                issuer: -1,
                amount: 200,
            },
        ],
    );
    let sim = build(CITY);
    let city = node(&sim, CITY);
    let facilities = sim.world().get::<FacilityTable>();
    let near = (0..facilities.count() as i32)
        .find(|&f| facilities.node[f as usize] == city)
        .unwrap_or(0);
    check(
        CITY,
        "shares",
        &[
            P::BuyShare {
                facility: near,
                count: 5,
            },
            P::BuyShare {
                facility: near,
                count: 3,
            },
            P::SellShare {
                facility: near,
                count: 2,
            },
        ],
    );
    check(
        CITY,
        "venture",
        &[P::BuyVentureShare {
            venture: 0,
            count: 2,
        }],
    );
}

#[test]
fn licences_places_and_companies() {
    let good = buyable(VILLAGE);
    check(
        CITY,
        "licence",
        &[P::BuyLicense { good }, P::BuyTradeLicense { kind: 1 }],
    );
    check(CITY, "ship", &[P::BuyShip, P::SellShip]);
    check(CITY, "plot", &[P::BuyPlot, P::SellPlot]);
    check(CITY, "guild", &[P::JoinGuild { guild: 0 }]);
    let sim = build(CITY);
    let city = node(&sim, CITY);
    check(
        CITY,
        "company",
        &[P::FoundCompany {
            headquarters: city,
            focus: 0,
            cost: 1_000,
        }],
    );
}

#[test]
fn a_preview_leaves_the_run_unchanged() {
    let good = buyable(VILLAGE);
    let buy = P::BuyGoods {
        good,
        quantity: 3,
        pay_from_deposit: false,
    };
    let run = |preview: bool| {
        let mut sim = build(VILLAGE);
        sim.queue(buy.clone().into_command());
        if preview {
            sim.preview_queued(buy.clone().into_command().as_ref());
        }
        sim.queue(buy.clone().into_command());
        for _ in 0..3 {
            sim.step().expect("step");
        }
        sim.world_hash()
    };
    assert_eq!(run(true), run(false));
}
