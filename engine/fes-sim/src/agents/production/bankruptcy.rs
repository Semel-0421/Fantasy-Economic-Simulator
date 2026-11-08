//! Bankrupt facilities get a new operator or go to the bank's auction, and lost warehouses are
//! cleared after their claim (C# `ProductionSystem.GoBankrupt`, `ExpireWarehouseClaims`,
//! `NpcLending.SeizeFacility`).
use crate::agents::asset_prices::Refs;
use crate::agents::cargo::warehouse_holder;
use crate::agents::facilities::{MODE_AUTOMATED, OWNER_BANK, OWNER_NPC, OWNER_PLAYER};
use crate::agents::money_ledger::{family_at, fund_new_operator, pay_local_households};
use crate::agents::{AuctionTable, CargoTable, FacilityTable, IncomeTaxState, NpcLoanTable};
use crate::context::SimContext;
use crate::events::EventBus;
use crate::world::{NodeBanks, NodeTable, WorldState};
use std::sync::Arc;

/// A facility ran out of cash and got a new operator (C# `FacilityBankrupt`; no subscriber).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct FacilityBankrupt {
    pub facility: i32,
    pub was_player_owned: bool,
    /// Last turn the player can take goods out of the lost warehouse, or -1.
    pub warehouse_claim_until: i32,
    pub turn: i32,
}

/// The claim on a lost warehouse ran out (C# `LostWarehouseCleared`; no subscriber).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct LostWarehouseCleared {
    pub facility: i32,
    pub turn: i32,
}

/// An NPC loan failed and the bank seized the facility (C# `NpcLoanDefaulted`, facility case).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct NpcLoanDefaulted {
    pub facility: i32,
    /// -1: a facility, not a merchant group.
    pub merchant: i32,
    pub nation_node: i32,
    pub turn: i32,
}

fn start_cash(ctx: &SimContext, f: usize) -> i64 {
    let db = &ctx.services.database;
    let def = ctx.world.get::<FacilityTable>().def_index[f];
    db.config.facility_start_cash[db.facilities[def as usize].tier as usize]
}

/// A new NPC operator takes over the same row, so facility ids stay stable.
pub fn go_bankrupt(ctx: &mut SimContext, f: usize) {
    let db = Arc::clone(&ctx.services.database);
    let turn = ctx.turn();
    let was_player = ctx.world.get::<FacilityTable>().owner[f] == OWNER_PLAYER;
    if !was_player && ctx.world.get::<NpcLoanTable>().find(f as i32).is_some() {
        seize_facility(ctx, f);
        let facilities = ctx.world.get_mut::<FacilityTable>();
        facilities.loss_years[f] = 0;
        let until = facilities.warehouse_claim_until[f];
        ctx.events.publish(FacilityBankrupt {
            facility: f as i32,
            was_player_owned: false,
            warehouse_claim_until: until,
            turn,
        });
        return;
    }
    let nodes = ctx.services.get::<NodeTable>();
    let facilities = ctx.world.get_mut::<FacilityTable>();
    let claim_until = if was_player {
        turn + db.config.lost_warehouse_weeks
    } else {
        facilities.warehouse_claim_until[f]
    };
    facilities.owner[f] = OWNER_NPC;
    facilities.mode[f] = MODE_AUTOMATED;
    let node = facilities.node[f];
    if was_player {
        let family = family_at(&mut ctx.world, nodes, &db.config, node);
        let facilities = ctx.world.get_mut::<FacilityTable>();
        facilities.group[f] = family;
        facilities.repurposable[f] = 1;
    }
    let (group, old_cash) = {
        let facilities = ctx.world.get::<FacilityTable>();
        (facilities.group[f], facilities.cash[f])
    };
    let new_cash = start_cash(ctx, f);
    let cash = fund_new_operator(&mut ctx.world, nodes, group, node, old_cash, new_cash);
    restart_facility(ctx.world.get_mut::<IncomeTaxState>(), f, cash);
    let facilities = ctx.world.get_mut::<FacilityTable>();
    facilities.cash[f] = cash;
    facilities.negative_cash_weeks[f] = 0;
    facilities.loss_years[f] = 0;
    facilities.warehouse_claim_until[f] = claim_until;
    ctx.events.publish(FacilityBankrupt {
        facility: f as i32,
        was_player_owned: was_player,
        warehouse_claim_until: claim_until,
        turn,
    });
}

/// C# `IncomeTaxState.RestartFacility`.
pub fn restart_facility(books: &mut IncomeTaxState, f: usize, cash: i64) {
    if f >= books.facility_start.len() {
        return;
    }
    books.facility_start[f] = cash;
    books.facility_dividends[f] = 0;
}

/// The bank takes an NPC facility whose loan failed and auctions it (DEC-037, DEC-039).
pub fn seize_facility(ctx: &mut SimContext, f: usize) {
    let db = Arc::clone(&ctx.services.database);
    let turn = ctx.turn();
    let r = Refs {
        db: &db,
        nodes: ctx.services.get::<NodeTable>(),
        banks: ctx.services.get::<NodeBanks>(),
    };
    seize_facility_in(&mut ctx.world, &mut ctx.events, r, turn, f);
}

/// C# `NpcLending.SeizeFacility` over the world.
pub fn seize_facility_in(
    world: &mut WorldState,
    events: &mut EventBus,
    r: Refs,
    turn: i32,
    f: usize,
) {
    let db = r.db;
    let loans = world.get_mut::<NpcLoanTable>();
    let row = loans.find(f as i32).expect("a facility with a loan");
    let nation = loans.nation[row];
    loans.remove_row_swap_back(row);

    let nodes = r.nodes;
    let facilities = world.get_mut::<FacilityTable>();
    facilities.owner[f] = OWNER_BANK;
    facilities.mode[f] = MODE_AUTOMATED;
    let (cash, node) = (facilities.cash[f], facilities.node[f]);
    if cash < 0 {
        pay_local_households(world, nodes, node, cash);
    }
    let facilities = world.get_mut::<FacilityTable>();
    facilities.cash[f] = cash.max(0);
    facilities.negative_cash_weeks[f] = 0;
    let tier = db.facilities[facilities.def_index[f] as usize].tier as usize;
    let value = db.config.facility_value[tier] * i64::from(facilities.level[f]);
    restart_facility(world.get_mut::<IncomeTaxState>(), f, cash.max(0));
    let reserve = db.config.auction_reserve_share.mul_int(value);
    let auctions = world.get_mut::<AuctionTable>();
    auctions.facility.push(f as i32);
    auctions.nation.push(nation);
    auctions.closes_turn.push(turn + db.config.auction_weeks);
    auctions.reserve_price.push(reserve);
    auctions.player_bid.push(0);
    auctions.for_player_loan.push(0);
    let nation_node = r.banks.nation_node(nodes, nation);
    events.publish(NpcLoanDefaulted {
        facility: f as i32,
        merchant: -1,
        nation_node,
        turn,
    });
}

/// After the claim period, whatever the player left in a lost warehouse is gone (DEC-052).
pub fn expire_warehouse_claims(ctx: &mut SimContext) {
    let turn = ctx.turn();
    for f in 0..ctx.world.get::<FacilityTable>().count() {
        let facilities = ctx.world.get_mut::<FacilityTable>();
        let until = facilities.warehouse_claim_until[f];
        if until < 0 || turn < until {
            continue;
        }
        facilities.warehouse_claim_until[f] = -1;
        ctx.world
            .get_mut::<CargoTable>()
            .clear(warehouse_holder(f as i32));
        ctx.events.publish(LostWarehouseCleared {
            facility: f as i32,
            turn,
        });
    }
}
