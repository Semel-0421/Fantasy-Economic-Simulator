// Tectonic plates, elevation from plate boundaries plus fractal noise, and erosion.
// The model follows the usual procedural-geology order (plates -> uplift -> noise -> erosion):
// converging continents raise high ranges, ocean under continent raises coastal ranges with a
// trench offshore, converging oceans raise island arcs, diverging plates open rifts and ridges.
import { wrappedFbm, type Rng } from './rng';
import { cellDistance, wrapDx, type Grid } from './grid';

export interface Plates {
  /** Plate id per cell. */
  plate: Int32Array;
  /** Drift per plate (pixels per step, unit-ish). */
  vx: number[];
  vy: number[];
}

function plateSeeds(grid: Grid, count: number, random: Rng): number[] {
  const seeds: number[] = [];
  const margin = Math.min(3, Math.floor(grid.rows / 4));
  let spacing = 140;
  while (seeds.length < count) {
    const col = random.int(grid.cols), row = margin + random.int(grid.rows - 2 * margin);
    const i = row * grid.cols + col;
    if (seeds.every((s) => cellDistance(grid, s, i) > spacing)) seeds.push(i); else spacing *= 0.995;
  }
  return seeds;
}

/** Picks a plate in proportion to its growth rate among those with a frontier. */
function pickPlate(frontier: number[][], rate: number[], count: number, random: Rng): number {
  let total = 0;
  for (let p = 0; p < count; p++) if (frontier[p]!.length) total += rate[p]!;
  let pick = random.next() * total, p = 0;
  for (; p < count; p++) if (frontier[p]!.length && (pick -= rate[p]!) <= 0) break;
  return Math.min(p, count - 1);
}

/** Claims the first free neighbour of a cell for plate p; false when every neighbour is taken. */
function claimNeighbour(grid: Grid, plate: Int32Array, list: number[], cell: number, p: number): boolean {
  for (let n = 0; n < 6; n++) {
    const j = grid.nb[cell * 6 + n]!;
    if (j >= 0 && plate[j] === -1) { plate[j] = p; list.push(j); return true; }
  }
  return false;
}

/** Grows count plates from scattered seeds with uneven, random growth so borders look natural. */
export function growPlates(grid: Grid, count: number, random: Rng): Plates {
  const plate = new Int32Array(grid.size).fill(-1);
  const seeds = plateSeeds(grid, count, random);
  const frontier: number[][] = seeds.map((s, p) => { plate[s] = p; return [s]; });
  const rate = seeds.map(() => random.range(0.6, 1.4));
  let open = seeds.length;
  while (open > 0) {
    // Pick a plate in proportion to its growth rate, then grow from a random frontier cell.
    const p = pickPlate(frontier, rate, count, random);
    const list = frontier[p]!;
    if (!list.length) { open = frontier.filter((f) => f.length).length; continue; }
    const k = random.int(list.length), cell = list[k]!;
    if (!claimNeighbour(grid, plate, list, cell, p)) {
      list[k] = list[list.length - 1]!; list.pop();
      if (!list.length) open = frontier.filter((f) => f.length).length;
    }
  }
  const vx: number[] = [], vy: number[] = [];
  for (let p = 0; p < count; p++) { const a = random.range(0, 2 * Math.PI), s = random.range(0.4, 1); vx.push(Math.cos(a) * s); vy.push(Math.sin(a) * s); }
  return { plate, vx, vy };
}

/** Breadth-first distance (in hexes) from every cell to the nearest cell in `sources`. */
export function distanceFrom(grid: Grid, sources: number[], limit = 1e9): Int32Array {
  const dist = new Int32Array(grid.size).fill(1e9), queue = new Int32Array(grid.size);
  let head = 0, tail = 0;
  for (const s of sources) { dist[s] = 0; queue[tail++] = s; }
  while (head < tail) {
    const c = queue[head++]!;
    if (dist[c]! >= limit) continue;
    for (let n = 0; n < 6; n++) { const j = grid.nb[c * 6 + n]!; if (j >= 0 && dist[j]! > dist[c]! + 1) { dist[j] = dist[c]! + 1; queue[tail++] = j; } }
  }
  return dist;
}

/** Stress a plate boundary puts on a cell, from the convergence speed and which sides hold continental crust. */
function boundaryStress(conv: number, ca: boolean, cb: boolean): number {
  if (conv > 0) return ca && cb ? 1.0 * conv : ca ? 0.65 * conv : cb ? -0.3 * conv : 0.3 * conv;
  return ca ? 0.3 * conv : 0.12 * -conv;
}

function plateStress(grid: Grid, plates: Plates, crust: Int32Array): { stress: Float32Array; boundary: number[] } {
  const { plate, vx, vy } = plates, stress = new Float32Array(grid.size);
  const boundary: number[] = [];
  for (let i = 0; i < grid.size; i++) {
    for (let n = 0; n < 6; n++) {
      const j = grid.nb[i * 6 + n]!;
      if (j < 0 || plate[j] === plate[i]) continue;
      const a = plate[i]!, b = plate[j]!, dx = wrapDx(grid.x[j]! - grid.x[i]!, grid.width), dy = grid.y[j]! - grid.y[i]!, len = Math.hypot(dx, dy);
      const conv = ((vx[a]! - vx[b]!) * dx + (vy[a]! - vy[b]!) * dy) / len;
      const s = boundaryStress(conv, crust[i]! >= 0, crust[j]! >= 0);
      if (Math.abs(s) > Math.abs(stress[i]!)) stress[i] = s;
      boundary.push(i);
    }
  }
  return { stress, boundary };
}

/** Spreads boundary stress inland with an exponential falloff (mountain belts are a few hexes wide). */
function spreadStress(grid: Grid, plate: Int32Array, stress: Float32Array, boundary: number[]): Float32Array {
  const spread = new Float32Array(grid.size), dist = distanceFrom(grid, boundary, 6), near = new Int32Array(grid.size).fill(-1);
  const queue = boundary.slice();
  for (const b of boundary) near[b] = b;
  for (let head = 0; head < queue.length; head++) {
    const c = queue[head]!;
    for (let n = 0; n < 6; n++) { const j = grid.nb[c * 6 + n]!; if (j >= 0 && near[j] === -1 && dist[j]! <= 6) { near[j] = near[c]!; queue.push(j); } }
  }
  for (let i = 0; i < grid.size; i++) if (near[i]! >= 0 && plate[near[i]!] === plate[i]) spread[i] = 1.35 * stress[near[i]!]! * Math.exp(-dist[i]! / 1.25);
  return spread;
}

function bordersOcean(grid: Grid, crust: Int32Array, i: number): boolean {
  for (let n = 0; n < 6; n++) { const j = grid.nb[i * 6 + n]!; if (j >= 0 && crust[j]! < 0) return true; }
  return false;
}

function crustCoast(grid: Grid, crust: Int32Array): number[] {
  const coast: number[] = [];
  for (let i = 0; i < grid.size; i++) if (crust[i]! >= 0 && bordersOcean(grid, crust, i)) coast.push(i);
  return coast;
}

interface HeightFields {
  grid: Grid;
  crust: Int32Array;
  inland: Int32Array;
  spread: Float32Array;
  noise: (x: number, y: number) => number;
  ridge: (x: number, y: number) => number;
  hotspots: number[];
  noiseStrength: number;
}

function cellElevation(f: HeightFields, i: number): number {
  const { grid, crust, inland, spread, noise, ridge, hotspots, noiseStrength } = f;
  const x = grid.x[i]!, y = grid.y[i]!;
  const land = crust[i]! >= 0;
  let v = land ? 0.12 + 0.3 * Math.min(1, inland[i]! / 6) : -0.6;
  v += spread[i]!;
  // The deep ocean floor is calmer than continental crust (abyssal plains), so noise rarely
  // builds land bridges between continents.
  const roughness = land ? 1 : 0.45;
  v += noiseStrength * roughness * (0.32 * noise(x, y) + 0.12 * Math.abs(ridge(x, y)));
  for (const s of hotspots) v += 0.75 * Math.exp(-(cellDistance(grid, i, s) ** 2) / 2500);
  // Keep the polar rims at sea; east and west wrap, so there is no edge there.
  const row = Math.floor(i / grid.cols);
  const edge = Math.min(row, grid.rows - 1 - row);
  if (edge < 5) v -= 0.9 * (1 - edge / 5) ** 2;
  return v;
}

/** Raw elevation (sea level is chosen later): crust, boundary uplift, noise, hotspots, edge falloff. */
export function elevation(grid: Grid, plates: Plates, crust: Int32Array, random: Rng, noiseStrength = 1): Float32Array {
  const h = new Float32Array(grid.size);
  const { stress, boundary } = plateStress(grid, plates, crust);
  const spread = spreadStress(grid, plates.plate, stress, boundary);
  const coast = crustCoast(grid, crust);
  const inland = distanceFrom(grid, coast, 12);
  const noise = wrappedFbm(random.int(1 << 30), 5, 190, grid.width), ridge = wrappedFbm(random.int(1 << 30), 4, 70, grid.width);
  const offshore = distanceFrom(grid, coast, 8);
  const hotspots = Array.from({ length: 1 + random.int(3) }, () => random.int(grid.size)).filter((c) => crust[c]! < 0 && offshore[c]! >= 6);
  const fields: HeightFields = { grid, crust, inland, spread, noise, ridge, hotspots, noiseStrength };
  for (let i = 0; i < grid.size; i++) h[i] = cellElevation(fields, i);
  return h;
}

/** One erosion step for cell i: slide material off an over-steep slope, then blend with the neighbours. */
function erodeCell(grid: Grid, cur: Float32Array, next: Float32Array, i: number, strength: number): void {
  let low = -1, drop = 0, sum = 0, count = 0;
  for (let n = 0; n < 6; n++) {
    const j = grid.nb[i * 6 + n]!;
    if (j < 0) continue;
    sum += cur[j]!; count++;
    if (cur[i]! - cur[j]! > drop) { drop = cur[i]! - cur[j]!; low = j; }
  }
  if (low >= 0 && drop > 0.14) { const move = strength * 0.25 * (drop - 0.14); next[i] = next[i]! - move; next[low] = next[low]! + move; }
  next[i] = next[i]! * (1 - strength * 0.15) + (count ? sum / count : cur[i]!) * strength * 0.15;
}

/** Thermal erosion (slopes steeper than the talus angle slide down) and light smoothing. */
export function erode(grid: Grid, h: Float32Array, passes = 4, strength = 1): Float32Array {
  let cur = h.slice();
  for (let pass = 0; pass < passes; pass++) {
    const next = cur.slice();
    for (let i = 0; i < grid.size; i++) erodeCell(grid, cur, next, i, strength);
    cur = next;
  }
  return cur;
}
