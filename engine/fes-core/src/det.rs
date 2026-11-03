//! Deterministic hashing and random draws, bit-identical to the C# `DetHash` / `DetRandom`.

use crate::Fx;

pub mod det_hash {
    pub const FNV_OFFSET: u64 = 14695981039346656037;
    pub const FNV_PRIME: u64 = 1099511628211;
    pub const GAMMA: u64 = 0x9E37_79B9_7F4A_7C15;

    /// SplitMix64 step: advance by the golden-ratio gamma, then finalize.
    pub fn mix(x: u64) -> u64 {
        let mut x = x.wrapping_add(GAMMA);
        x = (x ^ (x >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        x = (x ^ (x >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        x ^ (x >> 31)
    }

    pub fn combine(a: u64, b: u64) -> u64 {
        mix(a ^ mix(b))
    }

    pub fn hash(seed: u64, turn: i32, stream: u32, entity: i32, salt: u32) -> u64 {
        let mut h = mix(seed);
        h = combine(h, u64::from(turn as u32));
        h = combine(h, u64::from(stream));
        h = combine(h, u64::from(entity as u32));
        combine(h, u64::from(salt))
    }

    pub fn fnv_step(hash: u64, value: u8) -> u64 {
        (hash ^ u64::from(value)).wrapping_mul(FNV_PRIME)
    }

    /// Stable 32-bit id for a string: FNV-1a over UTF-16 code units, like C# `char`.
    pub fn of(text: &str) -> u32 {
        let mut h = FNV_OFFSET;
        for c in text.encode_utf16() {
            h = (h ^ u64::from(c)).wrapping_mul(FNV_PRIME);
        }
        (h ^ (h >> 32)) as u32
    }
}

/// Small deterministic generator for a burst of draws from one keyed seed.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct DetRandom {
    state: u64,
}

impl DetRandom {
    pub fn new(seed: u64) -> Self {
        Self { state: seed }
    }

    pub fn for_key(world_seed: u64, turn: i32, stream: u32, entity: i32, salt: u32) -> Self {
        Self::new(det_hash::hash(world_seed, turn, stream, entity, salt))
    }

    pub fn next_u64(&mut self) -> u64 {
        let z = det_hash::mix(self.state);
        self.state = self.state.wrapping_add(det_hash::GAMMA);
        z
    }

    pub fn next_u32(&mut self) -> u32 {
        (self.next_u64() >> 32) as u32
    }

    /// Uniform integer in `[0, max_exclusive)`; 0 when `max_exclusive <= 0`.
    pub fn next_int(&mut self, max_exclusive: i32) -> i32 {
        if max_exclusive <= 0 {
            return 0;
        }
        ((u64::from(self.next_u32()) * max_exclusive as u64) >> 32) as i32
    }

    pub fn next_int_range(&mut self, min_inclusive: i32, max_exclusive: i32) -> i32 {
        min_inclusive.wrapping_add(self.next_int(max_exclusive.wrapping_sub(min_inclusive)))
    }

    /// Uniform fixed-point value in `[0, 1)`.
    pub fn next_fx01(&mut self) -> Fx {
        Fx::from_raw(i64::from(self.next_u32() >> (32 - Fx::FRACTION_BITS)))
    }

    pub fn chance_permille(&mut self, permille: i32) -> bool {
        self.next_int(1000) < permille
    }

    /// Uniform multiplier in `[1 - spread, 1 + spread]`, spread in permille.
    pub fn next_spread(&mut self, spread_permille: i32) -> Fx {
        let draw = self.next_int(spread_permille.wrapping_mul(2).wrapping_add(1));
        Fx::from_permille(i64::from(
            1000i32.wrapping_sub(spread_permille).wrapping_add(draw),
        ))
    }
}
