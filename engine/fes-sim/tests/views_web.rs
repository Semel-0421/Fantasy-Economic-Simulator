//! The read-only views the web panels need beyond the player's position: the shares listed at the
//! bank the player stands in (exchange panel) and the merchant caravans on the road (map).
mod common;

use common::build_sample;
use fes_sim::agents::facilities::{FacilityTable, OWNER_NPC};
use fes_sim::agents::player::PlayerState;
use fes_sim::agents::AgentTable;
use fes_sim::commands::securities::BuyShare;
use fes_sim::world::{NodeBanks, NodeTable};
use fes_sim::{views, Command, CommandResult, Simulation};

fn listed_facility(sim: &Simulation) -> (usize, i32) {
    let ctx = sim.context();
    let t = ctx.world.get::<FacilityTable>();
    let n = ctx.services.get::<NodeTable>();
    let b = ctx.services.get::<NodeBanks>();
    let db = &ctx.services.database;
    let f = (0..t.count())
        .find(|&f| {
            t.owner[f] == OWNER_NPC
                && t.group[f] >= 0
                && t.level[f] > 0
                && !db.facilities[t.def_index[f] as usize].state_owned
                && b.nation_of(n, t.node[f]) >= 0
        })
        .expect("a listed facility");
    let nation = b.nation_of(n, t.node[f]);
    let branch = (0..n.count() as i32)
        .find(|&x| b.branch_nation(n, x) == nation)
        .expect("a branch");
    (f, branch)
}

#[test]
fn the_exchange_lists_what_buy_share_accepts_at_the_players_bank() {
    let mut sim = build_sample(&[]);
    let (f, branch) = listed_facility(&sim);
    sim.context_mut().world.get_mut::<PlayerState>().node = branch;
    let list = views::exchange(sim.context());
    let row = list
        .iter()
        .find(|r| r.facility == f as i32)
        .expect("listed here");
    assert_eq!(0, row.held);
    assert!(row.price > 0 && row.room > 0 && !row.name.is_empty());
    // Every listed row is one a BuyShare of one share would pass on the exchange rules.
    for r in &list {
        let buy = BuyShare {
            facility: r.facility,
            count: 1,
        };
        if let CommandResult::Error(e) = buy.validate(sim.context()) {
            assert!(
                e == "Not enough cash." || e == "The group keeps control of it.",
                "{e}"
            );
        }
    }
    // On the road nothing is listed.
    sim.context_mut()
        .world
        .get_mut::<PlayerState>()
        .travel_destination = branch;
    assert!(views::exchange(sim.context()).is_empty());
}

#[test]
fn caravans_are_the_merchants_on_the_road() {
    let mut sim = build_sample(&[]);
    for _ in 0..3 {
        sim.step().expect("a week");
    }
    let caravans = views::caravans(sim.context());
    let agents = sim.world().get::<AgentTable>();
    let travelling: Vec<_> = (0..agents.count())
        .filter(|&a| agents.destination[a] >= 0)
        .collect();
    assert_eq!(travelling.len(), caravans.len());
    assert!(!caravans.is_empty(), "some merchants travel by week 3");
    for (c, &a) in caravans.iter().zip(&travelling) {
        assert_eq!(
            (
                agents.node[a],
                agents.destination[a],
                agents.arrival_turn[a]
            ),
            (c.from, c.to, c.arrival_turn)
        );
    }
}
