//! The player's bond trades (C# `BondCommands.cs`, DEC-102).
use super::support::{branch, change_local_coins, fail, local_coins, nodes};
use crate::agents::asset_prices::facility_value;
use crate::agents::bond_desk::{company_room, offered, offers_of, raise};
use crate::agents::credit::{BondOffers, BondTable, CreditStats};
use crate::agents::credit_system::state_room;
use crate::agents::facilities::{FacilityTable, OWNER_NPC};
use crate::agents::finance::{event_kind, FinanceState};
use crate::agents::groups::GroupTable;
use crate::agents::money_ledger::pay_nation_town_wages;
use crate::agents::purses::PurseState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_data::database::GameDatabase;

/// C# `BondTable.PlayerHolder`.
pub const BOND_PLAYER_HOLDER: i32 = i32::MIN;

/// The player bought (positive) or sold (negative) bonds of an issuer (C# `BondTraded`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BondTraded {
    pub issuer: i32,
    pub face: i64,
}

fn bond_row(bonds: &BondTable, issuer: i32, holder: i32) -> Option<usize> {
    (0..bonds.count()).find(|&r| bonds.issuer[r] == issuer && bonds.holder[r] == holder)
}

/// C# `BondTable.Held`.
pub fn bond_held(bonds: &BondTable, issuer: i32, holder: i32) -> i64 {
    bond_row(bonds, issuer, holder).map_or(0, |r| bonds.face[r])
}

/// C# `BondTable.DebtOf`.
pub fn debt_of(bonds: &BondTable, issuer: i32) -> i64 {
    (0..bonds.count())
        .filter(|&r| bonds.issuer[r] == issuer)
        .map(|r| bonds.face[r])
        .sum()
}

/// C# `BondDesk.CompanyRoom(world, db, group)`.
pub fn company_room_of(world: &WorldState, db: &GameDatabase, group: i32) -> i64 {
    let facilities = world.get::<FacilityTable>();
    let value: i64 = (0..facilities.count())
        .filter(|&f| facilities.group[f] == group && facilities.owner[f] == OWNER_NPC)
        .map(|f| facility_value(db, facilities, f))
        .sum();
    company_room(&db.config, value, debt_of(world.get::<BondTable>(), group))
}

/// C# `PlayerBondDesk.NationOf`.
fn nation_of(ctx: &SimContext, issuer: i32) -> i32 {
    let purses = ctx.world.get::<PurseState>();
    let nations = purses.nation_count as i32;
    if issuer < 0 {
        return if !issuer < nations { !issuer } else { -1 };
    }
    let groups = ctx.world.get::<GroupTable>();
    if issuer as usize >= groups.count() || groups.is_player[issuer as usize] != 0 {
        return -1;
    }
    let nation = purses.node_float[groups.node[issuer as usize] as usize];
    if nation < nations {
        nation
    } else {
        -1
    }
}

/// C# `PlayerBondDesk.Check`.
fn desk_check(ctx: &SimContext, issuer: i32, amount: i64) -> Result<i32, &'static str> {
    let nation = nation_of(ctx, issuer);
    if amount <= 0 {
        return Err("Amount must be positive.");
    }
    if nation < 0 {
        return Err("Those bonds are not for sale.");
    }
    let here = branch(ctx)?;
    if here as i32 == nation {
        Ok(nation)
    } else {
        Err("Those bonds trade at the issuer's own bank.")
    }
}

/// Buys new bonds at the issuer's bank (C# `BuyBondCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyBond {
    pub issuer: i32,
    pub amount: i64,
}

impl Command for BuyBond {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let nation = match desk_check(ctx, self.issuer, self.amount) {
            Ok(n) => n,
            Err(e) => return fail(e),
        };
        if local_coins(ctx) < self.amount {
            return fail("Not enough cash.");
        }
        if ctx.world.get::<FinanceState>().event_of(nation as usize) == event_kind::CREDIT_CRUNCH {
            return fail("No bonds are sold in a credit crunch.");
        }
        let db = &ctx.services.database;
        let room = if self.issuer < 0 {
            state_room(
                &ctx.world,
                db,
                nodes(ctx),
                ctx.services.get::<NodeBanks>(),
                nation,
            )
        } else {
            company_room_of(&ctx.world, db, self.issuer)
        };
        if self.amount <= room {
            CommandResult::Ok
        } else {
            fail(format!("The issuer may owe only {room} more."))
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let nation = nation_of(ctx, self.issuer);
        change_local_coins(ctx, -self.amount);
        ctx.world
            .get_mut::<BondTable>()
            .add(self.issuer, BOND_PLAYER_HOLDER, self.amount);
        if self.issuer < 0 {
            let n = ctx.services.get::<NodeTable>();
            pay_nation_town_wages(
                &mut ctx.world,
                n,
                &ctx.services.database.config,
                nation,
                self.amount,
            );
            ctx.services.get_mut::<CreditStats>().state_raised += self.amount;
        } else {
            ctx.world.get_mut::<GroupTable>().cash[self.issuer as usize] += self.amount;
            ctx.services.get_mut::<CreditStats>().company_raised += self.amount;
        }
        ctx.events.publish(BondTraded {
            issuer: self.issuer,
            face: self.amount,
        });
    }
}

/// Sells bonds at their face to whoever at the issuer's bank has idle money (C#
/// `SellBondCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SellBond {
    pub issuer: i32,
    pub amount: i64,
}

impl Command for SellBond {
    fn prepare(&self, ctx: &mut SimContext) {
        if desk_check(ctx, self.issuer, self.amount).is_err() {
            return;
        }
        if bond_held(
            ctx.world.get::<BondTable>(),
            self.issuer,
            BOND_PLAYER_HOLDER,
        ) < self.amount
        {
            return;
        }
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        offers_of(&mut offers, &ctx.world, &ctx.services.database.config, turn);
        *ctx.services.get_mut::<BondOffers>() = offers;
    }

    fn validate(&self, ctx: &SimContext) -> CommandResult {
        let nation = match desk_check(ctx, self.issuer, self.amount) {
            Ok(n) => n,
            Err(e) => return fail(e),
        };
        if bond_held(
            ctx.world.get::<BondTable>(),
            self.issuer,
            BOND_PLAYER_HOLDER,
        ) < self.amount
        {
            return fail("You do not hold that much.");
        }
        // `prepare` built this turn's offers; ask a copy so validation changes nothing.
        let mut offers = ctx.services.get::<BondOffers>().clone();
        let buyer = if self.issuer < 0 { -1 } else { self.issuer };
        let o = offered(
            &mut offers,
            &ctx.world,
            &ctx.services.database.config,
            ctx.turn(),
            nation,
            buyer,
        );
        if o > 0 {
            CommandResult::Ok
        } else {
            fail("No one is buying this week.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let nation = nation_of(ctx, self.issuer);
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let sold = raise(
            &mut ctx.world,
            &mut offers,
            &ctx.services.database.config,
            turn,
            self.issuer,
            nation,
            self.amount,
        );
        *ctx.services.get_mut::<BondOffers>() = offers;
        let bonds = ctx.world.get_mut::<BondTable>();
        if let Some(r) = bond_row(bonds, self.issuer, BOND_PLAYER_HOLDER) {
            bonds.face[r] -= sold;
        }
        change_local_coins(ctx, sold);
        ctx.events.publish(BondTraded {
            issuer: self.issuer,
            face: -sold,
        });
    }
}
