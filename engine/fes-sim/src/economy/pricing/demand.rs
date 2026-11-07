//! Household demand per market cell (C# `DemandParams`, `WeeklyWish`, `HouseholdDemand`,
//! `DemandCurve`; SYS-MARKETS, DEC-048 to DEC-050, DEC-126). Integers only.
use crate::economy::household_cash::QUINTILES;
use fes_core::Fx;
use fes_data::SimulationConfig;

const FB: u32 = Fx::FRACTION_BITS;
const Q30: u32 = 30;

/// Household demand constants in raw Q16.
#[derive(Clone, Copy, Debug)]
pub struct DemandParams {
    pub luxury_elasticity_raw: i64,
    pub max_cheap_raw: i64,
    /// DEC-126: price / anchor from which households buy no necessities.
    pub necessity_choke_raw: i64,
    /// Per income quintile, poorest first: the necessity elasticity times the quintile's sensitivity.
    pub quintile_raw: [i64; QUINTILES],
}

impl DemandParams {
    pub fn from_config(c: &SimulationConfig) -> Self {
        let mut quintile_raw = [0i64; QUINTILES];
        for (q, slot) in quintile_raw.iter_mut().enumerate() {
            *slot = quintile_elasticity(c, q);
        }
        Self {
            luxury_elasticity_raw: c.luxury_elasticity.raw(),
            max_cheap_raw: c.max_cheap_demand.raw(),
            necessity_choke_raw: c.necessity_choke.raw(),
            quintile_raw,
        }
    }
}

/// Each quintile's sensitivity is taken against their mean, so demand as a whole moves by the
/// stated elasticity.
fn quintile_elasticity(c: &SimulationConfig, q: usize) -> i64 {
    let sum: i64 = (0..QUINTILES)
        .map(|k| quintile_sensitivity(c.quintile_income_share[k]).raw())
        .sum();
    let relative = (quintile_sensitivity(c.quintile_income_share[q]).raw() << FB)
        * QUINTILES as i64
        / sum.max(1);
    Fx::round_shift(c.necessity_elasticity.raw().wrapping_mul(relative))
}

/// What a cell's households want in a week: all units, necessity units at the anchor price,
/// necessity units at this price.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct WeeklyWish {
    pub units: i64,
    pub wanted: i64,
    pub met: i64,
}

/// C# `HouseholdDemand.CellBase`: the base weekly demand of a cell and its anchor price.
pub fn cell_base(
    base_demand: i32,
    base_price: i32,
    level_weight: i64,
    demand_mul: i64,
    value_mul: i64,
) -> (i64, i64) {
    let anchor = Fx::round_shift(i64::from(base_price).wrapping_mul(value_mul));
    let weekly = Fx::round_shift(
        Fx::round_shift(i64::from(base_demand).wrapping_mul(level_weight)).wrapping_mul(demand_mul),
    );
    (weekly, anchor)
}

/// C# `HouseholdDemand.Weekly`.
pub fn weekly(
    p: &DemandParams,
    weekly_at_anchor: i64,
    price: i32,
    anchor: i64,
    luxury_share: i64,
    necessity_weight: i64,
    luxury_weight: i64,
) -> WeeklyWish {
    if weekly_at_anchor == 0 {
        return WeeklyWish::default();
    }
    let anchor = anchor.max(1);
    let excess = ((i64::from(price) - anchor) << FB) / anchor;
    let ratio = (i64::from(price) << FB) / anchor;
    let necessity_share = Fx::ONE_RAW - luxury_share;
    let mut necessity = 0;
    if necessity_share > 0 {
        let choke = p.necessity_choke_raw;
        let log = log_of_ratio(ratio, choke);
        let sum: i64 = p
            .quintile_raw
            .iter()
            .map(|&e| constant_elasticity(e, ratio, log, p.max_cheap_raw, choke))
            .sum();
        necessity = sum / QUINTILES as i64;
    }
    let luxury = if luxury_share > 0 {
        factor(p.luxury_elasticity_raw, excess, p.max_cheap_raw)
    } else {
        0
    };
    let necessity_at_anchor = Fx::round_shift(necessity_share.wrapping_mul(necessity_weight));
    let necessity_part = Fx::round_shift(necessity_at_anchor.wrapping_mul(necessity));
    let luxury_part = Fx::round_shift(
        Fx::round_shift(luxury_share.wrapping_mul(luxury_weight)).wrapping_mul(luxury),
    );
    WeeklyWish {
        wanted: Fx::round_shift(weekly_at_anchor.wrapping_mul(necessity_at_anchor)),
        met: Fx::round_shift(weekly_at_anchor.wrapping_mul(necessity_part)),
        units: Fx::round_shift(weekly_at_anchor.wrapping_mul(necessity_part + luxury_part)),
    }
}

/// C# `DemandCurve.Factor`: linear demand relative to the anchor's, clamped to `0..=max`.
pub fn factor(elasticity_raw: i64, excess_raw: i64, max_raw: i64) -> i64 {
    let f = Fx::ONE_RAW - Fx::round_shift(elasticity_raw.wrapping_mul(excess_raw));
    if f < 0 {
        return 0;
    }
    f.min(max_raw)
}

/// C# `DemandCurve.ConstantElasticity` with a precomputed log (DEC-126).
pub fn constant_elasticity(
    elasticity_raw: i64,
    ratio_raw: i64,
    log_raw: i64,
    max_raw: i64,
    choke_raw: i64,
) -> i64 {
    if ratio_raw >= choke_raw {
        return 0;
    }
    if ratio_raw <= 0 {
        return max_raw;
    }
    exp2(-Fx::round_shift(elasticity_raw.wrapping_mul(log_raw))).min(max_raw)
}

/// log2 of the price ratio where the curve needs it (between nothing and the choke), else zero.
pub fn log_of_ratio(ratio_raw: i64, choke_raw: i64) -> i64 {
    if ratio_raw > 0 && ratio_raw < choke_raw {
        log2(ratio_raw)
    } else {
        0
    }
}

/// log2 of a positive raw Fx, as raw Fx: the whole part from the top bit, the fraction by
/// repeated squaring.
fn log2(x_raw: i64) -> i64 {
    let mut top: i32 = 0;
    let mut v = x_raw;
    while v > 1 {
        v >>= 1;
        top += 1;
    }
    let mut result = i64::from(top - FB as i32) << FB;
    let mut y = if top >= Q30 as i32 {
        x_raw >> (top - Q30 as i32)
    } else {
        x_raw << (Q30 as i32 - top)
    };
    for bit in (0..FB).rev() {
        y = y.wrapping_mul(y) >> Q30;
        if y < 2i64 << Q30 {
            continue;
        }
        y >>= 1;
        result += 1i64 << bit;
    }
    result
}

/// 2^(2^-i) in Q30.
const fn root_of_two(i: u32) -> i64 {
    match i {
        1 => 1518500250,
        2 => 1276901417,
        3 => 1170923762,
        4 => 1121280436,
        5 => 1097253708,
        6 => 1085434106,
        7 => 1079572136,
        8 => 1076653033,
        9 => 1075196443,
        10 => 1074468888,
        11 => 1074105294,
        12 => 1073923544,
        13 => 1073832680,
        14 => 1073787251,
        15 => 1073764537,
        _ => 1073753181,
    }
}

/// For each value h of a fraction's top eight bits, the product of 2^(2^-i) over its set bits
/// i = 1..8, in Q30, multiplied in that order from 2^30 (C# `HighRoots`, generated the same way).
const HIGH_ROOTS: [i64; 256] = high_roots();

const fn high_roots() -> [i64; 256] {
    let mut out = [0i64; 256];
    let mut h = 0;
    while h < 256 {
        let mut product = 1i64 << Q30;
        let mut i = 1;
        while i <= 8 {
            if h & (1 << (8 - i)) != 0 {
                product = (product * root_of_two(i)) >> Q30;
            }
            i += 1;
        }
        out[h] = product;
        h += 1;
    }
    out
}

/// 2 to a raw Fx power, as raw Fx: the fraction from the products of 2^(2^-i), the whole part as
/// a shift.
fn exp2(y_raw: i64) -> i64 {
    let whole = y_raw >> FB;
    let fraction = y_raw - (whole << FB);
    let mut product = HIGH_ROOTS[(fraction >> (FB - 8)) as usize];
    for i in 9..=FB {
        if fraction & (1i64 << (FB - i)) != 0 {
            product = product.wrapping_mul(root_of_two(i)) >> Q30;
        }
    }
    let shift = Q30 as i32 - FB as i32 - whole as i32;
    if shift >= 62 {
        return 0;
    }
    if shift <= 0 {
        return i64::MAX >> 1;
    }
    (product + (1i64 << (shift - 1))) >> shift
}

/// C# `DemandCurve.QuintileSensitivity`: the square root of one fifth over the quintile's income
/// share, so the poor give up necessities first.
pub fn quintile_sensitivity(income_share: Fx) -> Fx {
    sqrt(Fx::from_raw(
        Fx::from_permille(200).raw() * Fx::ONE_RAW / income_share.raw().max(1),
    ))
}

fn sqrt(value: Fx) -> Fx {
    let mut target = (value.raw().max(0) as u64) << FB;
    let mut root = 0u64;
    let mut bit = 1u64 << 62;
    while bit > target {
        bit >>= 2;
    }
    while bit != 0 {
        if target >= root + bit {
            target -= root + bit;
            root = (root >> 1) + bit;
        } else {
            root >>= 1;
        }
        bit >>= 2;
    }
    Fx::from_raw(root as i64)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn high_roots_match_the_csharp_table() {
        assert_eq!(HIGH_ROOTS[0], 1073741824);
        assert_eq!(HIGH_ROOTS[1], 1076653033);
        assert_eq!(HIGH_ROOTS[7], 1094286794);
        assert_eq!(HIGH_ROOTS[100], 1407633882);
        assert_eq!(HIGH_ROOTS[128], 1518500250);
        assert_eq!(HIGH_ROOTS[255], 2141676967);
    }
}
