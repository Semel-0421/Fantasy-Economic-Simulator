//! `agents.merchants` (C# `MerchantSystem` with `ArbitrageMerchantBehavior`): the detailed job,
//! the step after it (coins, bill room, price cover, tolls, bankruptcies, trip statistics), then
//! the aggregate flow.
use super::aggregate::{run_flow, FlowInputs, Pool};
use super::bankrupt::{go_bankrupt, MERCHANT_HOLDER_BASE};
use super::job::{DetailedJob, JobGroups, JobPlaces, JobRules, JobState, Scratch};
use super::needs::refresh_needs;
use super::statics::Statics;
use super::traits::MerchantTraitTable;
use super::AgentTable;
use crate::agents::groups::insurance_guild;
use crate::agents::money_ledger::to_state;
use crate::agents::travel_costs::TravelCosts;
use crate::agents::{
    BankState, BillRoom, CreditStats, DevelopmentState, GroupTable, LodState, MerchantTripStats,
    PolicyState, PurseState,
};
use crate::context::SimContext;
use crate::economy::pricing::demand::CurveMemo;
use crate::economy::{CurrencyState, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, NodeTolls, RoadState, RouteHazards, RouteTable};
use fes_core::det::det_hash;

/// `agents.merchants`. Its fields are scratch and caches; none carries state across turns that
/// the saved sections do not determine.
#[derive(Default)]
pub struct MerchantSystem {
    /// C# `IdleMerchantBehavior` instead of `ArbitrageMerchantBehavior`.
    idle: bool,
    statics: Option<Statics>,
    scratch: Scratch,
    pool: Pool,
    caches: Caches,
    /// The household necessity curve per cell, kept across turns (`refresh_needs`).
    curve: CurveMemo,
}

/// Per-turn derived inputs, and the insurer cache C# keys on the group count.
#[derive(Default)]
struct Caches {
    route_cost: Vec<i64>,
    route_danger: Vec<i64>,
    route_expected_loss: Vec<i64>,
    coin_rate: Vec<i64>,
    continent_index: Vec<i64>,
    node_insurer: Vec<i32>,
    insurers_for_groups: Option<usize>,
}

/// C# `PolicySystem.ApplyTolls`, from the saved toll steps: `NodeTolls` is unsaved and a pure
/// function of `agents.policy` (plan4-audit, unsaved-input register).
pub fn refresh_tolls(ctx: &mut SimContext) {
    let step = ctx.services.database.config.policy_step;
    let mut tolls = std::mem::take(ctx.services.get_mut::<NodeTolls>());
    let steps = &ctx.world.get::<PolicyState>().toll_step;
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    tolls.apply_steps(nodes, banks, steps, step);
    *ctx.services.get_mut::<NodeTolls>() = tolls;
}

impl System for MerchantSystem {
    fn id(&self) -> &'static str {
        "agents.merchants"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Agents
    }

    fn order(&self) -> i32 {
        sim_order::DEFAULT
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if self.idle {
            // C# keeps tolls current in `PolicySystem`, whatever the merchant behavior.
            refresh_tolls(ctx);
            return Ok(());
        }
        let traits = self.run_detailed(ctx);
        let statics = self.statics.as_ref().expect("statics");
        self.pool.pool_idle_groups(ctx, &traits, statics);
        let inputs = FlowInputs {
            statics,
            coin_rate: &self.caches.coin_rate,
            route_cost: &self.caches.route_cost,
            route_expected_loss: &self.caches.route_expected_loss,
        };
        run_flow(ctx, &mut self.pool, &inputs);
        Ok(())
    }

    /// C# `PolicySystem.OnLoaded` -> `ApplyTolls`; Rust derives the tolls here until policy is ported.
    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        refresh_tolls(ctx);
        Ok(())
    }
}

impl MerchantSystem {
    /// C# `IdleMerchantBehavior` (tests, or a world without trade): merchant groups exist but
    /// never trade, travel or go bankrupt. Use with `SimulationBuilder::replace_system`.
    pub fn idle() -> Self {
        Self {
            idle: true,
            ..Self::default()
        }
    }

    /// C# `RunDetailed` (after refreshing the tolls), without the aggregate flow. Returns the
    /// trait table it used.
    pub fn run_detailed(&mut self, ctx: &mut SimContext) -> MerchantTraitTable {
        refresh_tolls(ctx);
        if self.statics.is_none() {
            self.statics = Some(Statics::new(ctx));
        }
        let traits = MerchantTraitTable::new(&ctx.services.database.config);
        self.run_job(ctx, &traits);
        traits
    }

    fn run_job(&mut self, ctx: &mut SimContext, traits: &MerchantTraitTable) {
        self.caches.prepare_routes(ctx);
        self.caches.prepare_coins(ctx);
        self.prepare_scratch(ctx);
        refresh_needs(ctx, &mut self.scratch.household_need, &mut self.curve);
        self.caches.refresh_insurers(ctx);
        let mut state = JobState::take_from(&mut ctx.world);
        let rules = job_rules(ctx);
        {
            let statics = self.statics.as_ref().expect("statics");
            let places = places(ctx, statics, &self.caches);
            let groups = groups_of(ctx, statics, traits);
            DetailedJob {
                r: &rules,
                p: &places,
                g: &groups,
                s: &mut state,
                x: &mut self.scratch,
            }
            .execute();
        }
        state.restore(&mut ctx.world);
        self.settle_detailed(ctx);
        for a in std::mem::take(&mut self.scratch.bankrupt) {
            go_bankrupt(ctx, a as usize);
        }
        self.record_trip_stats(ctx);
    }
}

impl Caches {
    /// Roads change week to week (paving, decay).
    fn prepare_routes(&mut self, ctx: &SimContext) {
        let config = &ctx.services.database.config;
        let costs = TravelCosts {
            config,
            routes: ctx.services.get::<RouteTable>(),
            hazards: ctx.services.get::<RouteHazards>(),
            roads: ctx.world.get::<RoadState>(),
            development: ctx.world.get::<DevelopmentState>(),
            purses: ctx.world.get::<PurseState>(),
        };
        let count = costs.routes.count();
        self.route_cost.resize(count, 0);
        self.route_danger.resize(count, 0);
        self.route_expected_loss.resize(count, 0);
        for r in 0..count {
            let danger = costs.route_danger(r);
            self.route_cost[r] = costs.per_unit_charge(r).raw();
            self.route_danger[r] = danger.raw();
            // Far regions apply the expected loss: half the danger hits cargo (DEC-026).
            self.route_expected_loss[r] = (danger * config.encounter_cargo_loss).raw() / 2;
        }
    }

    /// Coins of each continent in each other's (the international reserves are moved into the
    /// job with the other written columns).
    fn prepare_coins(&mut self, ctx: &SimContext) {
        let currency = ctx.world.get::<CurrencyState>();
        let rates = &currency.exchange_rate;
        let n = rates.len();
        self.coin_rate.clear();
        for from in 0..n {
            for to in 0..n {
                self.coin_rate.push((rates[from] / rates[to]).raw());
            }
        }
        self.continent_index.clear();
        self.continent_index
            .extend(currency.price_index.iter().map(|p| p.raw()));
    }
}

impl MerchantSystem {
    fn prepare_scratch(&mut self, ctx: &SimContext) {
        let db = &ctx.services.database;
        let nodes = ctx.services.get::<NodeTable>().count();
        let continents = ctx.services.get::<NodeTable>().continent_count() as usize;
        let goods = db.good_count();
        let markets = ctx.world.get::<MarketStore>().market_count();
        let x = &mut self.scratch;
        x.first_row
            .resize(ctx.world.get::<AgentTable>().count(), -1);
        x.board.resize(nodes, super::NodeMemory::EMPTY);
        x.stocked_start.resize(nodes, -1);
        x.stocked_count.resize(nodes, 0);
        x.bid_best.resize(continents * goods, 0);
        x.bid_linked.resize(continents * continents, false);
        x.bid_bound.resize(continents * continents * goods, 0);
        x.cell_bits.resize(markets * goods.div_ceil(64), 0);
        x.cells_before.resize(markets * goods.div_ceil(64), 0);
        x.tolls_by_node.resize(nodes, 0);
        x.bankrupt.clear();
        let bank = ctx.world.get::<BankState>();
        let purses = ctx.world.get::<PurseState>();
        let c = &db.config;
        x.hedge_room.clear();
        x.hedge_room.extend((0..purses.count()).map(|p| {
            if c.hedge_fee.raw() > 0 && p < purses.nation_count {
                c.hedge_bank_share.mul_int(bank.spendable(p))
            } else {
                0
            }
        }));
        x.hedge_net.resize(purses.count(), 0);
    }
}

impl Caches {
    /// C# caches the insuring guild per node, keyed on the group count (groups are only added).
    fn refresh_insurers(&mut self, ctx: &SimContext) {
        let groups = ctx.world.get::<GroupTable>();
        if self.insurers_for_groups == Some(groups.count()) {
            return;
        }
        let nodes = ctx.services.get::<NodeTable>();
        self.node_insurer = (0..nodes.count() as i32)
            .map(|n| insurance_guild(groups, nodes, n))
            .collect();
        self.insurers_for_groups = Some(groups.count());
    }
}

fn places<'a>(ctx: &'a SimContext, statics: &'a Statics, caches: &'a Caches) -> JobPlaces<'a> {
    let nodes = ctx.services.get::<NodeTable>();
    let routes = ctx.services.get::<RouteTable>();
    let tolls = ctx.services.get::<NodeTolls>();
    let markets = ctx.world.get::<MarketStore>();
    JobPlaces {
        detail: &ctx.services.get::<LodState>().detail,
        continent: &nodes.continent,
        parent: &nodes.parent,
        level: &nodes.level,
        core_strength: &nodes.core_strength,
        category_specialty: &statics.category_specialty,
        tariff: &tolls.tariff,
        surcharge: &tolls.specialty_surcharge,
        license_row: &statics.license_row,
        licensed: &ctx.world.get::<PolicyState>().licensed,
        node_float: &ctx.world.get::<PurseState>().node_float,
        continent_index: &caches.continent_index,
        node_port: &statics.node_port,
        route_toward: &statics.route_toward,
        node_insurer: &caches.node_insurer,
        coin_rate: &caches.coin_rate,
        out_start: &routes.out_start,
        out_count: &routes.out_count,
        route_index: &routes.route_index,
        route_to: &routes.to,
        route_turns: &routes.travel_turns,
        route_base_fee: &routes.base_fee,
        route_cost_raw: &caches.route_cost,
        route_danger_raw: &caches.route_danger,
        cell_start: &markets.market_cell_start,
        cell_count: &markets.market_cell_count,
        cell_good: &markets.cell_good,
        good_base_price: &markets.good_base_price,
        good_weight: &markets.good_weight,
        good_size: &markets.good_size,
        good_category: &markets.good_category,
        price: &markets.price,
    }
}

impl MerchantSystem {
    fn settle_detailed(&mut self, ctx: &mut SimContext) {
        let (room, bank) = ctx.world.get2_mut::<BillRoom, BankState>();
        room.settle_bank(&mut bank.reserve);
        for (p, net) in self.scratch.hedge_net.iter_mut().enumerate() {
            if *net != 0 {
                bank.reserve[p] += *net;
            }
            *net = 0;
        }
        let credit = ctx.services.get_mut::<CreditStats>();
        credit.bills_issued += self.scratch.bill_stats[0];
        credit.bills_discounted += self.scratch.bill_stats[1];
        self.scratch.bill_stats = [0; 2];
        let nodes = ctx.services.get::<NodeTable>();
        let banks = ctx.services.get::<NodeBanks>();
        for (n, toll) in self.scratch.tolls_by_node.iter_mut().enumerate() {
            if *toll != 0 {
                to_state(&mut ctx.world, nodes, banks, n as i32, *toll); // one coin (DEC-015)
                *toll = 0;
            }
        }
    }

    fn record_trip_stats(&mut self, ctx: &mut SimContext) {
        let t = std::mem::take(&mut self.scratch.trip_stats);
        let s = ctx.services.get_mut::<MerchantTripStats>();
        let fields = [
            &mut s.trips,
            &mut s.guarded,
            &mut s.insured,
            &mut s.encounters,
            &mut s.loss_sales,
            &mut s.loss_amount,
            &mut s.carried_on,
            &mut s.forced_sales,
            &mut s.sold,
            &mut s.spent_goods,
            &mut s.spent_fees,
            &mut s.spent_tolls,
            &mut s.spent_guards,
            &mut s.spent_cover,
            &mut s.spent_upkeep,
            &mut s.spent_stolen,
            &mut s.spent_discount,
            &mut s.cover_refunds,
            &mut s.spent_hedge,
            &mut s.hedge_paid,
            &mut s.forced_trips,
        ];
        for (field, value) in fields.into_iter().zip(t) {
            *field += value;
        }
    }
}

fn job_rules(ctx: &SimContext) -> JobRules {
    let c = &ctx.services.database.config;
    let nodes = ctx.services.get::<NodeTable>();
    JobRules {
        seed: ctx.seed(),
        turn: ctx.turn(),
        encounter_stream: det_hash::of("agents.encounters"),
        license_stream: det_hash::of("agents.licenses.merchants"),
        licensed_permille: c.licensed_merchant_permille,
        international_stream: det_hash::of("agents.licenses.international"),
        international_permille: c.international_merchant_permille,
        intercontinental_stream: det_hash::of("agents.licenses.intercontinental"),
        intercontinental_permille: c.intercontinental_merchant_permille,
        base_good_count: ctx.world.get::<PolicyState>().base_good_count as i32,
        category_count: ctx
            .services
            .get::<crate::economy::MarketFlows>()
            .category_count as i32,
        bankrupt_weeks: c.merchant_bankrupt_weeks,
        reserve_upkeep_weeks: c.merchant_reserve_upkeep_weeks,
        merchant_holder_base: MERCHANT_HOLDER_BASE,
        cash_loss_raw: c.encounter_cash_loss.raw(),
        cargo_loss_raw: c.encounter_cargo_loss.raw(),
        danger_value_scale: c.danger_value_scale,
        rumors: c.merchant_rumors_enabled,
        weigh_sales: c.merchant_weigh_sales,
        judge_hops: c.merchant_judge_hops,
        bids: c.merchant_bids,
        bids_while_loaded: c.merchant_bids_while_loaded,
        return_home: c.merchant_return_home,
        cross_level: c.merchant_cross_level,
        continent_count: nodes.continent_count(),
        exchange_fee_raw: c.exchange_fee.raw(),
        spread_raw: c.trader_spread.raw(),
        judge_coin_units: c.merchant_judge_coin_units,
        bill_discount_raw: c.bill_discount.raw(),
        bill_weeks: c.bill_weeks,
        guard_base_cost: c.guard_base_cost,
        member_premium_raw: c.merchant_member_premium.raw(),
        count_trip_costs: c.merchant_count_trip_costs,
        must_act: c.merchant_must_act,
        hedge_floor_raw: c.hedge_floor.raw(),
        hedge_fee_raw: c.hedge_fee.raw(),
        hedge_fall_raw: c.hedge_fall.raw(),
        hedge_chance_raw: c.hedge_fall_chance.raw(),
        guard_cost_share_raw: c.guard_cost_share.raw(),
        guard_reduction_raw: c.guard_reduction.raw(),
        insurance_premium_raw: c.insurance_premium.raw(),
        insurance_refund_raw: c.insurance_refund.raw(),
    }
}

fn groups_of<'a>(
    ctx: &'a SimContext,
    statics: &'a Statics,
    traits: &'a MerchantTraitTable,
) -> JobGroups<'a> {
    let agents = ctx.world.get::<AgentTable>();
    JobGroups {
        home: &agents.home,
        owner: &agents.owner,
        association: &agents.association,
        mode: &agents.mode,
        scale: &agents.scale,
        trait_id: &agents.trait_id,
        vehicle: &agents.vehicle,
        traits,
        levels: &statics.levels,
        vehicle_capacity: &statics.vehicle_capacity,
        vehicle_cost: &statics.vehicle_cost,
        vehicle_danger: &statics.vehicle_danger,
        vehicle_premium: &statics.vehicle_premium,
    }
}
