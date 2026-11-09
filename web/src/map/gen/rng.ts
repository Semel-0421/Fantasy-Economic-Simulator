// Seeded randomness for map generation: the same seed string always gives the same numbers.
// Map generation is UI decoration and never touches the simulation's own random stream.

/** FNV-1a 32-bit hash of a string. */
export function fnv1a(text: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

export interface Rng {
  /** Uniform in [0, 1). */
  next(): number;
  /** Integer in [0, n). */
  int(n: number): number;
  /** Uniform in [a, b). */
  range(a: number, b: number): number;
}

/** sfc32 seeded from four FNV-1a hashes of the seed. */
export function rng(seed: string): Rng {
  let a = fnv1a(`${seed}#a`), b = fnv1a(`${seed}#b`), c = fnv1a(`${seed}#c`), d = fnv1a(`${seed}#d`);
  const next = (): number => {
    a >>>= 0; b >>>= 0; c >>>= 0; d >>>= 0;
    const t = (a + b | 0) + d | 0;
    d = d + 1 | 0;
    a = b ^ b >>> 9;
    b = c + (c << 3) | 0;
    c = c << 21 | c >>> 11;
    c = c + t | 0;
    return (t >>> 0) / 4294967296;
  };
  for (let i = 0; i < 12; i++) next();
  return { next, int: (n) => Math.floor(next() * n), range: (lo, hi) => lo + (hi - lo) * next() };
}

/**
 * Smooth value noise in [-1, 1] on a lattice hashed from the seed. With `period` (lattice cells),
 * the noise repeats every `period` units in x, so it tiles seamlessly round a cylinder.
 */
export function valueNoise(seed: number, period = 0): (x: number, y: number) => number {
  const at = (ix: number, iy: number): number => {
    if (period > 0) ix = (ix % period + period) % period;
    let h = Math.imul(ix, 374761393) ^ Math.imul(iy, 668265263) ^ seed;
    h = Math.imul(h ^ h >>> 13, 1274126177);
    return ((h ^ h >>> 16) >>> 0) / 2147483648 - 1;
  };
  const fade = (t: number): number => t * t * (3 - 2 * t);
  return (x, y) => {
    const ix = Math.floor(x), iy = Math.floor(y), fx = fade(x - ix), fy = fade(y - iy);
    const top = at(ix, iy) + (at(ix + 1, iy) - at(ix, iy)) * fx;
    const bottom = at(ix, iy + 1) + (at(ix + 1, iy + 1) - at(ix, iy + 1)) * fx;
    return top + (bottom - top) * fy;
  };
}

/** Fractal (fBm) sum of value noise octaves, roughly in [-1, 1]; `period` as in valueNoise. */
export function fbm(seed: number, octaves = 5, period = 0): (x: number, y: number) => number {
  const layers = Array.from({ length: octaves }, (_, i) => valueNoise(seed + i * 1013, period * 2 ** i));
  return (x, y) => {
    let sum = 0, amp = 0.5, freq = 1, norm = 0;
    for (const layer of layers) { sum += amp * layer(x * freq, y * freq); norm += amp; amp *= 0.5; freq *= 2; }
    return sum / norm;
  };
}

/**
 * Noise in pixel coordinates at roughly `scale` pixels per lattice cell that tiles every `width`
 * pixels in x (the scale is nudged so a whole number of cells fits round the cylinder).
 */
export function wrappedFbm(seed: number, octaves: number, scale: number, width: number): (x: number, y: number) => number {
  const cells = Math.max(1, Math.round(width / scale)), s = width / cells, f = fbm(seed, octaves, cells);
  return (x, y) => f(x / s, y / s);
}
