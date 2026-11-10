//! `agents.demand_forecast` (C# `DemandForecastSystem`, DEC-085, DEC-091): quarterly, with
//! `production.forecast_enabled`, the share of capacity makers selling at each market plan to run
//! at. Households' wants are carried up the chain through the inputs and aids of the makers they
//! set running; makers serve their own market first, then what the nation's other markets lack.
use super::facilities::{FacilityTable, OWNER_PLAYER};
use super::investment::output_scale;
use super::order_system::QUARTER_WEEKS;
use super::orders::{DemandForecast, OrderTable};
use super::production::lines::find_nearest_market;
use super::purses::PurseState;
use crate::context::SimContext;
use crate::economy::pricing::demand::{cell_base, weekly, DemandParams, WeeklyInput};
use crate::economy::pricing::luxury_share;
use crate::economy::{CurrencyState, HouseholdState, MarketStore};
use crate::error::SimError;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeTable, WorldState};
use fes_core::{Fx, IndustryTier};
use fes_data::database::GameDatabase;
use fes_data::defs::{GoodDef, RecipeLine};
use fes_data::SimulationConfig;

/// C# `FinanceOrder.DemandForecast`.
pub const ORDER: i32 = sim_order::LATE + 24;
/// Enough for the longest chain, tools to bars to ore.
const CHAIN_PASSES: usize = 6;

/// C# `HouseholdDemand.AtAnchor`: units a week a cell's households take at the anchor price.
pub fn at_anchor(
    p: &DemandParams,
    config: &SimulationConfig,
    level: usize,
    good: &GoodDef,
    price_index: i64,
    necessity_weight: i64,
    luxury_weight: i64,
) -> i64 {
    if good.base_demand <= 0 {
        return 0;
    }
    let (base, anchor) = cell_base(
        good.base_demand,
        good.base_price,
        config.level_demand_weight[level].raw(),
        Fx::ONE_RAW,
        price_index,
    );
    weekly(
        p,
        WeeklyInput {
            weekly_at_anchor: base,
            price: anchor.min(i64::from(i32::MAX)) as i32,
            anchor,
            luxury_share: luxury_share(config, good).raw(),
            necessity_weight,
            luxury_weight,
        },
    )
    .units
}

/// One kind of maker selling at one market, keyed by the cell of its first output.
#[derive(Clone, Copy)]
struct Makers {
    cell: usize,
    recipe: usize,
    /// Raw Fx: recipe runs a quarter at full capacity.
    runs: i64,
    /// Where its output cells start in the line list, then its inputs and aids.
    first_line: usize,
    plan: i64,
}

/// `agents.demand_forecast`. Every field is scratch, rebuilt at each run.
#[derive(Default)]
pub struct DemandForecastSystem {
    makers: Vec<Makers>,
    lines: Vec<i32>,
    fed_cells: Vec<usize>,
    cell_purse: Vec<usize>,
    makers_at: Vec<i32>,
    is_fed: Vec<bool>,
    households: Vec<i64>,
    ordered: Vec<i64>,
    industry: Vec<i64>,
    next_industry: Vec<i64>,
    capacity: Vec<i64>,
    need: Vec<i64>,
    unmet: Vec<i64>,
    spare: Vec<i64>,
    surplus: Vec<i64>,
}

impl System for DemandForecastSystem {
    fn id(&self) -> &'static str {
        "agents.demand_forecast"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        let turn = ctx.turn();
        let db = &ctx.services.database;
        if !db.config.forecast_enabled || turn == 0 || turn % QUARTER_WEEKS != 0 {
            return Ok(());
        }
        let nodes = ctx.services.get::<NodeTable>();
        let world = &mut ctx.world;
        self.size(world, db, nodes);
        self.measure_markets(world, db, nodes);
        self.list_makers(world, db, nodes);
        self.industry.fill(0);
        let markets = world.get::<MarketStore>();
        let run = Run {
            db,
            good_count: db.good_count(),
            cell_good: &markets.cell_good,
            stock: &markets.stock,
            forecast: &world.get::<DemandForecast>().forecast,
        };
        self.measure_needs(&run);
        for pass in 0..CHAIN_PASSES {
            if pass > 0 {
                self.measure_fed_cells(&run);
            }
            self.plan_makers(&run);
        }
        let state = world.get_mut::<DemandForecast>();
        for m in &self.makers {
            state.plan[m.cell] = m.plan;
        }
        Ok(())
    }
}

/// What the passes read, looked up once per run rather than per cell.
struct Run<'a> {
    db: &'a GameDatabase,
    good_count: usize,
    cell_good: &'a [i32],
    stock: &'a [i32],
    forecast: &'a [i64],
}

impl Run<'_> {
    fn good_of(&self, c: usize) -> usize {
        self.cell_good[c] as usize
    }
}

impl DemandForecastSystem {
    fn size(&mut self, world: &WorldState, db: &GameDatabase, nodes: &NodeTable) {
        let markets = world.get::<MarketStore>();
        let purses = world.get::<PurseState>();
        let cells = markets.cell_count();
        let per_good = purses.count() * db.good_count();
        if self.cell_purse.len() == cells && self.unmet.len() == per_good {
            return;
        }
        self.cell_purse = vec![0; cells];
        for n in 0..nodes.count() {
            let start = markets.market_cell_start[n] as usize;
            let end = start + markets.market_cell_count[n] as usize;
            for c in start..end {
                self.cell_purse[c] = purses.node_float[n] as usize;
            }
        }
        self.makers_at = vec![-1; cells];
        self.is_fed = vec![false; cells];
        self.fed_cells.clear();
        for v in [
            &mut self.households,
            &mut self.ordered,
            &mut self.industry,
            &mut self.next_industry,
            &mut self.capacity,
            &mut self.need,
        ] {
            *v = vec![0; cells];
        }
        for v in [&mut self.unmet, &mut self.spare, &mut self.surplus] {
            *v = vec![0; per_good];
        }
    }

    /// C# `MeasureMarkets`.
    fn measure_markets(&mut self, world: &mut WorldState, db: &GameDatabase, nodes: &NodeTable) {
        let config = &db.config;
        let demand = DemandParams::from_config(config);
        self.ordered.fill(0);
        let markets = world.get::<MarketStore>();
        let population = world.get::<HouseholdState>();
        let currency = world.get::<CurrencyState>();
        for n in 0..nodes.count() {
            let level = usize::from(nodes.level[n]);
            let start = markets.market_cell_start[n] as usize;
            let end = start + markets.market_cell_count[n] as usize;
            let index = currency.price_index[nodes.continent[n] as usize].raw();
            let necessity = population.necessity_weight(n, level, config).raw();
            let luxury = population.luxury_weight(n, level, config).raw();
            for c in start..end {
                let good = &db.goods[markets.cell_good[c] as usize];
                let at = at_anchor(&demand, config, level, good, index, necessity, luxury);
                self.households[c] = at * i64::from(QUARTER_WEEKS);
            }
        }
        // `need` is scratch here (measure_needs rewrites every cell): the stock, so the forecast
        // state can be written without holding the market store.
        for (need, &stock) in self.need.iter_mut().zip(&markets.stock) {
            *need = i64::from(stock);
        }
        let state = world.get_mut::<DemandForecast>();
        for (c, &stock) in self.need.iter().enumerate() {
            let left = (state.stock_at_start[c] + state.produced[c] - stock).max(0);
            state.stock_at_start[c] = stock;
            state.produced[c] = 0;
            state.forecast[c] = (state.forecast[c] + left) / 2;
        }
        let orders = world.get::<OrderTable>();
        for r in 0..orders.count() {
            if orders.ready_turn[r] < 0 {
                self.ordered[orders.cell[r] as usize] += i64::from(orders.quantity[r]);
            }
        }
    }

    /// C# `ListMakers`.
    fn list_makers(&mut self, world: &WorldState, db: &GameDatabase, nodes: &NodeTable) {
        self.makers.clear();
        self.lines.clear();
        for &cell in &self.fed_cells {
            self.is_fed[cell] = false;
        }
        self.fed_cells.clear();
        self.makers_at.fill(-1);
        self.capacity.fill(0);
        let facilities = world.get::<FacilityTable>();
        let markets = world.get::<MarketStore>();
        for f in 0..facilities.count() {
            self.list_maker(facilities, markets, db, nodes, f);
        }
        for m in &self.makers {
            let outputs = &db.recipes[m.recipe].outputs;
            let cells = &self.lines[m.first_line..m.first_line + outputs.len()];
            for (output, &cell) in outputs.iter().zip(cells).filter(|(_, &c)| c >= 0) {
                self.capacity[cell as usize] += Fx::mul_int_raw(i64::from(output.quantity), m.runs);
            }
        }
    }

    /// C# `ListMaker`.
    fn list_maker(
        &mut self,
        facilities: &FacilityTable,
        markets: &MarketStore,
        db: &GameDatabase,
        nodes: &NodeTable,
        f: usize,
    ) {
        let config = &db.config;
        let def = &db.facilities[facilities.def_index[f] as usize];
        let recipe = &db.recipes[def.recipe];
        if facilities.owner[f] == OWNER_PLAYER || recipe.outputs.is_empty() {
            return;
        }
        if def.tier == IndustryTier::Primary && !config.forecast_primary {
            return;
        }
        let node = facilities.node[f];
        let Some((_, cell)) = find_nearest_market(nodes, markets, node, recipe.outputs[0].good.0)
        else {
            return;
        };
        let mut at = self.makers_at[cell];
        if at < 0 {
            at = self.makers.len() as i32;
            self.makers_at[cell] = at;
            self.makers.push(Makers {
                cell,
                recipe: def.recipe,
                runs: 0,
                first_line: self.lines.len(),
                plan: 0,
            });
            self.add_lines(nodes, markets, node, &recipe.outputs);
            self.add_fed_lines(nodes, markets, node, &recipe.inputs);
            self.add_fed_lines(nodes, markets, node, &recipe.aids);
        }
        self.makers[at as usize].runs +=
            output_scale(facilities.level[f], config).raw() * i64::from(QUARTER_WEEKS);
    }

    fn add_lines(
        &mut self,
        nodes: &NodeTable,
        markets: &MarketStore,
        node: i32,
        lines: &[RecipeLine],
    ) {
        for line in lines {
            let cell = find_nearest_market(nodes, markets, node, line.good.0)
                .map_or(-1, |(_, c)| c as i32);
            self.lines.push(cell);
        }
    }

    fn add_fed_lines(
        &mut self,
        nodes: &NodeTable,
        markets: &MarketStore,
        node: i32,
        lines: &[RecipeLine],
    ) {
        let first = self.lines.len();
        self.add_lines(nodes, markets, node, lines);
        for k in first..self.lines.len() {
            let cell = self.lines[k];
            if cell < 0 || self.is_fed[cell as usize] {
                continue;
            }
            self.is_fed[cell as usize] = true;
            self.fed_cells.push(cell as usize);
        }
    }

    /// C# `PlanMakers`: one step up the chain.
    fn plan_makers(&mut self, run: &Run) {
        for &cell in &self.fed_cells {
            self.next_industry[cell] = 0;
        }
        let config = &run.db.config;
        for m in 0..self.makers.len() {
            let makers = self.makers[m];
            let recipe = &run.db.recipes[makers.recipe];
            let first = makers.first_line;
            let mut line = first + recipe.outputs.len();
            let (mut target, mut capacity) = (0i64, 0i64);
            for (i, output) in recipe.outputs.iter().enumerate() {
                let cell = self.lines[first + i];
                let share = Fx::mul_int_raw(i64::from(output.quantity), makers.runs);
                capacity += share;
                target += self.target_part(run, cell, share);
            }
            let plan = Fx::clamp(
                Fx::from_ratio(target, capacity.max(1)),
                config.forecast_min_plan,
                Fx::ONE,
            );
            self.makers[m].plan = plan.raw();
            let planned = (plan * Fx::from_raw(makers.runs)).raw();
            line = self.add_industry(&recipe.inputs, line, planned);
            self.add_industry(&recipe.aids, line, planned);
        }
        std::mem::swap(&mut self.industry, &mut self.next_industry);
    }

    /// What an output line's share of its cell's capacity should make.
    fn target_part(&self, run: &Run, cell: i32, share: i64) -> i64 {
        if cell < 0 {
            return 0;
        }
        let cell = cell as usize;
        self.served(run, cell) * share / self.capacity[cell].max(1)
    }

    /// C# `MeasureNeeds`.
    fn measure_needs(&mut self, run: &Run) {
        self.unmet.fill(0);
        self.spare.fill(0);
        self.surplus.fill(0);
        self.next_industry.fill(0);
        for c in 0..self.need.len() {
            let wanted = self.wanted(run, c, self.industry[c]);
            self.tally(run, c, wanted, 1);
        }
    }

    /// C# `MeasureFedCells`: after a pass `next_industry` holds what the cells were measured with.
    fn measure_fed_cells(&mut self, run: &Run) {
        for i in 0..self.fed_cells.len() {
            let c = self.fed_cells[i];
            if self.industry[c] == self.next_industry[c] {
                continue;
            }
            let old = self.wanted(run, c, self.next_industry[c]);
            self.tally(run, c, old, -1);
            let new = self.wanted(run, c, self.industry[c]);
            self.tally(run, c, new, 1);
        }
    }

    fn wanted(&self, run: &Run, c: usize, industry: i64) -> i64 {
        let config = &run.db.config;
        let households = config.forecast_household_weight;
        let forecast = households.mul_int(self.households[c] + industry)
            + (Fx::ONE - households).mul_int(run.forecast[c]);
        let cover = if run.db.goods[run.good_of(c)].traits.made_to_order {
            0
        } else {
            config.forecast_cover.mul_int(forecast)
        };
        forecast + cover + self.ordered[c]
    }

    fn tally(&mut self, run: &Run, c: usize, wanted: i64, sign: i64) {
        let k = self.cell_purse[c] * run.good_count + run.good_of(c);
        let stock = i64::from(run.stock[c]);
        let need = (wanted - stock).max(0);
        self.need[c] = need;
        self.surplus[k] += sign * (stock - wanted).max(0);
        self.unmet[k] += sign * (need - self.capacity[c]).max(0);
        self.spare[k] += sign * (self.capacity[c] - need).max(0);
    }

    /// C# `Served`: the cell's own need, and its makers' part of what other markets lack.
    fn served(&self, run: &Run, cell: usize) -> i64 {
        let k = self.cell_purse[cell] * run.good_count + run.good_of(cell);
        let own = self.need[cell].min(self.capacity[cell]);
        let spare = self.capacity[cell] - own;
        let lacking = (self.unmet[k] - self.surplus[k]).max(0);
        if self.spare[k] <= 0 {
            own
        } else {
            own + spare.min(lacking * spare / self.spare[k])
        }
    }

    fn add_industry(&mut self, lines: &[RecipeLine], line: usize, planned: i64) -> usize {
        let cells = &self.lines[line..line + lines.len()];
        for (input, &cell) in lines.iter().zip(cells).filter(|(_, &c)| c >= 0) {
            self.next_industry[cell as usize] +=
                Fx::mul_int_raw(i64::from(input.quantity), planned);
        }
        line + lines.len()
    }
}
