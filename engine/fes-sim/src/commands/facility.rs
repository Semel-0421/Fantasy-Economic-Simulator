//! The player's facilities (C# `FacilityCommands.cs`, `ExpandFacilityCommand`,
//! `RepurposeFacilityCommand`).
use super::support::{fail, is_at, local_coins, nodes, player, refs, site_check};
use crate::agents::asset_prices::asking_price;
use crate::agents::banks::BankState;
use crate::agents::facilities::{FacilityTable, OWNER_NPC, OWNER_PLAYER};
use crate::agents::finance::IncomeTaxState;
use crate::agents::groups::{GroupTable, KIND_COMPANY};
use crate::agents::guidance::GuidanceSignal;
use crate::agents::investment::{
    expansion_cost, expansion_possible, may_stand, repurpose_cost, FacilityExpanded,
    FacilityRepurposed,
};
use crate::agents::loans::NpcLoanTable;
use crate::agents::money_ledger::pay_households;
use crate::agents::player::PlayerState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::world::NodeTable;
use fes_core::GuidanceAxis;

/// C# `ControlMode.Manual`.
const MODE_MANUAL: u8 = 0;

fn valid_facility(ctx: &SimContext, facility: i32) -> bool {
    facility >= 0 && (facility as usize) < ctx.world.get::<FacilityTable>().count()
}

/// C# `PlayerState.CoinsAt`.
pub fn coins_at(ctx: &SimContext, node: i32) -> i64 {
    player(ctx).coins[nodes(ctx).continent[node as usize] as usize]
}

fn change_coins_at(ctx: &mut SimContext, node: i32, delta: i64) {
    let c = ctx.services.get::<NodeTable>().continent[node as usize] as usize;
    ctx.world.get_mut::<PlayerState>().coins[c] += delta;
}

/// C# `GroupTable.PlayerCompany`.
pub fn player_company(groups: &GroupTable) -> i32 {
    (0..groups.count())
        .find(|&g| groups.is_player[g] != 0)
        .map_or(-1, |g| g as i32)
}

/// Pays coins into (positive) or takes them out of (negative) a player facility (C#
/// `MoveFacilityCashCommand`, DEC-107).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MoveFacilityCash {
    pub facility: i32,
    pub amount: i64,
}

impl MoveFacilityCash {
    /// Its cash, less the tax on its profit so far.
    fn withdrawable(&self, ctx: &SimContext) -> i64 {
        let f = self.facility as usize;
        let cash = ctx.world.get::<FacilityTable>().cash[f];
        let profit = ctx.world.get::<IncomeTaxState>().facility_profit(f, cash);
        cash - ctx
            .services
            .database
            .config
            .tax_company
            .mul_int(profit.max(0))
    }
}

impl Command for MoveFacilityCash {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.amount == 0 || self.amount == i64::MIN {
            return fail("Amount must not be zero.");
        }
        if let Some(e) = site_check(ctx, self.facility, false) {
            return fail(e);
        }
        if self.amount > 0 && local_coins(ctx) < self.amount {
            return fail("Not enough cash.");
        }
        if self.amount < 0 && self.withdrawable(ctx) < -self.amount {
            return fail("The facility does not hold that much, less this year's tax.");
        }
        CommandResult::Ok
    }

    fn apply(&self, ctx: &mut SimContext) {
        super::support::change_local_coins(ctx, -self.amount);
        let f = self.facility as usize;
        ctx.world.get_mut::<FacilityTable>().cash[f] += self.amount;
        ctx.world
            .get_mut::<IncomeTaxState>()
            .move_facility_capital(f, self.amount);
    }
}

/// Switches a player facility between direct management and automation (C#
/// `SetFacilityControlCommand`); `mode` is the `ControlMode` byte.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SetFacilityControl {
    pub facility: i32,
    pub mode: u8,
}

impl Command for SetFacilityControl {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if !valid_facility(ctx, self.facility) {
            return fail("Unknown facility.");
        }
        if self.mode > 1 {
            return fail("Unknown control mode.");
        }
        if ctx.world.get::<FacilityTable>().owner[self.facility as usize] == OWNER_PLAYER {
            CommandResult::Ok
        } else {
            fail("Facility is not owned by the player.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        ctx.world.get_mut::<FacilityTable>().mode[self.facility as usize] = self.mode;
    }
}

/// Buys an NPC facility with its cash; signals an expansion direction (C# `BuyFacilityCommand`,
/// DEC-061, DEC-107, DEC-114). `axis` is the `GuidanceAxis` byte.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct BuyFacility {
    pub facility: i32,
    pub price: i64,
    pub axis: u8,
}

impl Command for BuyFacility {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if !valid_facility(ctx, self.facility) {
            return fail("Unknown facility.");
        }
        let f = self.facility as usize;
        let facilities = ctx.world.get::<FacilityTable>();
        if facilities.level[f] == 0 {
            return fail("The facility is closed.");
        }
        if self.price < 0 {
            return fail("Price must not be negative.");
        }
        if GuidanceAxis::from_u8(self.axis).is_none() {
            return fail("Unknown guidance axis.");
        }
        if facilities.owner[f] != OWNER_NPC {
            return fail("Facility is not for sale.");
        }
        let node = facilities.node[f];
        if !is_at(ctx, node) {
            return fail("The player is not at the facility.");
        }
        let db = &ctx.services.database;
        if db.facilities[facilities.def_index[f] as usize].state_owned {
            return fail("The state does not sell this facility.");
        }
        if self.price < asking_price(&ctx.world, refs(ctx), f) {
            return fail("The seller asks more.");
        }
        if coins_at(ctx, node) < self.price {
            return fail("Not enough cash.");
        }
        CommandResult::Ok
    }

    fn apply(&self, ctx: &mut SimContext) {
        let f = self.facility as usize;
        let node = ctx.world.get::<FacilityTable>().node[f];
        change_coins_at(ctx, node, -self.price);
        // [Draft] The seller repays its bank loan from the price; the bank writes off the rest.
        let mut proceeds = self.price;
        let loans = ctx.world.get::<NpcLoanTable>();
        if let Some(loan) = loans.find(self.facility) {
            let repaid = self.price.min(loans.balance[loan]);
            let nation = loans.nation[loan] as usize;
            ctx.world.get_mut::<BankState>().reserve[nation] += repaid;
            proceeds -= repaid;
            ctx.world
                .get_mut::<NpcLoanTable>()
                .remove_row_swap_back(loan);
        }
        let seller = ctx.world.get::<FacilityTable>().group[f];
        if seller >= 0 {
            ctx.world.get_mut::<GroupTable>().cash[seller as usize] += proceeds;
        } else {
            pay_households(
                &mut ctx.world,
                ctx.services.get::<NodeTable>(),
                node,
                proceeds,
            );
        }
        let company = player_company(ctx.world.get::<GroupTable>());
        let facilities = ctx.world.get_mut::<FacilityTable>();
        facilities.owner[f] = OWNER_PLAYER;
        facilities.mode[f] = MODE_MANUAL;
        facilities.warehouse_claim_until[f] = -1;
        facilities.group[f] = company;
    }

    fn guidance(&self, ctx: &SimContext) -> Option<GuidanceSignal> {
        Some(GuidanceSignal {
            axis: GuidanceAxis::from_u8(self.axis)?,
            weight: ctx
                .services
                .database
                .config
                .guidance_weight_buy_facility
                .raw(),
            focus_node: ctx.world.get::<FacilityTable>().node[self.facility as usize],
        })
    }
}

/// Founds the player's company (C# `FoundCompanyCommand`, DEC-056, DEC-061).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FoundCompany {
    pub headquarters: i32,
    pub focus: u8,
    pub cost: i64,
}

impl Command for FoundCompany {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if self.headquarters < 0 || self.headquarters as usize >= nodes(ctx).count() {
            return fail("Unknown headquarters node.");
        }
        if self.cost < 0 {
            return fail("Cost must not be negative.");
        }
        if GuidanceAxis::from_u8(self.focus).is_none() {
            return fail("Unknown guidance axis.");
        }
        if !is_at(ctx, self.headquarters) {
            return fail("The player is not at the headquarters.");
        }
        if player_company(ctx.world.get::<GroupTable>()) >= 0 {
            return fail("The company is already founded.");
        }
        if coins_at(ctx, self.headquarters) >= self.cost {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        change_coins_at(ctx, self.headquarters, -self.cost);
        let company = ctx.world.get_mut::<GroupTable>().add(
            KIND_COMPANY,
            self.headquarters,
            -1,
            self.cost,
            true,
        );
        let facilities = ctx.world.get_mut::<FacilityTable>();
        for f in 0..facilities.count() {
            if facilities.owner[f] == OWNER_PLAYER {
                facilities.group[f] = company;
            }
        }
    }

    fn guidance(&self, ctx: &SimContext) -> Option<GuidanceSignal> {
        Some(GuidanceSignal {
            axis: GuidanceAxis::from_u8(self.focus)?,
            weight: ctx
                .services
                .database
                .config
                .guidance_weight_found_company
                .raw(),
            focus_node: self.headquarters,
        })
    }
}

/// Builds another level of a player facility (C# `ExpandFacilityCommand`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ExpandFacility {
    pub facility: i32,
}

impl Command for ExpandFacility {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if let Some(e) = site_check(ctx, self.facility, false) {
            return fail(e);
        }
        let config = &ctx.services.database.config;
        let facilities = ctx.world.get::<FacilityTable>();
        let f = self.facility as usize;
        let def = &ctx.services.database.facilities[facilities.def_index[f] as usize];
        if !expansion_possible(def, facilities.level[f], config) {
            return fail("The facility cannot grow further.");
        }
        if coins_at(ctx, facilities.node[f]) >= expansion_cost(def, facilities.level[f], config) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let f = self.facility as usize;
        let db = std::sync::Arc::clone(&ctx.services.database);
        let (node, def_index, level) = {
            let t = ctx.world.get::<FacilityTable>();
            (t.node[f], t.def_index[f], t.level[f])
        };
        let cost = expansion_cost(&db.facilities[def_index as usize], level, &db.config);
        change_coins_at(ctx, node, -cost);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), node, cost);
        let facilities = ctx.world.get_mut::<FacilityTable>();
        facilities.level[f] += 1;
        let level = facilities.level[f];
        ctx.events.publish(FacilityExpanded {
            facility: self.facility,
            level,
        });
    }
}

/// Changes the use of a facility taken over after a bankruptcy (C# `RepurposeFacilityCommand`,
/// DEC-125).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RepurposeFacility {
    pub facility: i32,
    pub def_key: String,
}

impl RepurposeFacility {
    fn target(&self, ctx: &SimContext) -> Option<usize> {
        ctx.services
            .database
            .facilities
            .iter()
            .position(|d| d.key == self.def_key)
    }
}

impl Command for RepurposeFacility {
    fn validate(&self, ctx: &SimContext) -> CommandResult {
        if let Some(e) = site_check(ctx, self.facility, false) {
            return fail(e);
        }
        let facilities = ctx.world.get::<FacilityTable>();
        let f = self.facility as usize;
        if facilities.repurposable[f] == 0 {
            return fail("The facility may not change its use.");
        }
        let db = &ctx.services.database;
        let possible = self.target(ctx).map(|t| &db.facilities[t]).filter(|def| {
            def.index != facilities.def_index[f] && may_stand(def, nodes(ctx), facilities.node[f])
        });
        let Some(def) = possible else {
            return fail("That use is not possible here.");
        };
        if coins_at(ctx, facilities.node[f]) >= repurpose_cost(def, &db.config) {
            CommandResult::Ok
        } else {
            fail("Not enough cash.")
        }
    }

    fn apply(&self, ctx: &mut SimContext) {
        let Some(t) = self.target(ctx) else { return };
        let db = std::sync::Arc::clone(&ctx.services.database);
        let def = &db.facilities[t];
        let f = self.facility as usize;
        let node = ctx.world.get::<FacilityTable>().node[f];
        let cost = repurpose_cost(def, &db.config);
        change_coins_at(ctx, node, -cost);
        pay_households(&mut ctx.world, ctx.services.get::<NodeTable>(), node, cost);
        ctx.world
            .get_mut::<FacilityTable>()
            .change_use(f, def.index);
        ctx.events.publish(FacilityRepurposed {
            facility: self.facility,
            def_index: def.index,
        });
    }
}
