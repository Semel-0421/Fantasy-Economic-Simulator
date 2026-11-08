//! `preview_queued` on the full-scale world (Plan 6 Task 9): with several orders queued, repeated
//! previews agree and leave the world hash unchanged.
#![cfg(feature = "scenario")]
#[path = "preview_fullscale/allocations.rs"]
mod allocations;
mod scenario_play;
#[global_allocator]
static ALLOCATOR: allocations::Counting = allocations::Counting;

use fes_sim::commands::licenses::BuyTradeLicense;
use fes_sim::commands::trade::BuyGoods;
use scenario_play::*;

#[test]
fn repeated_previews_with_queued_orders_change_nothing() {
    let (mut sim, _) = allocations::measure("build scenario", || start(def("s3_licences_abroad")));
    let initial_probe = BuyGoods {
        good: good(&sim, "g19_0"),
        quantity: 5,
        pay_from_deposit: false,
    };
    let (_, retained) =
        allocations::measure("first preview", || sim.preview_queued(&initial_probe));
    println!("preview retained bytes: {retained}");
    order(&mut sim, Box::new(BuyTradeLicense { kind: -1 }));
    let g = good(&sim, "g19_0");
    for _ in 0..3 {
        buy(&mut sim, g, 10);
    }
    let probe = BuyGoods {
        good: g,
        quantity: 5,
        pay_from_deposit: false,
    };
    let (hash, _) = allocations::measure("world hash", || sim.world_hash());
    println!("preview_fullscale hash: {hash}");
    assert_eq!(hash, 1_121_562_210_749_400_463);
    let (first, _) = allocations::measure("queued preview", || sim.preview_queued(&probe));
    for _ in 0..3 {
        assert_eq!(sim.preview_queued(&probe), first);
        assert_eq!(
            sim.world_hash(),
            hash,
            "a preview leaves the world as it was"
        );
    }
    assert_eq!(first.error, None);
    let (bytes, _) = allocations::measure("save", || sim.save());
    let source = fes_data::FileDataSource::new(std::path::PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../fixtures/fullscale"
    )));
    let db = fes_data::database::GameDatabase::load(&source).unwrap();
    let map = fes_data::MapDef::load(&source).unwrap();
    let (loaded, _) = allocations::measure("load", || {
        fes_sim::Simulation::load(db, map, &bytes).unwrap()
    });
    assert_eq!(loaded.world_hash(), hash);
    assert!(retained <= 20_000_000, "preview retained {retained} bytes");
}
