//! `agents.policy` (C# `PolicySystem`, DEC-058 to DEC-060): the yearly review. Every group's
//! influence in each nation (assets, jobs, lobbying) weighs its stance on tolls, license prices,
//! licensed goods and minting; a policy whose balance passes a growing share of the nation's
//! influence moves one step. A toll step change rescales the nation's `NodeTolls`.
use super::asset_prices::facility_value;
use super::banks::BankState;
use super::facilities::{FacilityTable, OWNER_BANK};
use super::finance::FinanceState;
use super::groups::{city_of, GroupTable, KIND_STATE_ENTERPRISE};
use super::merchants::{capital_start, AgentTable};
use super::policy::PolicyState;
use super::subsidies::guild_of;
use crate::context::SimContext;
use crate::economy::CurrencyState;
use crate::error::SimError;
use crate::events::EventBus;
use crate::pipeline::{sim_order, SimPhase, System};
use crate::world::{NodeBanks, NodeTable, NodeTolls, WorldState};
use fes_core::det::det_hash;
use fes_core::{DetRandom, Fx, GoodId};
use fes_data::database::GameDatabase;
use fes_data::defs::RecipeDef;
use fes_data::SimulationConfig;
use std::collections::{BTreeSet, HashMap};

/// C# `FinanceOrder.Policy`.
pub const ORDER: i32 = sim_order::LATE + 30;

/// C# `PolicyKind` bytes.
pub const POLICY_TOLLS: u8 = 0;
pub const POLICY_LICENSE_PRICE: u8 = 1;
pub const POLICY_LICENSED_GOODS: u8 = 2;
pub const POLICY_MINTING: u8 = 3;

/// One nation's yearly review (C# `PolicyReviewed`); `nation` is the nation's node.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PolicyReviewed {
    pub nation: i32,
    pub year: i32,
    pub toll_step: i32,
    pub license_price_step: i32,
    pub license_added: i32,
    pub license_removed: i32,
    pub influence: i64,
}

/// A continent's minting step moved (C# `MintingPolicyChanged`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MintingPolicyChanged {
    pub continent: i32,
    pub step: i32,
}

/// C# `PolicyRules.Scale`.
pub fn scale(config: &SimulationConfig, step: i32) -> Fx {
    Fx::ONE + config.policy_step * Fx::from_int(i64::from(step))
}

fn clamp(raw: i64) -> i64 {
    raw.clamp(-Fx::ONE_RAW, Fx::ONE_RAW)
}

/// `agents.policy`.
pub struct PolicySystem;

impl PolicySystem {
    fn apply_tolls(ctx: &mut SimContext) {
        let steps = ctx.world.get::<PolicyState>().toll_step.clone();
        let mut tolls = std::mem::take(ctx.services.get_mut::<NodeTolls>());
        let s = &ctx.services;
        let (nodes, banks) = (s.get::<NodeTable>(), s.get::<NodeBanks>());
        for (n, &step) in steps.iter().enumerate() {
            let scale = scale(&s.database.config, step);
            tolls.scale_nation(nodes, banks.nation_node(nodes, n as i32), scale);
        }
        *ctx.services.get_mut::<NodeTolls>() = tolls;
    }
}

impl System for PolicySystem {
    fn id(&self) -> &'static str {
        "agents.policy"
    }

    fn phase(&self) -> SimPhase {
        SimPhase::Finance
    }

    fn order(&self) -> i32 {
        ORDER
    }

    fn initialize(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        Self::apply_tolls(ctx);
        Ok(())
    }

    fn on_loaded(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        Self::apply_tolls(ctx);
        Ok(())
    }

    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        if !ctx.clock().is_new_year() || ctx.turn() == 0 {
            return Ok(());
        }
        let year = ctx.clock().year() - 1;
        let seed = ctx.seed();
        let mut tolls = std::mem::take(ctx.services.get_mut::<NodeTolls>());
        let s = &ctx.services;
        let mut review = Review {
            db: &s.database,
            config: &s.database.config,
            nodes: s.get::<NodeTable>(),
            banks: s.get::<NodeBanks>(),
            world: &mut ctx.world,
            events: &mut ctx.events,
            tolls: &mut tolls,
            seed,
            holdings: Vec::new(),
            by_key: HashMap::new(),
            tilt: Vec::new(),
            lobbied: Vec::new(),
        };
        review.gather();
        review.weigh(year);
        review.world.get_mut::<PolicyState>().lobbying.clear();
        *ctx.services.get_mut::<NodeTolls>() = tolls;
        Ok(())
    }
}

/// What one group holds in one nation, and its stances there.
#[derive(Default)]
struct Holding {
    group: i32,
    nation: i32,
    assets: i64,
    jobs: i64,
    output_value: i64,
    input_value: i64,
    produces: BTreeSet<i32>,
    buys: BTreeSet<i32>,
    licensed_merchants: i32,
    unlicensed_merchants: i32,
}

/// How the influence falls, per nation and per continent.
struct Votes {
    goods: usize,
    total: Vec<i64>,
    tolls: Vec<i64>,
    prices: Vec<i64>,
    all_goods: Vec<i64>,
    per_good: Vec<i64>,
    mint_total: Vec<i64>,
    mint: Vec<i64>,
}

struct Review<'a> {
    db: &'a GameDatabase,
    config: &'a SimulationConfig,
    nodes: &'a NodeTable,
    banks: &'a NodeBanks,
    world: &'a mut WorldState,
    events: &'a mut EventBus,
    tolls: &'a mut NodeTolls,
    seed: u64,
    holdings: Vec<Holding>,
    by_key: HashMap<i64, usize>,
    tilt: Vec<i64>,
    lobbied: Vec<i64>,
}

impl Review<'_> {
    fn get(&mut self, group: i32, nation: i32) -> &mut Holding {
        let key = (i64::from(group) << 32) | i64::from(nation as u32);
        let i = *self.by_key.entry(key).or_insert_with(|| {
            self.holdings.push(Holding {
                group,
                nation,
                ..Holding::default()
            });
            self.holdings.len() - 1
        });
        &mut self.holdings[i]
    }

    fn nation_of(&self, node: i32) -> i32 {
        self.banks.nation_of(self.nodes, node)
    }

    fn gather(&mut self) {
        let mut worth: Vec<i64> = self
            .world
            .get::<GroupTable>()
            .cash
            .iter()
            .map(|&c| c.max(0))
            .collect();
        self.gather_facilities(&mut worth);
        self.gather_merchants(&mut worth);
        self.gather_groups(&worth);
    }

    fn gather_facilities(&mut self, worth: &mut [i64]) {
        let jobs_per = i64::from(self.config.jobs_per_facility_level);
        for f in 0..self.world.get::<FacilityTable>().count() {
            let facilities = self.world.get::<FacilityTable>();
            let g = facilities.group[f];
            if g < 0 || facilities.owner[f] == OWNER_BANK {
                continue;
            }
            let cash = facilities.cash[f].max(0);
            worth[g as usize] += cash;
            let node = facilities.node[f];
            let nation = self.nation_of(node);
            if nation < 0 {
                continue;
            }
            let db = self.db;
            let recipe = &db.recipes[db.facilities[facilities.def_index[f] as usize].recipe];
            let level = facilities.level[f];
            let value = facility_value(db, facilities, f);
            let owner = self.get(g, nation);
            owner.assets += value + cash;
            owner.jobs += i64::from(level) * jobs_per;
            add_trade(db, owner, recipe, level);
            self.guild_speaks(node, nation, recipe, level);
        }
    }

    /// Guilds speak for the trades their city's facilities work in (DEC-055).
    fn guild_speaks(&mut self, node: i32, nation: i32, recipe: &RecipeDef, level: i32) {
        let city = city_of(self.nodes, node);
        if city < 0 {
            return;
        }
        let db = self.db;
        for line in &recipe.outputs {
            let category = db.good(line.good).category_index;
            let guild = guild_of(self.world.get::<GroupTable>(), city, category);
            if guild < 0 {
                continue;
            }
            let jobs = i64::from(level) * i64::from(self.config.jobs_per_facility_level);
            let members = self.get(guild, nation);
            members.jobs += jobs;
            add_trade(db, members, recipe, level);
            break;
        }
    }

    fn gather_merchants(&mut self, worth: &mut [i64]) {
        let config = self.config;
        let stream = det_hash::of("agents.licenses.merchants");
        for a in 0..self.world.get::<AgentTable>().count() {
            let agents = self.world.get::<AgentTable>();
            let g = agents.group[a];
            if g < 0 {
                continue;
            }
            let cash = agents.cash[a].max(0);
            worth[g as usize] += cash;
            let nation = self.nation_of(agents.home[a]);
            if nation < 0 {
                continue;
            }
            let stake = capital_start(config, self.seed, a as i32);
            let licensed = DetRandom::for_key(self.seed, 0, stream, a as i32, 0)
                .chance_permille(config.licensed_merchant_permille);
            let h = self.get(g, nation);
            h.assets += stake + cash;
            h.jobs += i64::from(config.jobs_per_merchant_group);
            h.input_value += stake; // merchants pay the tolls
            if licensed {
                h.licensed_merchants += 1;
            } else {
                h.unlicensed_merchants += 1;
            }
        }
    }

    /// Loss tilt (DEC-059) and lobbying from group cash.
    fn gather_groups(&mut self, worth: &[i64]) {
        let config = self.config;
        let count = self.world.get::<GroupTable>().count();
        self.tilt = vec![0; count];
        self.lobbied = vec![0; count];
        for g in 0..count {
            let last = &mut self.world.get_mut::<PolicyState>().last_worth;
            if g < last.len() && last[g] > 0 && worth[g] < last[g] {
                let lost =
                    (config.policy_loss_tilt * Fx::from_ratio(last[g] - worth[g], last[g])).raw();
                self.tilt[g] = Fx::ONE_RAW.min(lost);
            }
            if g < last.len() {
                last[g] = worth[g];
            } else {
                last.push(worth[g]);
            }
            let groups = self.world.get::<GroupTable>();
            let (node, cash, player) = (groups.node[g], groups.cash[g], groups.is_player[g]);
            let nation = self.nation_of(node);
            if nation < 0 {
                continue;
            }
            self.get(g as i32, nation).assets += cash.max(0);
            if player != 0 || cash <= 0 {
                continue;
            }
            let spend = config.npc_lobby_share.mul_int(cash);
            self.world.get_mut::<GroupTable>().cash[g] -= spend;
            self.world.get_mut::<BankState>().reserve[nation as usize] += spend;
            self.lobbied[g] = spend;
        }
    }

    fn weigh(&mut self, year: i32) {
        let policy = self.world.get::<PolicyState>();
        let (nations, goods, continents) = (
            policy.nation_count,
            policy.base_good_count,
            policy.continent_count,
        );
        let mut votes = Votes {
            goods,
            total: vec![0; nations],
            tolls: vec![0; nations],
            prices: vec![0; nations],
            all_goods: vec![0; nations],
            per_good: vec![0; nations * goods],
            mint_total: vec![0; continents],
            mint: vec![0; continents],
        };
        for h in &self.holdings {
            self.count_holding(h, &mut votes);
        }
        let lobbying = self.world.get::<PolicyState>().lobbying.clone();
        for e in &lobbying {
            self.count_lobbying(e, &mut votes);
        }
        for n in 0..nations {
            self.decide_nation(n, year, &votes);
        }
        for c in 0..continents {
            self.decide_minting(c, &votes);
        }
    }

    fn continent_of_nation(&self, n: i32) -> usize {
        self.nodes.continent[self.banks.nation_node(self.nodes, n) as usize] as usize
    }

    fn count_holding(&self, h: &Holding, votes: &mut Votes) {
        let config = self.config;
        let groups = self.world.get::<GroupTable>();
        let g = h.group as usize;
        let mut influence =
            config.influence_asset_weight.mul_int(h.assets) + h.jobs * config.influence_per_job;
        if self.nation_of(groups.node[g]) == h.nation {
            influence += config.influence_lobby_weight.mul_int(self.lobbied[g]);
        }
        let n = h.nation as usize;
        let continent = self.continent_of_nation(h.nation);
        votes.total[n] += influence;
        votes.mint_total[continent] += influence;
        if groups.kind[g] == KIND_STATE_ENTERPRISE {
            // Up on tolls, wider and dearer licenses, more minting (DEC-059).
            votes.tolls[n] += influence;
            votes.prices[n] += influence;
            votes.all_goods[n] += influence;
            votes.mint[continent] += influence;
            return;
        }
        // Everyone else opposes minting once prices rose over the published year (DEC-060).
        votes.mint[continent] -=
            Fx::from_raw(self.minting_opposition(continent)).mul_int(influence);
        let tilt = self.tilt[g];
        let trade = h.output_value + h.input_value;
        let toll = if trade == 0 {
            0
        } else {
            Fx::from_ratio(h.output_value - h.input_value, trade).raw()
        } + tilt;
        votes.tolls[n] += Fx::from_raw(clamp(toll)).mul_int(influence);
        self.count_goods(h, tilt, influence, votes);
    }

    fn count_goods(&self, h: &Holding, tilt: i64, influence: i64, votes: &mut Votes) {
        let policy = self.world.get::<PolicyState>();
        let n = h.nation as usize;
        let goods = policy.base_good_count;
        let requires = |b: i32| policy.licensed[n * goods + b as usize] != 0;
        let mut involved: BTreeSet<i32> = h.produces.union(&h.buys).copied().collect();
        if h.licensed_merchants + h.unlicensed_merchants > 0 {
            involved.extend((0..goods as i32).filter(|&b| requires(b)));
        }
        let (mut price_stance, mut licensed_involved) = (0i64, 0i64);
        for b in involved {
            let licensed = requires(b);
            let Some(stance) = good_stance(h, b, licensed, tilt) else {
                continue;
            };
            votes.per_good[n * votes.goods + b as usize] += Fx::from_raw(stance).mul_int(influence);
            if licensed {
                price_stance += stance;
                licensed_involved += 1;
            }
        }
        if licensed_involved > 0 {
            votes.prices[n] += Fx::from_raw(price_stance / licensed_involved).mul_int(influence);
        }
    }

    fn count_lobbying(&self, e: &super::policy::LobbyEntry, votes: &mut Votes) {
        let weight = self.config.influence_lobby_weight.mul_int(e.amount);
        let push = i64::from(e.direction) * weight;
        let n = e.nation as usize;
        let continent = self.continent_of_nation(e.nation);
        votes.total[n] += weight;
        match e.policy {
            POLICY_TOLLS => votes.tolls[n] += push,
            POLICY_LICENSE_PRICE => votes.prices[n] += push,
            POLICY_LICENSED_GOODS => votes.per_good[n * votes.goods + e.base_good as usize] += push,
            POLICY_MINTING => {
                votes.mint[continent] += push;
                votes.mint_total[continent] += weight;
            }
            _ => {}
        }
    }

    fn decide_nation(&mut self, n: usize, year: i32, votes: &Votes) {
        let config = self.config;
        let threshold = config.policy_move_threshold.mul_int(votes.total[n]);
        let change = licensed_change(self.world.get::<PolicyState>(), n);
        let resisted = resisted(config, threshold, change);
        let policy = self.world.get_mut::<PolicyState>();
        let toll_before = policy.toll_step[n];
        policy.toll_step[n] = step(config, toll_before, votes.tolls[n], threshold);
        policy.license_price_step[n] = step(
            config,
            policy.license_price_step[n],
            votes.prices[n],
            threshold,
        );
        let (added, removed) =
            change_licensed_goods(policy, n, &votes.per_good, votes.all_goods[n], resisted);
        policy.last_influence[n] = votes.total[n];
        let (toll, price) = (policy.toll_step[n], policy.license_price_step[n]);
        let nation_node = self.banks.nation_node(self.nodes, n as i32);
        if toll != toll_before {
            self.tolls
                .scale_nation(self.nodes, nation_node, scale(config, toll));
        }
        self.events.publish(PolicyReviewed {
            nation: nation_node,
            year,
            toll_step: toll,
            license_price_step: price,
            license_added: added,
            license_removed: removed,
            influence: votes.total[n],
        });
    }

    fn decide_minting(&mut self, c: usize, votes: &Votes) {
        let config = self.config;
        let policy = self.world.get_mut::<PolicyState>();
        let before = policy.mint_step[c];
        let threshold = config.policy_move_threshold.mul_int(votes.mint_total[c]);
        policy.mint_step[c] = step(config, before, votes.mint[c], threshold);
        let now = policy.mint_step[c];
        if now == before {
            return;
        }
        self.world.get_mut::<CurrencyState>().mint_rate_multiplier[c] = scale(config, now);
        self.events.publish(MintingPolicyChanged {
            continent: c as i32,
            step: now,
        });
    }

    /// The published inflation over the config level that turns a group fully against minting.
    fn minting_opposition(&self, continent: usize) -> i64 {
        let inflation = self.world.get::<FinanceState>().published_inflation[continent];
        let full = self.config.inflation_opposition.raw();
        if inflation <= 0 || full <= 0 {
            return 0;
        }
        Fx::ONE_RAW.min(Fx::from_ratio(inflation, full).raw())
    }
}

/// What a facility sells and buys in a week at full output, valued at base prices.
fn add_trade(db: &GameDatabase, h: &mut Holding, recipe: &RecipeDef, level: i32) {
    for line in &recipe.outputs {
        h.produces.insert(line.good.base_index());
    }
    for line in &recipe.inputs {
        h.buys.insert(line.good.base_index());
    }
    let value = |lines: &[fes_data::defs::RecipeLine]| -> i64 {
        lines
            .iter()
            .map(|l| {
                i64::from(l.quantity)
                    * i64::from(level)
                    * i64::from(db.good(GoodId(l.good.0)).base_price)
            })
            .sum()
    };
    h.output_value += value(&recipe.outputs);
    h.input_value += value(&recipe.inputs);
}

/// A group's stance on licensing a good, or none (DEC-059).
fn good_stance(h: &Holding, b: i32, licensed: bool, tilt: i64) -> Option<i64> {
    let mut stance = 0i64;
    let mut involved = false;
    if h.produces.contains(&b) {
        stance += Fx::ONE_RAW;
        involved = true;
    }
    if h.buys.contains(&b) {
        stance -= Fx::ONE_RAW;
        involved = true;
    }
    let merchants = h.licensed_merchants + h.unlicensed_merchants;
    if licensed && merchants > 0 {
        stance += Fx::from_ratio(
            i64::from(h.licensed_merchants - h.unlicensed_merchants),
            2 * i64::from(merchants),
        )
        .raw();
        involved = true;
    }
    involved.then(|| clamp(stance + tilt))
}

/// One good per review: the strongest case to license an unlicensed good or to free a licensed one.
fn change_licensed_goods(
    policy: &mut PolicyState,
    n: usize,
    per_good: &[i64],
    all_goods: i64,
    threshold: i64,
) -> (i32, i32) {
    let goods = policy.base_good_count;
    let (mut added, mut removed) = (-1i32, -1i32);
    let (mut best_add, mut best_remove) = (0i64, 0i64);
    for b in 0..goods {
        let net = per_good[n * goods + b] + all_goods;
        let requires = policy.licensed[n * goods + b] != 0;
        if !requires && net >= threshold && net > best_add {
            best_add = net;
            added = b as i32;
        }
        if requires && net <= -threshold && -net > best_remove {
            best_remove = -net;
            removed = b as i32;
        }
    }
    if added >= 0 && removed >= 0 {
        if best_add >= best_remove {
            removed = -1;
        } else {
            added = -1;
        }
    }
    if added >= 0 {
        policy.licensed[n * goods + added as usize] = 1;
    }
    if removed >= 0 {
        policy.licensed[n * goods + removed as usize] = 0;
    }
    (added, removed)
}

fn resisted(config: &SimulationConfig, threshold: i64, step: i32) -> i64 {
    threshold + (config.policy_resistance * Fx::from_int(i64::from(step.abs()))).mul_int(threshold)
}

/// How many goods the nation licenses beyond (or below) goods.csv's list.
fn licensed_change(policy: &PolicyState, n: usize) -> i32 {
    let goods = policy.base_good_count;
    (0..goods)
        .map(|b| {
            i32::from(policy.licensed[n * goods + b])
                - i32::from(policy.licensed[policy.nation_count * goods + b])
        })
        .sum()
}

fn step(config: &SimulationConfig, step: i32, net: i64, threshold: i64) -> i32 {
    let threshold = resisted(config, threshold, step);
    let max = config.policy_max_steps;
    if net > 0 && net >= threshold {
        return max.min(step + 1);
    }
    if net < 0 && -net >= threshold {
        return (-max).max(step - 1);
    }
    step
}
