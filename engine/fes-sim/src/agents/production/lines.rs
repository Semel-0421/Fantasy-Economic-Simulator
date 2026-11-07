//! Every facility's recipe lines resolved to market cells (C# `ProductionSystem.BuildLines`).
//! C# caches them until the facility table changes; the cache key omits `LandLevel`, but in every
//! reachable case they are a pure function of saved state, so they are rebuilt every turn.
use crate::agents::{DevelopmentState, FacilityTable};
use crate::economy::{MarketFlows, MarketStore};
use crate::world::{NodeSpecialties, NodeTable};
use fes_core::{Fx, GoodId, IndustryTier, QualityGrade};
use fes_data::database::GameDatabase;
use fes_data::defs::RecipeLine;

/// `Lines::is_input` of a required input.
pub const INPUT_LINE: u8 = 1;
/// `Lines::is_input` of a tool or seed line (C# `ProductionJob.AidLine`).
pub const AID_LINE: u8 = 2;
/// `ModifierTarget.Supply`.
const SUPPLY: usize = 0;

/// Lines of every facility, inputs, aids, then outputs: `start[f] .. start[f + 1]`. Cell -1 = no market.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Lines {
    pub start: Vec<usize>,
    pub cell: Vec<i32>,
    pub good: Vec<i32>,
    pub modifier: Vec<i32>,
    pub quantity: Vec<i32>,
    pub is_input: Vec<u8>,
    /// Raw Fx: specialty and land multiplier (outputs only; one otherwise).
    pub output_mul: Vec<i64>,
    /// The one-grade-higher good, its cell and modifier, or -1.
    pub up_good: Vec<i32>,
    pub up_cell: Vec<i32>,
    pub up_modifier: Vec<i32>,
}

/// The nearest market (the node, then its ancestors) trading `good`: `(market, cell)`.
pub fn find_nearest_market(
    nodes: &NodeTable,
    markets: &MarketStore,
    node: i32,
    good: i32,
) -> Option<(usize, usize)> {
    let mut market = node;
    while market >= 0 {
        if let Some(cell) = markets.try_find_cell(market as usize, good) {
            return Some((market as usize, cell));
        }
        market = nodes.parent[market as usize];
    }
    None
}

struct Builder<'a> {
    db: &'a GameDatabase,
    nodes: &'a NodeTable,
    markets: &'a MarketStore,
    flows: &'a MarketFlows,
    specialties: &'a NodeSpecialties,
    out: Lines,
}

impl Builder<'_> {
    fn supply_modifier(&self, market: usize, good: i32) -> i32 {
        let category = self.db.good(GoodId(good)).category_index as usize;
        self.flows.multiplier_index(market, category, SUPPLY) as i32
    }

    fn add_lines(&mut self, node: i32, lines: &[RecipeLine], is_input: bool) {
        for line in lines {
            let good = line.good.0;
            let found = find_nearest_market(self.nodes, self.markets, node, good);
            self.out.cell.push(found.map_or(-1, |(_, c)| c as i32));
            self.out.good.push(good);
            let modifier = match found {
                Some((market, _)) if !is_input => self.supply_modifier(market, good),
                _ => -1,
            };
            self.out.modifier.push(modifier);
            self.out.quantity.push(line.quantity);
            self.out.is_input.push(u8::from(is_input));
            let specialty =
                !is_input && self.specialties.is_specialty(node, line.good.base_index());
            self.out.output_mul.push(if specialty {
                self.db.config.specialty_output_multiplier.raw()
            } else {
                Fx::ONE_RAW
            });
            self.add_upgrade(node, line.good, specialty);
        }
    }

    fn add_upgrade(&mut self, node: i32, good: GoodId, specialty: bool) {
        let (mut up_good, mut up_cell, mut up_modifier) = (-1, -1, -1);
        if specialty && good.grade() < QualityGrade::Best {
            let grade = QualityGrade::from_u8(good.grade() as u8 + 1).expect("a higher grade");
            let higher = GoodId::from(good.base_index(), grade);
            if let Some((market, cell)) =
                find_nearest_market(self.nodes, self.markets, node, higher.0)
            {
                up_good = higher.0;
                up_cell = cell as i32;
                up_modifier = self.supply_modifier(market, higher.0);
            }
        }
        self.out.up_good.push(up_good);
        self.out.up_cell.push(up_cell);
        self.out.up_modifier.push(up_modifier);
    }
}

/// Builds the lines of every facility.
pub fn build_lines(
    db: &GameDatabase,
    nodes: &NodeTable,
    markets: &MarketStore,
    flows: &MarketFlows,
    specialties: &NodeSpecialties,
    facilities: &FacilityTable,
    development: &DevelopmentState,
) -> Lines {
    let mut b = Builder {
        db,
        nodes,
        markets,
        flows,
        specialties,
        out: Lines::default(),
    };
    for f in 0..facilities.count() {
        b.out.start.push(b.out.cell.len());
        let node = facilities.node[f];
        let def = &db.facilities[facilities.def_index[f] as usize];
        let recipe = &db.recipes[def.recipe];
        b.add_lines(node, &recipe.inputs, true);
        let first_aid = b.out.is_input.len();
        b.add_lines(node, &recipe.aids, true);
        for k in first_aid..b.out.is_input.len() {
            b.out.is_input[k] = AID_LINE;
        }
        let first_output = b.out.output_mul.len();
        b.add_lines(node, &recipe.outputs, false);
        // The state's land development raises primary output on that land (DEC-072).
        let land = if def.tier == IndustryTier::Primary {
            development.land_level[node as usize]
        } else {
            0
        };
        if land == 0 {
            continue;
        }
        let bonus = Fx::ONE + db.config.state_land_bonus * Fx::from_int(i64::from(land));
        for k in first_output..b.out.output_mul.len() {
            b.out.output_mul[k] = (Fx::from_raw(b.out.output_mul[k]) * bonus).raw();
        }
    }
    b.out.start.push(b.out.cell.len());
    b.out
}
