//! What shares, venture shares, land and short sales are worth, and the bank's sales of the
//! player's holdings (C# `ShareBook`, `VentureBook`, `LandState.Price`/`SellForPlayer`,
//! `ShortRules.Required`, `BuyFacilityCommand.AskingPrice`; DEC-114, DEC-128 to DEC-134).
use super::bond_desk::take_from_households;
use super::credit::BondOffers;
use super::facilities::FacilityTable;
use super::groups::GroupTable;
use super::land::LandState;
use super::money_ledger::{pay_households, pay_nation_households};
use super::securities::{AssetMarkets, ShareTable, ShortTable, VentureTable};
use crate::agents::production::lines::find_nearest_market;
use crate::clock::WEEKS_PER_YEAR;
use crate::economy::{HouseholdCash, HouseholdState, MarketStore};
use crate::world::{NodeBanks, NodeTable, WorldState};
use fes_core::Fx;
use fes_data::database::GameDatabase;
use fes_data::defs::RecipeLine;
use fes_data::SimulationConfig;

/// C# `ShareTable.PlayerHolder`.
pub const PLAYER_HOLDER: i32 = i32::MIN;
/// C# `BuyFacilityCommand.EarningsYearsAsked`.
const EARNINGS_YEARS_ASKED: i64 = 2;

/// C# `ShareTable.Households(nation)`.
pub fn households_holder(nation: i32) -> i32 {
    -1 - nation
}

/// The static tables the price rules read.
#[derive(Clone, Copy)]
pub struct Refs<'a> {
    pub db: &'a GameDatabase,
    pub nodes: &'a NodeTable,
    pub banks: &'a NodeBanks,
}

impl ShareTable {
    fn row(&self, f: i32, holder: i32) -> Option<usize> {
        (0..self.count()).find(|&r| self.facility[r] == f && self.holder[r] == holder)
    }

    /// C# `Held`.
    pub fn held(&self, f: i32, holder: i32) -> i32 {
        self.row(f, holder).map_or(0, |r| self.count[r])
    }

    /// C# `Outside`: shares of a facility that others than its group hold.
    pub fn outside(&self, f: i32) -> i32 {
        (0..self.count())
            .filter(|&r| self.facility[r] == f)
            .map(|r| self.count[r])
            .sum()
    }

    /// C# `Add`: a row reaching zero is removed by swap-back.
    pub fn add(&mut self, f: i32, holder: i32, count: i32) {
        if let Some(r) = self.row(f, holder) {
            self.count[r] += count;
            if self.count[r] == 0 {
                self.remove_row_swap_back(r);
            }
            return;
        }
        if count == 0 {
            return;
        }
        self.facility.push(f);
        self.holder.push(holder);
        self.count.push(count);
    }
}

impl AssetMarkets {
    /// C# `StockPremium`: one outside any nation.
    pub fn stock_premium(&self, nation: i32) -> Fx {
        if nation < 0 {
            Fx::ONE
        } else {
            Fx::from_raw(self.premium[nation as usize])
        }
    }

    /// C# `PropertyPremium`: the market of the node's households.
    pub fn property_premium(&self, nodes: &NodeTable, node: i32) -> Fx {
        let households = if nodes.absorbed() {
            nodes.core[node as usize]
        } else {
            node
        };
        Fx::from_raw(self.premium[self.nation_count + households as usize])
    }

    /// C# `VenturePremium`.
    pub fn venture_premium(&self, continent: i32) -> Fx {
        Fx::from_raw(self.premium[self.nation_count + self.node_count + continent as usize])
    }
}

impl LandState {
    /// C# `Free`: plots the node's households hold, for sale.
    pub fn free(&self, n: usize) -> i32 {
        (self.plots[n] - self.used[n] - self.player_plots[n]).max(0)
    }

    /// C# `YearRent` (DEC-134).
    pub fn year_rent(
        &self,
        households: &HouseholdState,
        config: &SimulationConfig,
        n: usize,
    ) -> i64 {
        let weekly = Fx::mul_int_raw(households.population[n], households.income_per_head[n]);
        weekly * i64::from(WEEKS_PER_YEAR) / 1_000_000 * config.land_rent_ppm
            / i64::from((self.plots[n] - self.used[n]).max(1))
    }

    /// C# `Fundamental`: by scarcity, and some years of the plot's rent.
    pub fn fundamental(
        &self,
        nodes: &NodeTable,
        households: &HouseholdState,
        config: &SimulationConfig,
        n: usize,
    ) -> i64 {
        config.land_value[usize::from(nodes.level[n])] * i64::from(self.plots[n])
            / i64::from(self.free(n).max(1))
            + i64::from(config.land_rent_years) * self.year_rent(households, config, n)
    }
}

/// C# `LandState.Price`: the fundamental times the node's property premium.
pub fn land_price(
    world: &WorldState,
    nodes: &NodeTable,
    config: &SimulationConfig,
    n: usize,
) -> i64 {
    let land = world.get::<LandState>();
    let fundamental = land.fundamental(nodes, world.get::<HouseholdState>(), config, n);
    let premium = world
        .get::<AssetMarkets>()
        .property_premium(nodes, n as i32);
    premium.mul_int(fundamental).max(1)
}

/// C# `LandState.SellForPlayer`: plots sold one at a time to the node's households, as far as they
/// can pay. Returns the coins raised.
pub fn sell_land_for_player(
    world: &mut WorldState,
    nodes: &NodeTable,
    config: &SimulationConfig,
    n: usize,
    mut count: i32,
) -> i64 {
    let households = if nodes.absorbed() {
        nodes.core[n] as usize
    } else {
        n
    };
    let mut raised = 0;
    while count > 0 && world.get::<LandState>().player_plots[n] > 0 {
        let price = land_price(world, nodes, config, n);
        if world.get::<HouseholdCash>().cash[households] < price {
            break;
        }
        pay_households(world, nodes, n as i32, -price);
        world.get_mut::<LandState>().player_plots[n] -= 1;
        raised += price;
        count -= 1;
    }
    raised
}

fn worth(r: Refs, markets: &MarketStore, node: i32, lines: &[RecipeLine]) -> i64 {
    lines
        .iter()
        .filter_map(|line| {
            find_nearest_market(r.nodes, markets, node, line.good.0)
                .map(|(_, cell)| i64::from(line.quantity) * i64::from(markets.price[cell]))
        })
        .sum()
}

/// C# `NpcLending.FacilityValue`.
pub fn facility_value(db: &GameDatabase, facilities: &FacilityTable, f: usize) -> i64 {
    let tier = db.facilities[facilities.def_index[f] as usize].tier as usize;
    db.config.facility_value[tier] * i64::from(facilities.level[f])
}

/// C# `NpcLending.FacilityUpkeep`.
pub fn facility_upkeep(db: &GameDatabase, facilities: &FacilityTable, f: usize) -> i64 {
    let tier = db.facilities[facilities.def_index[f] as usize].tier as usize;
    db.config.facility_upkeep[tier] * i64::from(facilities.level[f])
}

/// C# `BuyFacilityCommand.WeeklyEarnings`: a week at the nearest prices, less wages, inputs, aids
/// and upkeep.
pub fn weekly_earnings(world: &WorldState, r: Refs, f: usize) -> i64 {
    let config = &r.db.config;
    let facilities = world.get::<FacilityTable>();
    let markets = world.get::<MarketStore>();
    let recipe = &r.db.recipes[r.db.facilities[facilities.def_index[f] as usize].recipe];
    let node = facilities.node[f];
    let wages: i64 = recipe
        .outputs
        .iter()
        .map(|line| {
            let base = i64::from(markets.good_base_price[line.good.0 as usize]);
            config
                .facility_output_wage
                .mul_int(i64::from(line.quantity) * base)
        })
        .sum();
    let outputs = worth(r, markets, node, &recipe.outputs);
    let week = outputs
        - config.trader_spread.mul_int(outputs)
        - wages
        - worth(r, markets, node, &recipe.inputs)
        - worth(r, markets, node, &recipe.aids);
    let level = facilities.level[f];
    let scale = Fx::from_int(i64::from(level))
        * (Fx::ONE + config.facility_expansion_bonus * Fx::from_int(i64::from(level - 1)));
    scale.mul_int(week) - facility_upkeep(r.db, facilities, f)
}

/// C# `BuyFacilityCommand.AskingPrice` (DEC-114).
pub fn asking_price(world: &WorldState, r: Refs, f: usize) -> i64 {
    let facilities = world.get::<FacilityTable>();
    let earnings =
        weekly_earnings(world, r, f).max(0) * i64::from(WEEKS_PER_YEAR) * EARNINGS_YEARS_ASKED;
    facility_value(r.db, facilities, f) + earnings + facilities.cash[f].max(0)
}

/// C# `ShareBook.Price`: the asking price over the shares, times the nation's stock premium.
pub fn share_price(world: &WorldState, r: Refs, f: usize) -> i64 {
    let fundamental =
        (asking_price(world, r, f) / i64::from(r.db.config.facility_shares.max(1))).max(1);
    let node = world.get::<FacilityTable>().node[f];
    let nation = r.banks.nation_of(r.nodes, node);
    world
        .get::<AssetMarkets>()
        .stock_premium(nation)
        .mul_int(fundamental)
        .max(1)
}

/// C# `VentureBook.Price`.
pub fn venture_price(world: &WorldState, config: &SimulationConfig, v: usize) -> i64 {
    let ventures = world.get::<VentureTable>();
    let fundamental = ((ventures.cash[v]
        + ventures.stake[v]
        + i64::from(config.venture_value_years) * ventures.profit_year[v].max(0))
        / i64::from(config.venture_shares))
    .max(1);
    world
        .get::<AssetMarkets>()
        .venture_premium(ventures.continent[v])
        .mul_int(fundamental)
        .max(1)
}

/// C# `ShortRules.Required`: `share` of what buying back the short sales held at `bank` costs.
pub fn short_required(world: &WorldState, r: Refs, bank: i32, share: Fx) -> i64 {
    let shorts = world.get::<ShortTable>();
    let mut value = 0i64;
    for s in 0..shorts.count() {
        if shorts.bank[s] != bank {
            continue;
        }
        let asset = shorts.asset[s] as usize;
        let price = if shorts.venture[s] != 0 {
            venture_price(world, &r.db.config, asset)
        } else {
            share_price(world, r, asset)
        };
        value += i64::from(shorts.shares[s]) * price;
    }
    share.mul_int(value)
}

/// Coins taken from the households beyond whole shares go back to them.
fn whole_shares(world: &mut WorldState, nation: i32, taken: i64, price: i64) -> i32 {
    let sold = (taken / price) as i32;
    let kept = i64::from(sold) * price;
    if taken > kept {
        pay_nation_households(world, nation, taken - kept);
    }
    sold
}

/// C# `ShareBook.SellForPlayer` (DEC-130): the group buys back what it can pay for, the nation's
/// households the rest. Returns the coins raised.
pub fn sell_shares_for_player(
    world: &mut WorldState,
    offers: &mut BondOffers,
    r: Refs,
    turn: i32,
    f: usize,
    count: i32,
) -> i64 {
    let fi = f as i32;
    let count = count.min(world.get::<ShareTable>().held(fi, PLAYER_HOLDER));
    let price = share_price(world, r, f);
    let (group, node) = {
        let facilities = world.get::<FacilityTable>();
        (facilities.group[f], facilities.node[f])
    };
    let back = if group >= 0 {
        let cash = world.get::<GroupTable>().cash[group as usize];
        i64::from(count).min(cash.max(0) / price) as i32
    } else {
        0
    };
    if back > 0 {
        world.get_mut::<GroupTable>().cash[group as usize] -= i64::from(back) * price;
    }
    world.get_mut::<ShareTable>().add(fi, PLAYER_HOLDER, -back);
    let raised = i64::from(back) * price;
    let nation = r.banks.nation_of(r.nodes, node);
    if count == back || nation < 0 {
        return raised;
    }
    let want = i64::from(count - back) * price;
    let taken = take_from_households(world, offers, &r.db.config, turn, nation, want);
    let sold = whole_shares(world, nation, taken, price);
    let shares = world.get_mut::<ShareTable>();
    shares.add(fi, PLAYER_HOLDER, -sold);
    shares.add(fi, households_holder(nation), sold);
    raised + i64::from(sold) * price
}

/// C# `VentureBook.SellForPlayer` (DEC-130): to the port nation's households, as far as they pay.
pub fn sell_venture_shares_for_player(
    world: &mut WorldState,
    offers: &mut BondOffers,
    r: Refs,
    turn: i32,
    v: usize,
    count: i32,
) -> i64 {
    let count = count.min(world.get::<VentureTable>().player_shares[v]);
    let price = venture_price(world, &r.db.config, v);
    let nation = r
        .banks
        .nation_of(r.nodes, world.get::<VentureTable>().port[v]);
    let want = i64::from(count) * price;
    let taken = take_from_households(world, offers, &r.db.config, turn, nation, want);
    let sold = whole_shares(world, nation, taken, price);
    world.get_mut::<VentureTable>().player_shares[v] -= sold;
    i64::from(sold) * price
}
