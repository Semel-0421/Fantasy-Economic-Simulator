//! `preview_queued` on the full-scale world (Plan 6 Task 9): with several orders queued, repeated
//! previews agree and leave the world hash unchanged.
#![cfg(feature = "scenario")]
mod scenario_play;

use fes_sim::commands::licenses::BuyTradeLicense;
use fes_sim::commands::trade::BuyGoods;
use scenario_play::*;

#[test]
fn repeated_previews_with_queued_orders_change_nothing() {
    let mut sim = start(def("s3_licences_abroad"));
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
    let hash = sim.world_hash();
    let first = sim.preview_queued(&probe);
    for _ in 0..3 {
        assert_eq!(sim.preview_queued(&probe), first);
        assert_eq!(
            sim.world_hash(),
            hash,
            "a preview leaves the world as it was"
        );
    }
    assert_eq!(first.error, None);
}
