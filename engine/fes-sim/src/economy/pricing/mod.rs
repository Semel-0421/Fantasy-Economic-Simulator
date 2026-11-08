//! `economy.pricing` (C# `PricingSystem`, `Economy/MarketSystems.cs`, `Economy/PriceKernel.cs`,
//! `Economy/Households.cs`): households settle and consume every turn, and every market whose
//! level is due reprices, consuming the supply and demand accumulated since its previous update.
//! The C# Burst jobs run here as plain loops over markets in index order.
pub mod consumption;
pub mod demand;
pub mod reprice;

use self::consumption::{Books, Scratch};
use self::demand::{CurveMemo, DemandParams};
use self::reprice::{PriceParams, Reprice};
use super::{CurrencyState, HouseholdCash, HouseholdState, MarketFlows, MarketStore};
use crate::context::SimContext;
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::NodeTable;
use crate::Simulation;
use fes_core::{Fx, GoodId, NodeId, QualityGrade, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::GoodDef;
use fes_data::SimulationConfig;

/// C# `SimulationConfig.LuxuryShare`: how far a good is bought like a luxury.
pub fn luxury_share(config: &SimulationConfig, good: &GoodDef) -> Fx {
    if good.traits.is_luxury {
        return Fx::ONE;
    }
    let shares = if good.traits.is_semi {
        &config.semi_grade_luxury_share
    } else {
        &config.grade_luxury_share
    };
    shares[good.grade() as usize]
}

#[derive(Default)]
pub struct PricingSystem {
    /// The household necessity curve by price ratio, kept across turns.
    curve: CurveMemo,
}

impl System for PricingSystem {
    fn id(&self) -> &'static str {
        "economy.pricing"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Markets
    }

    fn order(&self) -> i32 {
        sim_order::LATE
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let mut markets = std::mem::replace(
            ctx.world.get_mut::<MarketStore>(),
            MarketStore::new(0, 0, 0),
        );
        let mut cash = std::mem::replace(
            ctx.world.get_mut::<HouseholdCash>(),
            HouseholdCash::new(0, 0),
        );
        let households = ctx.world.get_mut::<HouseholdState>();
        let mut wanted = std::mem::take(&mut households.necessity_wanted);
        let mut met = std::mem::take(&mut households.necessity_met);

        run(
            ctx,
            &mut markets,
            &mut cash,
            &mut wanted,
            &mut met,
            &mut self.curve,
        );

        let households = ctx.world.get_mut::<HouseholdState>();
        households.necessity_wanted = wanted;
        households.necessity_met = met;
        *ctx.world.get_mut::<MarketStore>() = markets;
        *ctx.world.get_mut::<HouseholdCash>() = cash;
        Ok(())
    }
}

fn run(
    ctx: &SimContext,
    markets: &mut MarketStore,
    cash: &mut HouseholdCash,
    wanted: &mut [i64],
    met: &mut [i64],
    curve: &mut CurveMemo,
) {
    let db = &ctx.services.database;
    let nodes = ctx.services.get::<NodeTable>();
    let households = ctx.world.get::<HouseholdState>();
    grow_at_home(db, nodes, markets, cash, households);
    let scratch = scratch(ctx, cash);
    curve.prepare(&scratch.demand);

    let mut inline_mask = 0i32;
    for (l, level) in WorldLevel::ALL.iter().enumerate() {
        if ctx.schedule.cadence(*level).period == 1 {
            inline_mask |= 1 << l;
        }
    }
    let slow_mask = ((1 << WorldLevel::COUNT) - 1) & !inline_mask;
    let mut books = Books {
        markets,
        cash,
        necessity_wanted: wanted,
        necessity_met: met,
        curve,
    };
    if slow_mask != 0 {
        scratch.consume(slow_mask, &mut books);
    }

    let mut due_mask = 0i32;
    let mut level_elapsed = vec![0i32; WorldLevel::COUNT];
    for (l, level) in WorldLevel::ALL.iter().enumerate() {
        if !ctx.is_due(*level) {
            continue;
        }
        due_mask |= 1 << l;
        level_elapsed[l] = ctx.schedule.elapsed_turns(*level, ctx.turn());
    }
    if due_mask == 0 {
        return;
    }
    let reprice = Reprice {
        params: PriceParams::from_config(&db.config),
        due_level_mask: due_mask,
        inline_level_mask: inline_mask,
        level_elapsed,
    };
    reprice.run(&scratch, &mut books);
}

/// Settles every node's households and derives the per-node weights and budgets of this turn.
fn scratch<'a>(ctx: &'a SimContext, cash: &mut HouseholdCash) -> Scratch<'a> {
    let db = &ctx.services.database;
    let config = &db.config;
    let nodes = ctx.services.get::<NodeTable>();
    let flows = ctx.services.get::<MarketFlows>();
    let currency = ctx.world.get::<CurrencyState>();
    let households = ctx.world.get::<HouseholdState>();
    let count = nodes.count();
    let mut s = Scratch {
        demand: DemandParams::from_config(config),
        category_count: flows.category_count,
        specialty_anchor_mul_raw: config.specialty_price_multiplier.raw(),
        turn: ctx.turn(),
        need_memory_weeks: config.need_memory_weeks,
        level: &nodes.level,
        anchor_mul: vec![0; count],
        necessity_weight: vec![0; count],
        luxury_weight: vec![0; count],
        budget: vec![0; count],
        luxury_budget: vec![0; count],
        good_luxury_share: db
            .goods
            .iter()
            .map(|g| luxury_share(config, g).raw())
            .collect(),
        level_weight: config.level_demand_weight.iter().map(|w| w.raw()).collect(),
        modifier_mul: &flows.multipliers,
    };
    for n in 0..count {
        let level = usize::from(nodes.level[n]);
        s.anchor_mul[n] = currency.price_index[nodes.continent[n] as usize].raw();
        s.necessity_weight[n] = households.necessity_weight(n, level, config).raw();
        cash.settle(n, &config.quintile_income_share);
        let weekly_income =
            Fx::mul_int_raw(households.population[n], households.income_per_head[n]);
        let wealth = cash.luxury_wealth_mul(
            n,
            weekly_income,
            config.luxury_wealth_per_year.raw(),
            config.luxury_wealth_cap.raw(),
        );
        s.luxury_weight[n] = Fx::round_shift(
            households
                .luxury_weight(n, level, config)
                .raw()
                .wrapping_mul(wealth),
        );
        s.budget[n] = cash.necessity_budget_factor(
            n,
            config.household_budget_weeks,
            config.household_alms_share.raw(),
        );
        s.luxury_budget[n] = cash.luxury_budget_factor(n, config.household_budget_weeks);
    }
    s
}

/// In proportion to the people of the villages, into their market's lowest grade of each good made
/// at home (DEC-117, DEC-118).
fn grow_at_home(
    db: &GameDatabase,
    nodes: &NodeTable,
    markets: &MarketStore,
    cash: &mut HouseholdCash,
    households: &HouseholdState,
) {
    let home_goods: Vec<&GoodDef> = db
        .goods
        .iter()
        .filter(|g| g.traits.home_output > 0 && g.grade() == QualityGrade::Low)
        .collect();
    for n in 0..nodes.count() {
        if nodes.level_of(n as i32) != WorldLevel::Village {
            continue;
        }
        let market = if nodes.absorbed() {
            nodes.core[n] as usize
        } else {
            n
        };
        for good in &home_goods {
            let Some(cell) = lowest_cell(markets, market, good.id.base_index()) else {
                continue;
            };
            let grown = i64::from(cash.home_grown[cell])
                + households.population[n] * i64::from(good.traits.home_output) / 1000;
            cash.home_grown[cell] = MarketStore::clamp_to_non_negative_int(grown);
        }
    }
}

fn lowest_cell(markets: &MarketStore, market: usize, base_good: i32) -> Option<usize> {
    QualityGrade::ALL
        .iter()
        .find_map(|g| markets.try_find_cell(market, GoodId::from(base_good, *g).0))
}

/// One good's quote in a market view.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GoodQuote {
    pub good: GoodId,
    pub price: Fx,
    pub stock: i64,
}

/// A plain copy of one market's prices and stock (spec 7.2), in the market's cell order.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MarketView {
    pub quotes: Vec<GoodQuote>,
}

/// Copies the market of `node` out of the simulation.
pub fn market_view(sim: &Simulation, node: NodeId) -> MarketView {
    let markets = sim.world().get::<MarketStore>();
    let market = node.0 as usize;
    let start = markets.market_cell_start[market] as usize;
    let end = start + markets.market_cell_count[market] as usize;
    MarketView {
        quotes: (start..end)
            .map(|c| GoodQuote {
                good: GoodId(markets.cell_good[c]),
                price: Fx::from_int(i64::from(markets.price[c])),
                stock: i64::from(markets.stock[c]),
            })
            .collect(),
    }
}
