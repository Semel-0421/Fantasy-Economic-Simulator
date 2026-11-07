//! `agents.production` (C# `ProductionSystem`): facilities turn recipe inputs into outputs every
//! week, sell or store them, pay upkeep and dividends, and go bankrupt after weeks below zero cash.
pub mod bankruptcy;
pub mod job;
pub mod lines;
pub mod upkeep;

use self::bankruptcy::{expire_warehouse_claims, go_bankrupt};
use self::job::{JobParams, PlayerOutput, ProductionJob};
use self::lines::{build_lines, Lines};
use self::upkeep::{pass_on_dividends, pay_holders, pay_wages_away, snapshot_dividends, Upkeep};
use super::cargo::{warehouse_holder, CargoCapacity};
use super::development::SubsidyPolicy;
use super::money_ledger::state_excess;
use super::{
    AgentTable, BankState, BillRoom, BillTable, CargoTable, DemandForecast, DevelopmentState,
    FacilityTable, GroupTable, IncomeTaxState, LodState, PurseState,
};
use crate::clock::WEEKS_PER_YEAR;
use crate::context::SimContext;
use crate::economy::{MarketFlows, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeSpecialties, NodeTable, WorldState};
use fes_core::det::det_hash;
use fes_core::WorldLevel;
use fes_data::database::GameDatabase;
use std::sync::Arc;

pub use bankruptcy::{FacilityBankrupt, LostWarehouseCleared, NpcLoanDefaulted};

const SYSTEM_ID: &str = "agents.production";

/// C# `DevelopmentState.RefreshSubsidyRates`: the rates follow the policies in force. Unsaved, so
/// derived from the saved policies whenever production needs them.
pub fn refresh_subsidy_rates(development: &mut DevelopmentState, goods: usize, rate_raw: i64) {
    let DevelopmentState {
        subsidies,
        subsidy_rate,
        ..
    } = development;
    fill_subsidy_rates(subsidy_rate, subsidies, goods, rate_raw);
}

/// The rates `refresh_subsidy_rates` writes, into `rate`.
pub fn fill_subsidy_rates(rate: &mut [i64], subsidies: &[SubsidyPolicy], goods: usize, raw: i64) {
    rate.fill(0);
    for p in subsidies {
        rate[p.nation as usize * goods + p.good as usize] = raw;
    }
}

/// C# `IncomeTaxState.EnsureRows`: companies founded since the last call start their books.
fn ensure_rows(world: &mut WorldState) {
    // Copy only the rows added since the last call.
    let books = world.get::<IncomeTaxState>();
    let (facility_from, merchant_from) = (books.facility_start.len(), books.merchant_start.len());
    let cash = &world.get::<FacilityTable>().cash;
    let facility_tail = cash[facility_from.min(cash.len())..].to_vec();
    let merchants = &world.get::<AgentTable>().cash;
    let merchant_tail = merchants[merchant_from.min(merchants.len())..].to_vec();
    let books = world.get_mut::<IncomeTaxState>();
    books.facility_start.extend_from_slice(&facility_tail);
    books
        .facility_dividends
        .resize(books.facility_dividends.len() + facility_tail.len(), 0);
    books.merchant_start.extend_from_slice(&merchant_tail);
}

/// What each nation with a subsidy may pay this week (DEC-074); also sets `subsidy_left`.
fn open_subsidies(ctx: &mut SimContext) -> Vec<i64> {
    let db = &ctx.services.database;
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    let mut budget = vec![0i64; ctx.world.get::<BankState>().reserve.len()];
    for p in &ctx.world.get::<DevelopmentState>().subsidies {
        budget[p.nation as usize] = 1;
    }
    for (n, b) in budget.iter_mut().enumerate() {
        if *b != 0 {
            let excess = state_excess(&ctx.world, nodes, banks, &db.config, n as i32);
            *b = db.config.subsidy_share.mul_int(excess.max(0)) / i64::from(WEEKS_PER_YEAR);
        }
    }
    let left = &mut ctx.world.get_mut::<DevelopmentState>().subsidy_left;
    left[..budget.len()].copy_from_slice(&budget);
    budget
}

fn job_params(ctx: &SimContext) -> JobParams {
    let c = &ctx.services.database.config;
    JobParams {
        seed: ctx.seed(),
        turn: ctx.turn(),
        stream: det_hash::of(SYSTEM_ID),
        grade_stream: det_hash::of("agents.production.grade"),
        variance_permille: c.production_variance_permille,
        good_count: ctx.world.get::<MarketStore>().good_base_price.len(),
        grade_up_chance_raw: c.specialty_grade_up_chance.raw(),
        expansion_bonus_raw: c.facility_expansion_bonus.raw(),
        unaided_output_raw: c.facility_unaided_output.raw(),
        output_wage_raw: c.facility_output_wage.raw(),
        spread_raw: c.trader_spread.raw(),
        bill_producers: c.bill_producers,
        bill_discount_raw: c.bill_discount.raw(),
        bill_weeks: c.bill_weeks,
    }
}

/// The week's per-facility results the later steps need.
struct Week {
    player_output: Vec<PlayerOutput>,
    input_cost: Vec<i64>,
    output_wage: Vec<i64>,
}

/// Every facility's lines, rebuilt each turn.
fn lines_of(ctx: &SimContext) -> Lines {
    let w = &ctx.world;
    build_lines(
        &ctx.services.database,
        ctx.services.get::<NodeTable>(),
        w.get::<MarketStore>(),
        ctx.services.get::<MarketFlows>(),
        ctx.services.get::<NodeSpecialties>(),
        w.get::<FacilityTable>(),
        w.get::<DevelopmentState>(),
    )
}

/// Runs the production job over the world, moving the written columns out and back.
fn run_production(ctx: &mut SimContext, float_reserve: &[i64]) -> Week {
    let p = job_params(ctx);
    let lines = lines_of(ctx);
    let w = &mut ctx.world;
    let m = w.get_mut::<MarketStore>();
    let (mut stock, mut supply, mut demand, mut bid) = (
        std::mem::take(&mut m.stock),
        std::mem::take(&mut m.supply),
        std::mem::take(&mut m.demand),
        std::mem::take(&mut m.input_bid),
    );
    let mut cash = std::mem::take(&mut w.get_mut::<FacilityTable>().cash);
    let mut float = std::mem::take(&mut w.get_mut::<PurseState>().float);
    let mut left = std::mem::take(&mut w.get_mut::<DevelopmentState>().subsidy_left);
    let mut produced = std::mem::take(&mut w.get_mut::<DemandForecast>().produced);
    let r = w.get_mut::<BillRoom>();
    let (mut room, mut bank_room, mut bank_spent) = (
        std::mem::take(&mut r.room),
        std::mem::take(&mut r.bank_room),
        std::mem::take(&mut r.bank_spent),
    );
    let mut bills = std::mem::take(w.get_mut::<BillTable>());
    let count = cash.len();
    let mut week = Week {
        player_output: Vec::new(),
        input_cost: vec![0; count],
        output_wage: vec![0; count],
    };
    {
        let markets = w.get::<MarketStore>();
        let facilities = w.get::<FacilityTable>();
        let mut job = ProductionJob {
            p,
            lines: &lines,
            cell_plan: &w.get::<DemandForecast>().plan,
            good_base_price: &markets.good_base_price,
            facility_node: &facilities.node,
            facility_owner: &facilities.owner,
            facility_level: &facilities.level,
            facility_efficiency: &facilities.efficiency,
            node_float: &w.get::<PurseState>().node_float,
            float_reserve,
            subsidy_rate: &w.get::<DevelopmentState>().subsidy_rate,
            node_detail: &ctx.services.get::<LodState>().detail,
            modifier_mul: &ctx.services.get::<MarketFlows>().multipliers,
            price: &markets.price,
            cell_produced: &mut produced,
            facility_output_wage: &mut week.output_wage,
            facility_input_cost: &mut week.input_cost,
            facility_cash: &mut cash,
            float: &mut float,
            subsidy_left: &mut left,
            stock: &mut stock,
            supply: &mut supply,
            demand: &mut demand,
            input_bid: &mut bid,
            bill_room: &mut room,
            bill_bank_room: &mut bank_room,
            bill_bank_spent: &mut bank_spent,
            bills: &mut bills,
            player_output: &mut week.player_output,
        };
        job.execute();
    }
    let m = w.get_mut::<MarketStore>();
    (m.stock, m.supply, m.demand, m.input_bid) = (stock, supply, demand, bid);
    w.get_mut::<FacilityTable>().cash = cash;
    w.get_mut::<PurseState>().float = float;
    w.get_mut::<DevelopmentState>().subsidy_left = left;
    w.get_mut::<DemandForecast>().produced = produced;
    let r = w.get_mut::<BillRoom>();
    (r.room, r.bank_room, r.bank_spent) = (room, bank_room, bank_spent);
    *w.get_mut::<BillTable>() = bills;
    week
}

/// Player-owned output, in facility order: into the warehouse, the overflow sold (DEC-005, DEC-029).
fn store_player_output(world: &mut WorldState, db: &GameDatabase, output: &[PlayerOutput]) {
    let capacity = CargoCapacity {
        max_weight: db.config.warehouse_max_weight,
        max_size: db.config.warehouse_max_size,
    };
    for o in output {
        let holder = warehouse_holder(o.facility);
        let markets = world.get::<MarketStore>();
        let (weight, size) = (&markets.good_weight, &markets.good_size);
        let g = o.good as usize;
        let load = world.get::<CargoTable>().load_of(holder, weight, size);
        let stored = o
            .quantity
            .min(capacity.max_additional(load, weight[g], size[g]));
        world.get_mut::<CargoTable>().change(holder, o.good, stored);
        let mut overflow = o.quantity - stored;
        if overflow == 0 || o.cell < 0 {
            continue; // no market for the good: the overflow is lost
        }
        let cell = o.cell as usize;
        let price = world.get::<MarketStore>().price[cell];
        let offer = price - db.config.trader_spread.mul_int(i64::from(price)) as i32;
        let node = world.get::<FacilityTable>().node[o.facility as usize];
        let purses = world.get::<PurseState>();
        let purse = purses.node_float[node as usize] as usize;
        if offer > 0 {
            let affordable = purses.float[purse].max(0) / i64::from(offer);
            overflow = i64::from(overflow).min(affordable) as i32;
        }
        if overflow == 0 {
            continue;
        }
        let supply = &mut world.get_mut::<MarketStore>().supply[cell];
        *supply = MarketStore::clamp_to_non_negative_int(i64::from(*supply) + i64::from(overflow));
        let paid = i64::from(overflow) * i64::from(offer);
        world.get_mut::<FacilityTable>().cash[o.facility as usize] += paid;
        world.get_mut::<PurseState>().float[purse] -= paid;
    }
}

/// Per node, raw Fx: the share of wages that leaves a village for its nation's towns.
fn node_wage_away(ctx: &SimContext) -> Vec<i64> {
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    let share = ctx.services.database.config.village_wage_away_share.raw();
    (0..nodes.count() as i32)
        .map(|n| {
            if nodes.level_of(n) == WorldLevel::Village && banks.nation_of(nodes, n) >= 0 {
                share
            } else {
                0
            }
        })
        .collect()
}

pub struct ProductionSystem;

impl System for ProductionSystem {
    fn id(&self) -> &'static str {
        SYSTEM_ID
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Production
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let db = Arc::clone(&ctx.services.database);
        let goods = ctx.world.get::<MarketStore>().good_base_price.len();
        refresh_subsidy_rates(
            ctx.world.get_mut::<DevelopmentState>(),
            goods,
            db.config.subsidy_rate.raw(),
        );
        // ClearWeek.
        let float_reserve: Vec<i64> = ctx
            .world
            .get::<PurseState>()
            .float
            .iter()
            .map(|&f| f.max(0) - db.config.producer_float_share.mul_int(f.max(0)))
            .collect();
        let mut group_dividend = vec![0i64; ctx.world.get::<GroupTable>().count()];
        ensure_rows(&mut ctx.world);

        let budget = open_subsidies(ctx);
        let week = run_production(ctx, &float_reserve);
        let (bank, room) = ctx.world.get2_mut::<BankState, BillRoom>();
        let spent = &mut room.bank_spent;
        for n in 0..bank.reserve.len().min(spent.len()) {
            bank.reserve[n] -= spent[n];
            spent[n] = 0;
        }
        let (bank, development) = ctx.world.get2_mut::<BankState, DevelopmentState>();
        let left = &development.subsidy_left;
        for n in 0..budget.len() {
            bank.reserve[n] -= budget[n] - left[n];
        }

        store_player_output(&mut ctx.world, &db, &week.player_output);
        let before = snapshot_dividends(&ctx.world);
        let away_share = node_wage_away(ctx);
        let nodes = ctx.services.get::<NodeTable>();
        let banks = ctx.services.get::<NodeBanks>();
        let mut wages_away = vec![0i64; nodes.count()];
        let mut bankrupt = Vec::new();
        Upkeep {
            db: &db,
            node_wage_away: &away_share,
            input_cost: &week.input_cost,
            output_wage: &week.output_wage,
            node_wages_away: &mut wages_away,
            group_dividend: &mut group_dividend,
            bankrupt: &mut bankrupt,
        }
        .run(&mut ctx.world);
        pay_holders(&mut ctx.world, nodes, banks, &db, &before);
        pay_wages_away(&mut ctx.world, nodes, &db, &mut wages_away);
        pass_on_dividends(&mut ctx.world, nodes, banks, &db, &group_dividend);

        for f in bankrupt {
            go_bankrupt(ctx, f);
        }
        expire_warehouse_claims(ctx);
        Ok(())
    }
}
