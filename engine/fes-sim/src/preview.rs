//! A trade's result the moment the player makes it (spec 5.2a): the queued commands and then the
//! new one run on the current state, in the order the Commands phase will run them, and the
//! player's purse, cart and local stock are read; then the state is put back.
use crate::agents::credit::{BondOffers, CreditStats};
use crate::agents::player::PlayerState;
use crate::commands::{Command, CommandResult};
use crate::context::SimContext;
use crate::economy::{HouseholdCash, MarketStore};
use crate::scenario::ScenarioRules;
use crate::simulation::Simulation;
use crate::views::{self, CargoView, PurseView};

/// What the player's position would be once the queued commands and this one apply.
#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct CommandPreview {
    /// Why this command would be rejected, or `None` when it applies.
    pub error: Option<String>,
    pub purse: PurseView,
    pub cargo: CargoView,
    /// (good, stock) of the market where the player then stands.
    pub stock: Vec<(i32, i32)>,
}

/// The player's position now, with `error` as the command's answer.
pub fn position(ctx: &SimContext, error: Option<String>) -> CommandPreview {
    let node = ctx.world.get::<PlayerState>().node as usize;
    let markets = ctx.world.get::<MarketStore>();
    let goods = ctx.services.database.goods.len() as i32;
    let stock = (0..goods)
        .filter_map(|g| {
            markets
                .try_find_cell(node, g)
                .map(|c| (g, markets.stock[c]))
        })
        .collect();
    CommandPreview {
        error,
        purse: views::purse(ctx),
        cargo: views::cargo(ctx),
        stock,
    }
}

/// Validates then applies, as the Commands phase does; returns the rejection reason.
fn run(ctx: &mut SimContext, cmd: &dyn Command) -> Option<String> {
    cmd.prepare(ctx);
    match cmd.validate(ctx) {
        CommandResult::Error(reason) => Some(reason),
        CommandResult::Ok => {
            cmd.apply(ctx);
            None
        }
    }
}

/// Copies of the two large components a command may change, kept between previews so their
/// buffers are reused: a preview copies them in, runs, and swaps them back in place of the changed
/// ones (no allocation). `agents.demand_forecast`, the largest, is written only by the Finance and
/// Production systems, never by a command, so it is not saved at all. Together these are about 98%
/// of a full-scale world's bytes, which a preview therefore no longer serializes.
#[derive(Default)]
pub(crate) struct Scratch {
    markets: Option<MarketStore>,
    cash: Option<HouseholdCash>,
}

/// Components a preview keeps in `Scratch` or never touches instead of serializing.
const UNSERIALIZED: [&str; 3] = [
    "agents.demand_forecast",
    "economy.markets",
    "economy.household_cash",
];

fn copy_markets(dst: &mut Option<MarketStore>, src: &MarketStore) {
    let Some(d) = dst else {
        *dst = Some(src.clone());
        return;
    };
    let MarketStore {
        market_cell_start,
        market_cell_count,
        cell_good,
        cell_specialty,
        good_base_price,
        good_base_demand,
        good_category,
        good_weight,
        good_size,
        price,
        prev_price,
        stock,
        supply,
        demand,
        input_bid,
    } = d;
    market_cell_start.clone_from(&src.market_cell_start);
    market_cell_count.clone_from(&src.market_cell_count);
    cell_good.clone_from(&src.cell_good);
    cell_specialty.clone_from(&src.cell_specialty);
    good_base_price.clone_from(&src.good_base_price);
    good_base_demand.clone_from(&src.good_base_demand);
    good_category.clone_from(&src.good_category);
    good_weight.clone_from(&src.good_weight);
    good_size.clone_from(&src.good_size);
    price.clone_from(&src.price);
    prev_price.clone_from(&src.prev_price);
    stock.clone_from(&src.stock);
    supply.clone_from(&src.supply);
    demand.clone_from(&src.demand);
    input_bid.clone_from(&src.input_bid);
}

fn copy_cash(dst: &mut Option<HouseholdCash>, src: &HouseholdCash) {
    let Some(d) = dst else {
        *dst = Some(src.clone());
        return;
    };
    let HouseholdCash {
        cash,
        spent,
        desired,
        desired_necessity,
        pending_units,
        last_offered,
        spent_year,
        cash_year_start,
        saved_year,
        quintile_cash,
        spent_settled,
        home_grown,
    } = d;
    cash.clone_from(&src.cash);
    spent.clone_from(&src.spent);
    desired.clone_from(&src.desired);
    desired_necessity.clone_from(&src.desired_necessity);
    pending_units.clone_from(&src.pending_units);
    last_offered.clone_from(&src.last_offered);
    spent_year.clone_from(&src.spent_year);
    cash_year_start.clone_from(&src.cash_year_start);
    saved_year.clone_from(&src.saved_year);
    quintile_cash.clone_from(&src.quintile_cash);
    spent_settled.clone_from(&src.spent_settled);
    home_grown.clone_from(&src.home_grown);
}

impl Scratch {
    fn take(&mut self, ctx: &SimContext) {
        if let Some(m) = ctx.world.try_get::<MarketStore>() {
            copy_markets(&mut self.markets, m);
        }
        if let Some(c) = ctx.world.try_get::<HouseholdCash>() {
            copy_cash(&mut self.cash, c);
        }
    }

    /// Swaps the saved copies back in; the changed ones become next preview's buffers.
    fn put_back(&mut self, ctx: &mut SimContext) {
        if let Some(m) = self.markets.as_mut() {
            std::mem::swap(ctx.world.get_mut::<MarketStore>(), m);
        }
        if let Some(c) = self.cash.as_mut() {
            std::mem::swap(ctx.world.get_mut::<HouseholdCash>(), c);
        }
    }
}

/// The services a command may change while it applies.
struct Saved {
    credit: Option<CreditStats>,
    offers: Option<BondOffers>,
    rules: Option<ScenarioRules>,
}

impl Saved {
    fn take(ctx: &SimContext) -> Self {
        Self {
            credit: ctx.services.try_get::<CreditStats>().cloned(),
            offers: ctx.services.try_get::<BondOffers>().cloned(),
            rules: ctx.services.try_get::<ScenarioRules>().cloned(),
        }
    }

    fn put_back(self, ctx: &mut SimContext) {
        restore(ctx, self.credit);
        restore(ctx, self.offers);
        restore(ctx, self.rules);
    }
}

fn restore<T: std::any::Any + Send>(ctx: &mut SimContext, saved: Option<T>) {
    if let (Some(value), Some(slot)) = (saved, ctx.services.try_get_mut::<T>()) {
        *slot = value;
    }
}

impl Simulation {
    /// The result of `cmd` if queued now: every command already queued this week runs first, in
    /// order, then `cmd`, exactly as the next Commands phase will run them. Nothing changes: the
    /// world, the services commands touch and the event queue are put back. The bond-offer cache
    /// is refreshed first, as `preview` refreshes it.
    pub fn preview_queued(&mut self, cmd: &dyn Command) -> CommandPreview {
        let ctx = &mut self.ctx;
        cmd.prepare(ctx);
        let world = ctx.world.sections_except(&UNSERIALIZED);
        self.preview_scratch.take(ctx);
        let saved = Saved::take(ctx);
        let events = ctx.events.pending_len();
        let queued = ctx.commands.queued().to_vec();
        for q in &queued {
            run(ctx, q.as_ref());
        }
        let error = run(ctx, cmd);
        let out = position(ctx, error);
        for s in &world {
            ctx.world
                .load_section(s)
                .expect("a world reads back its own sections");
        }
        self.preview_scratch.put_back(ctx);
        saved.put_back(ctx);
        ctx.events.truncate_pending(events);
        out
    }
}
