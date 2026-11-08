//! `agents.credit` (C# `CreditSystem`, `BillMarket`, DEC-097, DEC-122, DEC-123, DEC-129): traders
//! pay the bills due within a share of their float, groups in the red sell their bills, issuers
//! service their bonds, groups keep their facilities in working cash, nations whose towns fall
//! short sell bonds, and the week's bill room is set. The week's bond offers end here.
use super::asset_prices::{facility_upkeep, list_shares, Refs};
use super::banks::BankState;
use super::bond_desk::{raise, take_from_households, CompanyRooms};
use super::credit::{BillRoom, BillTable, BondOffers, BondTable, CreditStats};
use super::facilities::{FacilityTable, OWNER_NPC};
use super::finance::event_kind::CREDIT_CRUNCH;
use super::finance::{FinanceState, IncomeTaxState};
use super::groups::GroupTable;
use super::merchants::AgentTable;
use super::money_ledger::{pay_nation_households, pay_nation_town_wages, town_shortfall};
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::{CurrencyState, HouseholdCash, MarketStore};
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::Fx;
use fes_data::database::GameDatabase;

/// C# `FinanceOrder.Credit`.
pub const ORDER: i32 = sim_order::LATE + 28;
const WEEKS_PER_YEAR: i64 = 52;

/// C# `BillHolders`.
pub const BILL_BANK: i32 = -1;
pub const BILL_HOUSEHOLDS: i32 = -2;
const BILL_FACILITY_BASE: i32 = -3;
/// C# `BondTable.PlayerHolder`.
pub const BOND_PLAYER_HOLDER: i32 = i32::MIN;

/// C# `BillHolders.Facility`.
pub fn bill_facility(facility: i32) -> i32 {
    BILL_FACILITY_BASE - facility
}

/// A company too long behind on its bonds defaulted (C# `BondDefaulted`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct BondDefaulted {
    pub group: i32,
    pub turn: i32,
}

/// C# `BillMarket.Price`: a bill's face as far as the traders' coins cover all they owe on
/// bills, less the discount.
pub fn bill_price(face: i64, trader_coins: i64, owed: i64, discount: Fx) -> i64 {
    if trader_coins <= 0 || owed <= 0 {
        return 0;
    }
    let collectable = if trader_coins >= owed {
        face
    } else {
        (i128::from(face) * i128::from(trader_coins) / i128::from(owed)) as i64
    };
    collectable - discount.mul_int(collectable)
}

/// C# `BillMarket.Room`.
pub fn bill_room(wishes_room: i64, trader_coins: i64) -> i64 {
    wishes_room.min(trader_coins.max(0))
}

/// C# `BondDesk.StateRoom`: how much more a nation may owe.
pub fn state_room(
    world: &WorldState,
    db: &GameDatabase,
    nodes: &NodeTable,
    banks: &NodeBanks,
    nation: i32,
) -> i64 {
    let continent = nodes.continent[banks.nation_node(nodes, nation) as usize];
    let nations = (0..banks.nation_count(nodes))
        .filter(|&n| nodes.continent[banks.nation_node(nodes, n) as usize] == continent)
        .count() as i64;
    let supply = world.get::<CurrencyState>().coin_supply[continent as usize];
    let limit = db.config.state_debt_share.mul_int(supply / nations.max(1));
    let state = !nation;
    let bonds = world.get::<BondTable>();
    let debt: i64 = (0..bonds.count())
        .filter(|&r| bonds.issuer[r] == state)
        .map(|r| bonds.face[r])
        .sum();
    (limit - debt).max(0)
}

/// `agents.credit`. The scratch fields are refreshed at the start of the step that uses them.
#[derive(Default)]
pub struct CreditSystem {
    state_behind: Vec<bool>,
    float_pot: Vec<i64>,
    owed: Vec<i64>,
    rooms: CompanyRooms,
}

impl System for CreditSystem {
    fn id(&self) -> &'static str {
        "agents.credit"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn initialize(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let s = &ctx.services;
        refresh_room(
            &mut ctx.world,
            &s.database,
            s.get::<NodeTable>(),
            s.get::<NodeBanks>(),
        );
        Ok(())
    }

    /// The week's offers are kept by turn number, which a load can bring back.
    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        ctx.services.get_mut::<BondOffers>().turn = -1;
        Ok(())
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let mut offers = std::mem::take(ctx.services.get_mut::<BondOffers>());
        let mut stats = std::mem::take(ctx.services.get_mut::<CreditStats>());
        let s = &ctx.services;
        let mut c = Credit {
            sys: self,
            world: &mut ctx.world,
            events: &mut ctx.events,
            offers: &mut offers,
            stats: &mut stats,
            db: &s.database,
            nodes: s.get::<NodeTable>(),
            banks: s.get::<NodeBanks>(),
            turn,
        };
        c.pay_bills();
        c.sell_bills_of_groups_in_the_red();
        c.service_bonds();
        c.keep_facilities_in_cash();
        c.sell_state_bonds();
        refresh_room(c.world, c.db, c.nodes, c.banks);
        offers.turn = -1;
        *ctx.services.get_mut::<BondOffers>() = offers;
        *ctx.services.get_mut::<CreditStats>() = stats;
        Ok(())
    }
}

struct Credit<'a> {
    sys: &'a mut CreditSystem,
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    offers: &'a mut BondOffers,
    stats: &'a mut CreditStats,
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    turn: i32,
}

impl Credit<'_> {
    /// C# `PayBills`.
    fn pay_bills(&mut self) {
        let config = &self.db.config;
        let floats = self.world.get::<PurseState>().float.clone();
        self.sys.float_pot = floats
            .iter()
            .map(|&f| config.bill_repay_share.mul_int(f.max(0)))
            .collect();
        let mut r = self.world.get::<BillTable>().count();
        while r > 0 {
            r -= 1;
            let bills = self.world.get::<BillTable>();
            if bills.due_turn[r] > self.turn {
                continue;
            }
            let purse = bills.purse[r] as usize;
            let paid = bills.face[r].min(self.sys.float_pot[purse]);
            if paid > 0 {
                let holder = bills.holder[r];
                self.sys.float_pot[purse] -= paid;
                self.world.get_mut::<PurseState>().float[purse] -= paid;
                self.pay_holder(holder, purse, paid);
                self.world.get_mut::<BillTable>().face[r] -= paid;
                self.stats.bills_paid += paid;
            }
            let bills = self.world.get_mut::<BillTable>();
            if bills.face[r] <= 0 {
                bills.remove_row_swap_back(r);
            } else if self.turn - bills.due_turn[r] >= config.bill_default_weeks {
                // Protested: the holder is left with the loss.
                self.stats.bills_lost += bills.face[r];
                bills.remove_row_swap_back(r);
            }
        }
    }

    fn pay_holder(&mut self, holder: i32, purse: usize, coins: i64) {
        if holder >= 0 {
            self.world.get_mut::<AgentTable>().cash[holder as usize] += coins;
        } else if holder == BILL_BANK {
            self.world.get_mut::<BankState>().reserve[purse] += coins;
        } else if holder == BILL_HOUSEHOLDS {
            pay_nation_households(self.world, purse as i32, coins);
        } else if holder <= BILL_FACILITY_BASE {
            let f = (BILL_FACILITY_BASE - holder) as usize;
            self.world.get_mut::<FacilityTable>().cash[f] += coins;
        }
    }

    /// C# `SellBillsOfGroupsInTheRed` (DEC-122).
    fn sell_bills_of_groups_in_the_red(&mut self) {
        let config = &self.db.config;
        let count = self.world.get::<BillTable>().count();
        if config.bill_sale_discount.raw() <= 0 || count == 0 {
            return;
        }
        let purses = self.world.get::<PurseState>();
        let nation_count = purses.nation_count;
        self.sys.owed.clear();
        self.sys.owed.resize(purses.float.len(), 0);
        let bills = self.world.get::<BillTable>();
        for r in 0..count {
            self.sys.owed[bills.purse[r] as usize] += bills.face[r];
        }
        for r in (0..count).rev() {
            let bills = self.world.get::<BillTable>();
            let holder = bills.holder[r];
            if holder >= 0
                && self.world.get::<AgentTable>().cash[holder as usize] < 0
                && (bills.purse[r] as usize) < nation_count
            {
                self.sell_bill(r, holder);
            }
        }
    }

    /// C# `SellBill`: as far as buyers pay; what they do not pay for stays with the group.
    fn sell_bill(&mut self, r: usize, holder: i32) {
        let bills = self.world.get::<BillTable>();
        let purse = bills.purse[r];
        let face = bills.face[r];
        let float = self.world.get::<PurseState>().float[purse as usize];
        let price = bill_price(
            face,
            float,
            self.sys.owed[purse as usize],
            self.db.config.bill_sale_discount,
        );
        if price <= 0 {
            return;
        }
        let paid = take_from_households(
            self.world,
            self.offers,
            &self.db.config,
            self.turn,
            purse,
            price,
        );
        if paid <= 0 {
            return;
        }
        let sold = if paid >= price {
            face
        } else {
            (i128::from(face) * i128::from(paid) / i128::from(price)) as i64
        };
        let bills = self.world.get_mut::<BillTable>();
        if sold < face {
            bills.face[r] -= sold;
            let due = bills.due_turn[r];
            bills.holder.push(BILL_HOUSEHOLDS);
            bills.purse.push(purse);
            bills.face.push(sold);
            bills.due_turn.push(due);
        } else {
            bills.holder[r] = BILL_HOUSEHOLDS;
        }
        self.world.get_mut::<AgentTable>().cash[holder as usize] += paid;
        self.stats.bills_sold += paid;
        self.stats.bills_sold_face += sold;
    }

    /// C# `ServiceBonds`: a default restarts the scan.
    fn service_bonds(&mut self) {
        let nations = self.banks.nation_count(self.nodes) as usize;
        self.sys.state_behind.clear();
        self.sys.state_behind.resize(nations, false);
        let mut r = self.world.get::<BondTable>().count();
        while r > 0 {
            r -= 1;
            self.service_bond(r);
        }
        let mut r = self.world.get::<BondTable>().count() as i64 - 1;
        while r >= 0 {
            let bonds = self.world.get::<BondTable>();
            let i = r as usize;
            if bonds.issuer[i] >= 0 && bonds.behind_weeks[i] >= self.db.config.company_default_weeks
            {
                self.default_company(bonds.issuer[i]);
                r = self.world.get::<BondTable>().count() as i64;
            }
            r -= 1;
        }
    }

    fn service_bond(&mut self, r: usize) {
        let config = &self.db.config;
        let bonds = self.world.get_mut::<BondTable>();
        let issuer = bonds.issuer[r];
        let state = issuer < 0;
        let (rate, weeks) = if state {
            (config.state_bond_interest, config.state_bond_weeks)
        } else {
            (config.company_bond_interest, config.company_bond_weeks)
        };
        bonds.interest[r] += rate.mul_int(bonds.face[r]) / WEEKS_PER_YEAR;
        let principal = if bonds.face[r] <= 0 {
            0
        } else {
            (bonds.face[r] / i64::from(weeks)).max(1)
        };
        let due = bonds.interest[r] + principal;
        let funds = if state {
            self.world.get::<BankState>().spendable(!issuer as usize)
        } else {
            self.world.get::<GroupTable>().cash[issuer as usize].max(0)
        };
        let paid = due.min(funds);
        if state {
            self.world.get_mut::<BankState>().reserve[!issuer as usize] -= paid;
        } else {
            self.world.get_mut::<GroupTable>().cash[issuer as usize] -= paid;
        }
        let bonds = self.world.get_mut::<BondTable>();
        let to_interest = paid.min(bonds.interest[r]);
        bonds.interest[r] -= to_interest;
        let to_face = paid - to_interest;
        bonds.face[r] -= to_face;
        let holder = bonds.holder[r];
        self.pay_bond_holder(holder, issuer, paid, to_face);
        self.stats.bonds_paid += paid;
        let bonds = self.world.get_mut::<BondTable>();
        if paid >= due {
            bonds.behind_weeks[r] = 0;
        } else {
            bonds.behind_weeks[r] += 1;
            if state {
                self.sys.state_behind[!issuer as usize] = true;
            }
        }
        if bonds.face[r] <= 0 && bonds.interest[r] <= 0 {
            bonds.remove_row_swap_back(r);
        }
    }

    fn default_company(&mut self, group: i32) {
        let bonds = self.world.get_mut::<BondTable>();
        let mut k = bonds.count();
        while k > 0 {
            k -= 1;
            if bonds.issuer[k] != group {
                continue;
            }
            self.stats.bonds_lost += bonds.face[k];
            bonds.remove_row_swap_back(k);
        }
        self.stats.defaults += 1;
        self.events.publish(BondDefaulted {
            group,
            turn: self.turn,
        });
    }

    fn pay_bond_holder(&mut self, holder: i32, issuer: i32, paid: i64, face: i64) {
        if holder == BOND_PLAYER_HOLDER {
            let nation = if issuer < 0 {
                !issuer
            } else {
                let node = self.world.get::<GroupTable>().node[issuer as usize];
                self.world.get::<PurseState>().node_float[node as usize]
            } as usize;
            let bank = self.world.get_mut::<BankState>();
            bank.player_deposit[nation] += paid;
            bank.reserve[nation] += paid;
            return;
        }
        if holder < 0 {
            self.world.get_mut::<GroupTable>().cash[!holder as usize] += paid;
            return;
        }
        let cash = self.world.get_mut::<HouseholdCash>();
        cash.cash[holder as usize] += paid;
        cash.saved_year[holder as usize] -= face; // interest is income, the rest was theirs
    }

    /// C# `KeepFacilitiesInCash`.
    fn keep_facilities_in_cash(&mut self) {
        if self.db.config.working_weeks <= 0 {
            return;
        }
        self.sys.rooms.refresh(self.world, self.db);
        for f in 0..self.world.get::<FacilityTable>().count() {
            let facilities = self.world.get::<FacilityTable>();
            let group = facilities.group[f];
            if group < 0
                || facilities.owner[f] != OWNER_NPC
                || self.world.get::<GroupTable>().is_player[group as usize] != 0
            {
                continue;
            }
            let gap = working_cash_gap(self.world, self.db, self.nodes, f);
            if gap > 0 {
                self.top_up(f, group as usize, gap);
            }
        }
    }

    /// C# `TopUp`.
    fn top_up(&mut self, f: usize, group: usize, mut gap: i64) {
        let config = &self.db.config;
        let groups = self.world.get::<GroupTable>();
        let keep = config.group_start_cash[groups.kind[group] as usize];
        let own = gap.min((groups.cash[group] - keep).max(0));
        if own > 0 {
            self.world.get_mut::<GroupTable>().cash[group] -= own;
            self.world.get_mut::<FacilityTable>().cash[f] += own;
            self.world
                .get_mut::<IncomeTaxState>()
                .move_facility_capital(f, own);
            gap -= own;
            self.stats.groups_topped_up += own;
        }
        let purses = self.world.get::<PurseState>();
        let nation = purses.node_float[self.world.get::<GroupTable>().node[group] as usize];
        if gap <= 0 || nation as usize >= purses.nation_count {
            return;
        }
        gap -= self.list_shares(f, nation, gap);
        if gap <= 0
            || config.company_debt_share.raw() <= 0
            || self.world.get::<FinanceState>().event_of(nation as usize) == CREDIT_CRUNCH
        {
            return;
        }
        let amount = gap.min(self.sys.rooms.room(config, group));
        let raised = raise(
            self.world,
            self.offers,
            config,
            self.turn,
            group as i32,
            nation,
            amount,
        );
        self.sys.rooms.add_debt(group, raised);
        self.world.get_mut::<FacilityTable>().cash[f] += raised;
        self.world
            .get_mut::<IncomeTaxState>()
            .move_facility_capital(f, raised);
        self.stats.company_raised += raised;
    }

    /// C# `ListShares` (DEC-129): state facilities list none.
    fn list_shares(&mut self, f: usize, nation: i32, gap: i64) -> i64 {
        let def = self.world.get::<FacilityTable>().def_index[f] as usize;
        if self.db.config.facility_max_listed_shares <= 0 || self.db.facilities[def].state_owned {
            return 0;
        }
        let r = Refs {
            db: self.db,
            nodes: self.nodes,
            banks: self.banks,
        };
        let raised = list_shares(self.world, self.offers, r, self.turn, f, nation, gap);
        if raised <= 0 {
            return 0;
        }
        self.world.get_mut::<FacilityTable>().cash[f] += raised;
        self.world
            .get_mut::<IncomeTaxState>()
            .move_facility_capital(f, raised);
        self.stats.shares_listed += raised;
        raised
    }

    /// C# `SellStateBonds`.
    fn sell_state_bonds(&mut self) {
        let config = &self.db.config;
        if config.state_debt_share.raw() <= 0 {
            return;
        }
        for n in 0..self.banks.nation_count(self.nodes) {
            let nu = n as usize;
            if self.sys.state_behind[nu]
                || self.world.get::<FinanceState>().event_of(nu) == CREDIT_CRUNCH
            {
                continue;
            }
            let room = state_room(self.world, self.db, self.nodes, self.banks, n);
            if room <= 0 {
                continue;
            }
            let cash = self.world.get::<HouseholdCash>();
            let shortfall: i64 = self.world.get::<PurseState>().nation_nodes[nu]
                .iter()
                .map(|&node| town_shortfall(self.nodes, cash, config, node).max(0))
                .sum();
            if shortfall <= 0 {
                continue;
            }
            let raised = raise(
                self.world,
                self.offers,
                config,
                self.turn,
                !n,
                n,
                shortfall.min(room),
            );
            if raised <= 0 {
                continue;
            }
            pay_nation_town_wages(self.world, self.nodes, config, n, raised);
            self.stats.state_raised += raised;
        }
    }
}

/// C# `WorkingCashGap`: what a facility worth running lacks for its next weeks; 0 when its goods
/// are not worth making.
pub fn working_cash_gap(world: &WorldState, db: &GameDatabase, nodes: &NodeTable, f: usize) -> i64 {
    let facilities = world.get::<FacilityTable>();
    let markets = world.get::<MarketStore>();
    let recipe = &db.recipes[db.facilities[facilities.def_index[f] as usize].recipe];
    let node = facilities.node[f];
    let price_of = |good: i32| {
        find_nearest_market(nodes, markets, node, good)
            .map_or(0, |(_, c)| i64::from(markets.price[c]))
    };
    let inputs: i64 = recipe
        .inputs
        .iter()
        .map(|l| i64::from(l.quantity) * price_of(l.good.0))
        .sum();
    let outputs: i64 = recipe
        .outputs
        .iter()
        .map(|l| i64::from(l.quantity) * price_of(l.good.0))
        .sum();
    if outputs - db.config.trader_spread.mul_int(outputs) <= inputs {
        return 0;
    }
    i64::from(db.config.working_weeks)
        * (facility_upkeep(db, facilities, f) + inputs * i64::from(facilities.level[f]))
        - facilities.cash[f]
}

/// C# `RefreshRoom`: traders may owe weeks of what households wish to buy, and the bank may
/// discount down to a floor under its target.
pub fn refresh_room(
    world: &mut WorldState,
    db: &GameDatabase,
    nodes: &NodeTable,
    banks: &NodeBanks,
) {
    let config = &db.config;
    let mut room = std::mem::replace(world.get_mut::<BillRoom>(), BillRoom::new(0));
    room.room.fill(0);
    room.bank_room.fill(0);
    if config.bill_cap_weeks > 0 {
        let purses = world.get::<PurseState>();
        let cash = world.get::<HouseholdCash>();
        let finance = world.get::<FinanceState>();
        let bank = world.get::<BankState>();
        let supply = &world.get::<CurrencyState>().coin_supply;
        let nation_count = banks.nation_count(nodes);
        for n in 0..nation_count {
            let nu = n as usize;
            let wishes: i64 = purses.nation_nodes[nu]
                .iter()
                .map(|&node| cash.desired[node as usize])
                .sum();
            room.room[nu] = bill_room(i64::from(config.bill_cap_weeks) * wishes, purses.float[nu]);
            if finance.event_of(nu) == CREDIT_CRUNCH {
                continue;
            }
            let continent = nodes.continent[banks.nation_node(nodes, n) as usize];
            let on_continent = (0..nation_count)
                .filter(|&k| nodes.continent[banks.nation_node(nodes, k) as usize] == continent)
                .count() as i64;
            let share = config
                .bank_initial_reserve_share
                .mul_int(supply[continent as usize])
                / on_continent.max(1);
            let target = config.state_reserve_target.mul_int(share);
            room.bank_room[nu] =
                (bank.spendable(nu) - config.bill_bank_floor.mul_int(target)).max(0);
        }
        let bills = world.get::<BillTable>();
        for r in 0..bills.count() {
            let p = bills.purse[r] as usize;
            room.room[p] = (room.room[p] - bills.face[r]).max(0);
        }
    }
    *world.get_mut::<BillRoom>() = room;
}
