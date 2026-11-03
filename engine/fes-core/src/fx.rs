//! Q47.16 fixed point, bit-identical to the C# `Fx` (unchecked 64-bit wrapping arithmetic).
use std::ops::{Add, Div, Mul, Neg, Sub};

/// Q47.16 fixed-point number holding its raw `i64`.
#[derive(Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Debug, Default)]
pub struct Fx(i64);

const HALF_RAW: i64 = Fx::ONE_RAW / 2;
const MAX_SHIFTABLE: i64 = i64::MAX >> Fx::FRACTION_BITS;
const SAFE_FACTOR: u64 = i32::MAX as u64;

/// Narrows a wide result to `i64`; C# threw `OverflowException` here, so this panics.
fn narrow(v: i128) -> i64 {
    i64::try_from(v).expect("Fx overflow (C# OverflowException)")
}

fn is_wide(a: i64, b: i64) -> bool {
    a.unsigned_abs() > SAFE_FACTOR || b.unsigned_abs() > SAFE_FACTOR
}

impl Fx {
    pub const FRACTION_BITS: u32 = 16;
    pub const ONE_RAW: i64 = 1 << Self::FRACTION_BITS;
    pub const ZERO: Fx = Fx(0);
    pub const ONE: Fx = Fx(Self::ONE_RAW);

    pub const fn from_raw(raw: i64) -> Fx {
        Fx(raw)
    }

    pub const fn raw(self) -> i64 {
        self.0
    }

    pub const fn from_int(value: i64) -> Fx {
        Fx(value.wrapping_shl(Self::FRACTION_BITS))
    }

    /// permille / 1000, rounded half away from zero to the nearest raw step.
    pub fn from_permille(permille: i64) -> Fx {
        let scaled = permille.wrapping_shl(Self::FRACTION_BITS);
        Fx(if scaled >= 0 {
            scaled.wrapping_add(500) / 1000
        } else {
            scaled.wrapping_sub(500) / 1000
        })
    }

    /// numerator / denominator truncated toward zero; zero when the denominator is zero.
    pub fn from_ratio(numerator: i64, denominator: i64) -> Fx {
        if denominator == 0 {
            return Fx::ZERO;
        }
        if !(-MAX_SHIFTABLE..=MAX_SHIFTABLE).contains(&numerator) {
            let q = i128::from(numerator) * i128::from(Self::ONE_RAW) / i128::from(denominator);
            return Fx(narrow(q));
        }
        Fx((numerator << Self::FRACTION_BITS) / denominator)
    }

    /// `value * self`, rounded half away from zero.
    pub fn mul_int(self, value: i64) -> i64 {
        let raw = self.0;
        if is_wide(value, raw) {
            let p = i128::from(value) * i128::from(raw);
            let half = i128::from(HALF_RAW);
            let r = if p >= 0 {
                (p + half) >> 16
            } else {
                -((-p + half) >> 16)
            };
            return narrow(r);
        }
        Self::round_shift(value * raw)
    }

    /// Like `mul_int` on a raw; saturates beyond 2^31 instead of widening.
    pub fn mul_int_raw(value: i64, raw: i64) -> i64 {
        if value == 0 || raw == 0 {
            return 0;
        }
        if is_wide(value, raw) {
            return if (value < 0) != (raw < 0) {
                i64::MIN >> Self::FRACTION_BITS
            } else {
                i64::MAX >> Self::FRACTION_BITS
            };
        }
        Self::round_shift(value * raw)
    }

    /// Converts a Q16 product to an integer, rounding half away from zero.
    pub fn round_shift(product: i64) -> i64 {
        if product >= 0 {
            product.wrapping_add(HALF_RAW) >> Self::FRACTION_BITS
        } else {
            -(product.wrapping_neg().wrapping_add(HALF_RAW) >> Self::FRACTION_BITS)
        }
    }

    pub fn min(a: Fx, b: Fx) -> Fx {
        if a.0 <= b.0 {
            a
        } else {
            b
        }
    }

    pub fn max(a: Fx, b: Fx) -> Fx {
        if a.0 >= b.0 {
            a
        } else {
            b
        }
    }

    pub fn clamp(value: Fx, min: Fx, max: Fx) -> Fx {
        Fx::max(min, Fx::min(max, value))
    }
}

impl Add for Fx {
    type Output = Fx;
    fn add(self, b: Fx) -> Fx {
        Fx(self.0.wrapping_add(b.0))
    }
}

impl Sub for Fx {
    type Output = Fx;
    fn sub(self, b: Fx) -> Fx {
        Fx(self.0.wrapping_sub(b.0))
    }
}

impl Neg for Fx {
    type Output = Fx;
    fn neg(self) -> Fx {
        Fx(self.0.wrapping_neg())
    }
}

#[allow(clippy::suspicious_arithmetic_impl)] // fixed-point shift is intended
impl Mul for Fx {
    type Output = Fx;
    fn mul(self, b: Fx) -> Fx {
        Fx(self.0.wrapping_mul(b.0) >> Self::FRACTION_BITS)
    }
}

#[allow(clippy::suspicious_arithmetic_impl)] // fixed-point shift is intended
impl Div for Fx {
    type Output = Fx;
    fn div(self, b: Fx) -> Fx {
        if b.0 == 0 {
            return Fx::ZERO;
        }
        Fx(self.0.wrapping_shl(Self::FRACTION_BITS) / b.0)
    }
}
