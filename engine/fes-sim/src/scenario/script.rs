//! Runs a scenario's scripted events at the end of each week: triggers, deadline fallbacks and the
//! effects, each mapped onto a system that already exists (market modifiers, encounters, shares,
//! licences, the player's cart and coins).
use super::defs::{Effect, EventDef, ScenarioDef, Trigger};
use super::rules::ScenarioRules;
use crate::agents::asset_prices::PLAYER_HOLDER;
use crate::agents::banks::BankState;
use crate::agents::cargo::{CargoTable, PLAYER_CART};
use crate::agents::credit::BondTable;
use crate::agents::credit_system::BOND_PLAYER_HOLDER;
use crate::agents::facilities::FacilityTable;
use crate::agents::groups::GroupTable;
use crate::agents::licenses::LicenseTable;
use crate::agents::money_ledger::pay_households;
use crate::agents::player::PlayerState;
use crate::agents::securities::ShareTable;
use crate::commands::bonds::bond_held;
use crate::context::SimContext;
use crate::economy::modifiers::{MarketModifier, MarketModifierTable};
use crate::event_record::SimEventRecord;
use crate::world::{NodeBanks, NodeTable, RouteTable};
use fes_core::{Fx, QualityGrade};
use fes_data::defs::ModifierTarget;

/// Node and good keys of a scenario file, looked up in a built world.
pub struct Keys<'a>(pub &'a SimContext);

impl Keys<'_> {
    pub fn node(&self, key: &str) -> Result<i32, String> {
        let nodes = self.0.services.get::<NodeTable>();
        nodes
            .try_find(key)
            .map(|n| n.0)
            .ok_or_else(|| format!("Unknown node '{key}'."))
    }

    /// The good of this base key at the middle grade.
    pub fn good(&self, key: &str) -> Result<i32, String> {
        self.0
            .services
            .database
            .try_get_good(key, QualityGrade::Mid)
            .map(|g| g.0)
            .ok_or_else(|| format!("Unknown good '{key}'."))
    }

    pub fn route(&self, from: &str, to: &str) -> Result<i32, String> {
        let routes = self.0.services.get::<RouteTable>();
        match crate::commands::travel::find_route(routes, self.node(from)?, self.node(to)?) {
            -1 => Err(format!("No route from '{from}' to '{to}'.")),
            r => Ok(r),
        }
    }

    fn facility(&self, f: i32) -> Result<i32, String> {
        let count = self.0.world.get::<FacilityTable>().count() as i32;
        (0..count)
            .contains(&f)
            .then_some(f)
            .ok_or_else(|| format!("Unknown facility {f}."))
    }

    /// A bond-desk issuer: `!nation` for a nation, else a company group.
    fn issuer(&self, issuer: i32) -> Result<i32, String> {
        let known = if issuer < 0 {
            let nodes = self.0.services.get::<NodeTable>();
            !issuer < self.0.services.get::<NodeBanks>().nation_count(nodes)
        } else {
            (issuer as usize) < self.0.world.get::<GroupTable>().count()
        };
        known
            .then_some(issuer)
            .ok_or_else(|| format!("Unknown bond issuer {issuer}."))
    }

    /// Checks every key and number an effect names.
    pub fn check_effect(&self, e: &Effect) -> Result<(), String> {
        match e {
            Effect::OfferContract { good, node, .. } => self.good(good).and(self.node(node)),
            Effect::Raid { from, to, .. } => self.route(from, to),
            Effect::PriceShock { node, good, .. } => self.good(good).and(self.node(node)),
            Effect::CompanyNews { facility, .. }
            | Effect::CompanyOrders { facility, .. }
            | Effect::Dividend { facility, .. } => self.facility(*facility),
            Effect::LicenceOffer { good, nation, .. } => self.good(good).and(self.node(nation)),
            Effect::BondIssue { issuer, .. } | Effect::Coupon { issuer, .. } => {
                self.issuer(*issuer)
            }
            Effect::Dialog(_) => Ok(0),
        }
        .map(|_| ())
    }
}

/// A delivery request offered by the script.
#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct ScenarioContract {
    pub id: String,
    pub good: i32,
    pub quantity: i32,
    pub node: i32,
    /// Coins paid on delivery, in the start currency.
    pub reward: i64,
    pub deadline_week: i32,
    pub text: String,
    pub accepted: bool,
    pub delivered: bool,
    /// The deadline passed before delivery.
    pub expired: bool,
}

/// The script's progress.
#[derive(Clone, Debug)]
pub struct Script {
    events: Vec<EventDef>,
    fired: Vec<bool>,
    pub contracts: Vec<ScenarioContract>,
    /// The start currency (rewards are paid in it).
    pub home: usize,
}

impl Script {
    /// Checks every key of the scenario against the world.
    pub fn new(def: &ScenarioDef, ctx: &SimContext, home: usize) -> Result<Self, String> {
        let keys = Keys(ctx);
        for e in def.events.iter().flat_map(|e| &e.effects) {
            keys.check_effect(e)?;
        }
        Ok(Self {
            events: def.events.clone(),
            fired: vec![false; def.events.len()],
            contracts: Vec::new(),
            home,
        })
    }

    /// One week's script: deliveries and deadlines, then every event that is due.
    pub fn tick(&mut self, ctx: &mut SimContext, week: i32, out: &mut Vec<SimEventRecord>) {
        self.deliver(ctx, week, out);
        for i in 0..self.events.len() {
            if self.fired[i] || !self.due(ctx, i, week) {
                continue;
            }
            self.fired[i] = true;
            let id = self.events[i].id.clone();
            for effect in self.events[i].effects.clone() {
                self.apply(ctx, &id, &effect, out);
            }
        }
    }

    fn due(&self, ctx: &SimContext, i: usize, week: i32) -> bool {
        let e = &self.events[i];
        if e.fallback_week.is_some_and(|w| week >= w) {
            return true;
        }
        let keys = Keys(ctx);
        let player = ctx.world.get::<PlayerState>();
        match &e.trigger {
            Trigger::Week(w) => week >= *w,
            Trigger::PlayerAt(node) => {
                !player.is_travelling() && keys.node(node).is_ok_and(|n| n == player.node)
            }
            Trigger::Holds { good, quantity } => keys.good(good).is_ok_and(|g| {
                ctx.world.get::<CargoTable>().quantity_of(PLAYER_CART, g) >= *quantity
            }),
            Trigger::ContractDone(id) => self.delivered(id),
            Trigger::Never => false,
        }
    }

    pub fn delivered(&self, id: &str) -> bool {
        self.contracts.iter().any(|c| c.id == id && c.delivered)
    }

    /// Accepts an offered contract that is still open.
    pub fn accept(&mut self, id: &str) -> Result<(), String> {
        let c = self
            .contracts
            .iter_mut()
            .find(|c| c.id == id && !c.delivered && !c.expired)
            .ok_or_else(|| format!("No open contract '{id}'."))?;
        c.accepted = true;
        Ok(())
    }

    /// Accepted contracts: delivered when the player stands at the node with the goods in the
    /// cart; expired once the deadline week has passed.
    fn deliver(&mut self, ctx: &mut SimContext, week: i32, out: &mut Vec<SimEventRecord>) {
        let turn = ctx.turn();
        for c in self.contracts.iter_mut() {
            if !c.accepted || c.delivered || c.expired {
                continue;
            }
            if week > c.deadline_week {
                c.expired = true;
                continue;
            }
            let p = ctx.world.get::<PlayerState>();
            if p.is_travelling()
                || p.node != c.node
                || ctx
                    .world
                    .get::<CargoTable>()
                    .quantity_of(PLAYER_CART, c.good)
                    < c.quantity
            {
                continue;
            }
            ctx.world
                .get_mut::<CargoTable>()
                .change(PLAYER_CART, c.good, -c.quantity);
            ctx.world.get_mut::<PlayerState>().coins[self.home] += c.reward;
            c.delivered = true;
            out.push(SimEventRecord::ScenarioDialog {
                event: c.id.clone(),
                text: String::new(),
                coins: c.reward,
                continent: self.home as i32,
                turn,
            });
        }
    }

    fn apply(&mut self, ctx: &mut SimContext, id: &str, e: &Effect, out: &mut Vec<SimEventRecord>) {
        let (text, coins, continent) = match e {
            Effect::Dialog(text) | Effect::BondIssue { text, .. } => (text.clone(), 0, -1),
            Effect::OfferContract { text, .. } => {
                self.offer(ctx, e);
                (text.clone(), 0, -1)
            }
            Effect::Raid {
                from,
                to,
                loss_permille,
            } => {
                raid(ctx, from, to, *loss_permille);
                return;
            }
            Effect::PriceShock { text, .. } => {
                price_shock(ctx, e);
                (text.clone(), 0, -1)
            }
            Effect::CompanyNews {
                facility,
                percent,
                text,
            } => {
                let cash = &mut ctx.world.get_mut::<FacilityTable>().cash[*facility as usize];
                *cash = *cash * i64::from(*percent) / 100;
                (text.clone(), 0, -1)
            }
            Effect::CompanyOrders {
                facility,
                coins,
                text,
            } => {
                let f = *facility as usize;
                let node = ctx.world.get::<FacilityTable>().node[f];
                pay_households(
                    &mut ctx.world,
                    ctx.services.get::<NodeTable>(),
                    node,
                    -coins,
                );
                ctx.world.get_mut::<FacilityTable>().cash[f] += coins;
                (text.clone(), 0, -1)
            }
            Effect::Dividend {
                facility,
                per_share,
                text,
            } => {
                let (paid, c) = dividend(ctx, *facility, *per_share);
                (text.clone(), paid, c)
            }
            Effect::Coupon {
                issuer,
                percent,
                text,
            } => {
                let (paid, c) = coupon(ctx, *issuer, *percent);
                (text.clone(), paid, c)
            }
            Effect::LicenceOffer { good, nation, text } => {
                licence(ctx, good, nation);
                (text.clone(), 0, -1)
            }
        };
        out.push(SimEventRecord::ScenarioDialog {
            event: id.to_string(),
            text,
            coins,
            continent,
            turn: ctx.turn(),
        });
    }

    fn offer(&mut self, ctx: &SimContext, e: &Effect) {
        let Effect::OfferContract {
            id,
            good,
            quantity,
            node,
            reward,
            deadline_week,
            text,
        } = e
        else {
            return;
        };
        let keys = Keys(ctx);
        let (Ok(good), Ok(node)) = (keys.good(good), keys.node(node)) else {
            return;
        };
        self.contracts.push(ScenarioContract {
            id: id.clone(),
            good,
            quantity: *quantity,
            node,
            reward: *reward,
            deadline_week: *deadline_week,
            text: text.clone(),
            accepted: false,
            delivered: false,
            expired: false,
        });
    }
}

fn raid(ctx: &mut SimContext, from: &str, to: &str, loss_permille: i32) {
    if let Ok(route) = Keys(ctx).route(from, to) {
        ctx.services
            .get_mut::<ScenarioRules>()
            .pending_raids
            .push((route, loss_permille));
    }
}

fn price_shock(ctx: &mut SimContext, e: &Effect) {
    let Effect::PriceShock {
        node,
        good,
        percent,
        weeks,
        ..
    } = e
    else {
        return;
    };
    let keys = Keys(ctx);
    let (Ok(node), Ok(good)) = (keys.node(node), keys.good(good)) else {
        return;
    };
    let category = ctx.services.database.goods[good as usize].category_index;
    let expires_turn = ctx.turn() + weeks;
    ctx.world
        .get_mut::<MarketModifierTable>()
        .add(MarketModifier {
            node,
            category,
            target: ModifierTarget::Value,
            multiplier: Fx::from_ratio(i64::from(*percent), 100),
            expires_turn,
            source_event: -1,
        });
}

/// Pays the player's shares from the company: the facility's cash first, then its group's.
fn dividend(ctx: &mut SimContext, facility: i32, per_share: i64) -> (i64, i32) {
    let held = i64::from(ctx.world.get::<ShareTable>().held(facility, PLAYER_HOLDER));
    let f = facility as usize;
    let node = ctx.world.get::<FacilityTable>().node[f];
    let group = ctx.world.get::<FacilityTable>().group[f];
    let continent = ctx.services.get::<NodeTable>().continent[node as usize];
    let due = held * per_share;
    let facilities = ctx.world.get_mut::<FacilityTable>();
    let from_facility = due.min(facilities.cash[f].max(0));
    facilities.cash[f] -= from_facility;
    let mut paid = from_facility;
    if group >= 0 {
        let cash = &mut ctx.world.get_mut::<GroupTable>().cash[group as usize];
        let from_group = (due - paid).min((*cash).max(0));
        *cash -= from_group;
        paid += from_group;
    }
    ctx.world.get_mut::<PlayerState>().coins[continent as usize] += paid;
    (paid, continent)
}

/// Pays the player `percent`% of the face it holds of the issuer's bonds, from the issuer.
fn coupon(ctx: &mut SimContext, issuer: i32, percent: i32) -> (i64, i32) {
    let face = bond_held(ctx.world.get::<BondTable>(), issuer, BOND_PLAYER_HOLDER);
    let nodes = ctx.services.get::<NodeTable>();
    let banks = ctx.services.get::<NodeBanks>();
    let node = if issuer < 0 {
        banks.nation_node(nodes, !issuer)
    } else {
        ctx.world.get::<GroupTable>().node[issuer as usize]
    };
    let continent = nodes.continent[node as usize];
    let due = face * i64::from(percent) / 100;
    let paid = if issuer < 0 {
        let bank = ctx.world.get_mut::<BankState>();
        let paid = due.min(bank.spendable(!issuer as usize));
        bank.reserve[!issuer as usize] -= paid;
        paid
    } else {
        let cash = &mut ctx.world.get_mut::<GroupTable>().cash[issuer as usize];
        let paid = due.min((*cash).max(0));
        *cash -= paid;
        paid
    };
    ctx.world.get_mut::<PlayerState>().coins[continent as usize] += paid;
    (paid, continent)
}

fn licence(ctx: &mut SimContext, good: &str, nation: &str) {
    let keys = Keys(ctx);
    let (Ok(good), Ok(nation)) = (keys.good(good), keys.node(nation)) else {
        return;
    };
    let turn = ctx.turn();
    let base = good / QualityGrade::COUNT as i32;
    ctx.world
        .get_mut::<LicenseTable>()
        .extend(base, nation, turn);
}
