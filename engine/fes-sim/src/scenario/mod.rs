//! Teaching scenarios (spec 5): the scenario switch, the results statement, scripted events and
//! objectives. Nothing here runs in free mode.
pub mod defs;
pub mod objectives;
pub mod rules;
pub mod script;
pub mod statement;

pub use defs::{Effect, EventDef, Goal, LicensedGood, ObjectiveDef, ScenarioDef, Trigger};
pub use objectives::{ObjectiveView, Objectives};
pub use rules::{Opening, ScenarioRules};
pub use script::{ScenarioContract, Script};
pub use statement::{StatementCosts, StatementLedger, StatementView};

use crate::agents::money_ledger::{pay_households, take_households};
use crate::agents::player::PlayerState;
use crate::agents::policy::PolicyState;
use crate::context::SimContext;
use crate::event_record::SimEventRecord;
use crate::world::{NodeBanks, NodeTable};

/// The scenario state a running simulation keeps beside the world (never saved: scenarios do not
/// save, spec 9.2).
#[derive(Clone, Debug)]
pub struct ScenarioRun {
    pub ledger: StatementLedger,
    pub script: Script,
    pub objectives: Objectives,
    /// Weeks the scenario lasts; 0 for rules without a definition (it never ends).
    pub weeks: i32,
    start_turn: i32,
    pub ended: bool,
}

impl ScenarioRun {
    /// Starts a scenario on a freshly built world: places the player and its coins, then checks
    /// the script against the world.
    pub fn start(ctx: &mut SimContext, def: Option<&ScenarioDef>) -> Result<Self, String> {
        if let Some(def) = def {
            place_player(ctx, def)?;
            license_goods(ctx, &def.licensed)?;
        }
        let ledger = StatementLedger::start(ctx);
        let home = ledger.home();
        let empty = ScenarioDef::default();
        let def = def.unwrap_or(&empty);
        Ok(Self {
            ledger,
            script: Script::new(def, ctx, home)?,
            objectives: Objectives::new(&def.objectives),
            weeks: def.weeks,
            start_turn: ctx.turn(),
            ended: false,
        })
    }

    /// Weeks played.
    pub fn week(&self, ctx: &SimContext) -> i32 {
        ctx.turn() - self.start_turn
    }

    /// Called after each turn with the turn's events; appends the scenario's own events.
    pub fn after_turn(&mut self, ctx: &mut SimContext, events: &mut Vec<SimEventRecord>) {
        let week = self.week(ctx);
        let mut own = Vec::new();
        self.script.tick(ctx, week, &mut own);
        events.append(&mut own);
        self.ledger.record(ctx, events);
        let net_profit = self.ledger.view(ctx).net_profit;
        self.objectives
            .check(ctx, &self.script, net_profit, week, events);
        if self.weeks > 0 && week >= self.weeks {
            self.ended = true;
            events.push(SimEventRecord::ScenarioEnded {
                net_profit,
                objectives_done: self.objectives.done_count(),
                turn: ctx.turn(),
            });
        }
    }
}

/// Moves the player to the start node with exactly the start coins; households give or take the
/// difference so the world's money stays the same (taking only what they hold).
fn place_player(ctx: &mut SimContext, def: &ScenarioDef) -> Result<(), String> {
    let at = script::Keys(ctx).node(&def.start_node)?;
    let nodes = ctx.services.get::<NodeTable>();
    let continents = ctx.world.get::<PlayerState>().coins.len();
    for c in 0..continents {
        let held = ctx.world.get::<PlayerState>().coins[c];
        let home = (0..nodes.count() as i32).find(|&n| nodes.continent[n as usize] as usize == c);
        if let (true, Some(home)) = (held != 0, home) {
            pay_households(&mut ctx.world, nodes, home, held);
            ctx.world.get_mut::<PlayerState>().coins[c] = 0;
        }
    }
    let local = nodes.continent[at as usize];
    // The start node's households first, then the rest of the continent's, in node order.
    let mut taken = take_households(&mut ctx.world, nodes, at, def.start_coins);
    for n in (0..nodes.count() as i32).filter(|&n| nodes.continent[n as usize] == local) {
        taken += take_households(&mut ctx.world, nodes, n, def.start_coins - taken);
    }
    if taken < def.start_coins {
        pay_households(&mut ctx.world, nodes, at, taken);
        return Err(format!(
            "The households of the start continent hold less than {} coins.",
            def.start_coins
        ));
    }
    let p = ctx.world.get_mut::<PlayerState>();
    p.node = at;
    p.coins[local as usize] = def.start_coins;
    Ok(())
}

/// Marks the scenario's special goods as licensed in their nations (the nation's policy list).
fn license_goods(ctx: &mut SimContext, list: &[LicensedGood]) -> Result<(), String> {
    for item in list {
        let keys = script::Keys(ctx);
        let good = keys.good(&item.good)?;
        let node = keys.node(&item.nation)?;
        let nodes = ctx.services.get::<NodeTable>();
        let nation = ctx.services.get::<NodeBanks>().nation_of(nodes, node);
        if nation < 0 {
            return Err(format!("'{}' is in no nation.", item.nation));
        }
        let base = (good / fes_core::QualityGrade::COUNT as i32) as usize;
        let policy = ctx.world.get_mut::<PolicyState>();
        let row = nation as usize * policy.base_good_count + base;
        policy.licensed[row] = 1;
    }
    Ok(())
}
