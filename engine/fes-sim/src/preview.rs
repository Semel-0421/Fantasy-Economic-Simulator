//! A trade's result the moment the player makes it (spec 5.2a): the queued commands and then the
//! new one run on the current state, in the order the Commands phase will run them, and the
//! player's purse, cart and local stock are read; then the state is put back.
use crate::agents::credit::{BondOffers, CreditStats};
use crate::agents::player::PlayerState;
use crate::commands::player_command::PlayerCommand;
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

/// Only the large components' columns written by commands and their helpers (market flows,
/// household payments and bond/share purchases). Keep this list in sync with command writes.
#[derive(Default)]
pub(crate) struct Scratch {
    stock: Vec<i32>,
    supply: Vec<i32>,
    demand: Vec<i32>,
    cash: Vec<i64>,
    saved_year: Vec<i64>,
}

/// Commands never write the forecast; the other two components use column snapshots below.
const UNSERIALIZED: [&str; 3] = [
    "agents.demand_forecast",
    "economy.markets",
    "economy.household_cash",
];

impl Scratch {
    fn take(&mut self, ctx: &SimContext) {
        if let Some(m) = ctx.world.try_get::<MarketStore>() {
            self.stock.clone_from(&m.stock);
            self.supply.clone_from(&m.supply);
            self.demand.clone_from(&m.demand);
        }
        if let Some(c) = ctx.world.try_get::<HouseholdCash>() {
            self.cash.clone_from(&c.cash);
            self.saved_year.clone_from(&c.saved_year);
        }
    }

    /// Swaps the saved columns back; the changed ones become next preview's buffers.
    fn put_back(&mut self, ctx: &mut SimContext) {
        if ctx.world.try_get::<MarketStore>().is_some() {
            let m = ctx.world.get_mut::<MarketStore>();
            std::mem::swap(&mut m.stock, &mut self.stock);
            std::mem::swap(&mut m.supply, &mut self.supply);
            std::mem::swap(&mut m.demand, &mut self.demand);
        }
        if ctx.world.try_get::<HouseholdCash>().is_some() {
            let c = ctx.world.get_mut::<HouseholdCash>();
            std::mem::swap(&mut c.cash, &mut self.cash);
            std::mem::swap(&mut c.saved_year, &mut self.saved_year);
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
        let queued = ctx.commands.queued().to_vec();
        let columns_only = PlayerCommand::from_command(cmd).is_some()
            && queued
                .iter()
                .all(|q| PlayerCommand::from_command(q.as_ref()).is_some());
        // Custom commands may write other columns: preserve their previous restoration coverage.
        let skip: &[&str] = if columns_only {
            &UNSERIALIZED
        } else {
            &["agents.demand_forecast"]
        };
        let world = ctx.world.sections_except(skip);
        if columns_only {
            self.preview_scratch.take(ctx);
        }
        let saved = Saved::take(ctx);
        let events = ctx.events.pending_len();
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
        if columns_only {
            self.preview_scratch.put_back(ctx);
        }
        saved.put_back(ctx);
        ctx.events.truncate_pending(events);
        out
    }
}
