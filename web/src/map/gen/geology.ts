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

/** Grows `count` plates from scattered seeds with uneven, random growth so borders look natural. */
export function growPlates(grid: Grid, count: number, random: Rng): Plates {
  const plate = new Int32Array(grid.size).fill(-1), seeds: number[] = [];
  const margin = Math.min(3, Math.floor(grid.rows / 4));
  let spacing = 140;
  while (seeds.length < count) {
    const col = random.int(grid.cols), row = margin + random.int(grid.rows - 2 * margin);
    const i = row * grid.cols + col;
    if (seeds.every((s) => cellDistance(grid, s, i) > spacing)) seeds.push(i); else spacing *= 0.995;
  }
  const frontier: number[][] = seeds.map((s, p) => { plate[s] = p; return [s]; });
  const rate = seeds.map(() => random.range(0.6, 1.4));
  let open = seeds.length;
  while (open > 0) {
    // Pick a plate in proportion to its growth rate, then grow from a random frontier cell.
    let total = 0;
    for (let p = 0; p < count; p++) if (frontier[p]!.length) total += rate[p]!;
    let pick = random.next() * total, p = 0;
    for (; p < count; p++) if (frontier[p]!.length && (pick -= rate[p]!) <= 0) break;
    p = Math.min(p, count - 1);
    const list = frontier[p]!;
    if (!list.length) { open = frontier.filter((f) => f.length).length; continue; }
    const k = random.int(list.length), cell = list[k]!;
    let grew = false;
    for (let n = 0; n < 6; n++) {
      const j = grid.nb[cell * 6 + n]!;
      if (j >= 0 && plate[j] === -1) { plate[j] = p; list.push(j); grew = true; break; }
    }
    if (!grew) { list[k] = list[list.length - 1]!; list.pop(); if (!list.length) open = frontier.filter((f) => f.length).length; }
  }
  const vx: number[] = [], vy: number[] = [];
  for (let p = 0; p < count; p++) { const a = random.range(0, 2 * Math.PI), s = random.range(0.4, 1); vx.push(Math.cos(a) * s); vy.push(Math.sin(a) * s); }
  return { plate, vx, vy };
}

/**
 * Continental crust: one blob per continent, grown from far-apart seeds with uneven random growth
 * (ragged, fractal-like coasts) and never closer than `gap` hexes to another blob, so the
 * continents stay separate landmasses with open sea between them.
 */
export function growContinents(grid: Grid, targets: number[], random: Rng, gap = 3): Int32Array {
  const owner = new Int32Array(grid.size).fill(-1), margin = Math.min(6, Math.floor(grid.rows / 4)), seeds: number[] = [];
  // A low-frequency field makes some directions grow faster: peninsulas, bays, irregular shapes.
  const shape = wrappedFbm(random.int(1 << 30), 3, 90, grid.width), drift = wrappedFbm(random.int(1 << 30), 3, 160, grid.width);
  // Only the poles are edges: east and west wrap round.
  const edgeOk = (i: number): boolean => { const row = Math.floor(i / grid.cols); return Math.min(row, grid.rows - 1 - row) >= 3 + 3 * (0.5 + 0.5 * shape(grid.x[i]!, grid.y[i]!)); };
  const interior = (i: number): boolean => { const row = Math.floor(i / grid.cols); return row >= margin && row < grid.rows - margin; };
  let first = random.int(grid.size);
  while (!interior(first)) first = random.int(grid.size);
  seeds.push(first);
  while (seeds.length < targets.length) {
    let best = -1, bestD = -1;
    for (let k = 0; k < 400; k++) {
      const c = random.int(grid.size);
      if (!interior(c)) continue;
      const d = Math.min(...seeds.map((s) => cellDistance(grid, s, c)));
      if (d > bestD) { bestD = d; best = c; }
    }
    seeds.push(best);
  }
  const near = (cell: number, b: number): boolean => {
    const seen = new Set([cell]); let ring = [cell];
    for (let d = 0; d < gap; d++) {
      const next: number[] = [];
      for (const c of ring) for (let n = 0; n < 6; n++) {
        const j = grid.nb[c * 6 + n]!;
        if (j < 0 || seen.has(j)) continue;
        if (owner[j]! >= 0 && owner[j] !== b) return true;
        seen.add(j); next.push(j);
      }
      ring = next;
    }
    return false;
  };
  const frontier = seeds.map((s, b) => { owner[s] = b; return [s]; }), size = seeds.map(() => 1);
  const rate = seeds.map(() => random.range(0.7, 1.3));
  for (let guard = 0; guard < grid.size * 40; guard++) {
    const open = [...frontier.keys()].filter((b) => frontier[b]!.length && size[b]! < targets[b]!);
    if (!open.length) break;
    let pick = random.next() * open.reduce((a, b) => a + rate[b]!, 0), b = open[0]!;
    for (const o of open) if ((pick -= rate[o]!) <= 0) { b = o; break; }
    const list = frontier[b]!, k = random.int(list.length), cell = list[k]!;
    const options: number[] = [];
    for (let n = 0; n < 6; n++) { const j = grid.nb[cell * 6 + n]!; if (j >= 0 && owner[j] === -1 && edgeOk(j) && !near(j, b)) options.push(j); }
    if (!options.length) { list[k] = list[list.length - 1]!; list.pop(); continue; }
    const j = options[random.int(options.length)]!;
    // Uneven acceptance from the shape field: growth stalls in some directions and races in others.
    if (random.next() > 0.45 + 0.55 * (0.5 + 0.5 * drift(grid.x[j]!, grid.y[j]!))) continue;
    owner[j] = b; size[b]!++; list.push(j);
  }
  return owner;
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

/** Raw elevation (sea level is chosen later): crust, boundary uplift, noise, hotspots, edge falloff. */
export function elevation(grid: Grid, plates: Plates, crust: Int32Array, random: Rng): Float32Array {
  const { plate, vx, vy } = plates, h = new Float32Array(grid.size), stress = new Float32Array(grid.size);
  const boundary: number[] = [];
  for (let i = 0; i < grid.size; i++) {
    for (let n = 0; n < 6; n++) {
      const j = grid.nb[i * 6 + n]!;
      if (j < 0 || plate[j] === plate[i]) continue;
      const a = plate[i]!, b = plate[j]!, dx = wrapDx(grid.x[j]! - grid.x[i]!, grid.width), dy = grid.y[j]! - grid.y[i]!, len = Math.hypot(dx, dy);
      const conv = ((vx[a]! - vx[b]!) * dx + (vy[a]! - vy[b]!) * dy) / len;
      const ca = crust[i]! >= 0, cb = crust[j]! >= 0;
      let s: number;
      if (conv > 0) s = ca && cb ? 1.0 * conv : ca ? 0.65 * conv : cb ? -0.3 * conv : 0.3 * conv;
      else s = ca ? 0.3 * conv : 0.12 * -conv;
      if (Math.abs(s) > Math.abs(stress[i]!)) stress[i] = s;
      boundary.push(i);
    }
  }
  // Spread boundary stress inland with an exponential falloff (mountain belts are a few hexes wide).
  const spread = new Float32Array(grid.size), dist = distanceFrom(grid, boundary, 6), near = new Int32Array(grid.size).fill(-1);
  const queue = boundary.slice();
  for (const b of boundary) near[b] = b;
  for (let head = 0; head < queue.length; head++) {
    const c = queue[head]!;
    for (let n = 0; n < 6; n++) { const j = grid.nb[c * 6 + n]!; if (j >= 0 && near[j] === -1 && dist[j]! <= 6) { near[j] = near[c]!; queue.push(j); } }
  }
  for (let i = 0; i < grid.size; i++) if (near[i]! >= 0 && plate[near[i]!] === plate[i]) spread[i] = 1.35 * stress[near[i]!]! * Math.exp(-dist[i]! / 1.25);
  const coast: number[] = [];
  for (let i = 0; i < grid.size; i++) if (crust[i]! >= 0) for (let n = 0; n < 6; n++) { const j = grid.nb[i * 6 + n]!; if (j >= 0 && crust[j]! < 0) { coast.push(i); break; } }
  const inland = distanceFrom(grid, coast, 12);
  const noise = wrappedFbm(random.int(1 << 30), 5, 190, grid.width), ridge = wrappedFbm(random.int(1 << 30), 4, 70, grid.width);
  const offshore = distanceFrom(grid, coast, 8);
  const hotspots = Array.from({ length: 1 + random.int(3) }, () => random.int(grid.size)).filter((c) => crust[c]! < 0 && offshore[c]! >= 6);
  for (let i = 0; i < grid.size; i++) {
    const x = grid.x[i]!, y = grid.y[i]!;
    const land = crust[i]! >= 0;
    let v = land ? 0.12 + 0.3 * Math.min(1, inland[i]! / 6) : -0.6;
    v += spread[i]!;
    // The deep ocean floor is calmer than continental crust (abyssal plains), so noise rarely
    // builds land bridges between continents.
    const roughness = land ? 1 : 0.45;
    v += roughness * (0.32 * noise(x, y) + 0.12 * Math.abs(ridge(x, y)));
    for (const s of hotspots) v += 0.75 * Math.exp(-(cellDistance(grid, i, s) ** 2) / 2500);
    // Keep the polar rims at sea; east and west wrap, so there is no edge there.
    const row = Math.floor(i / grid.cols);
    const edge = Math.min(row, grid.rows - 1 - row);
    if (edge < 5) v -= 0.9 * (1 - edge / 5) ** 2;
    h[i] = v;
  }
  return h;
}

/** Thermal erosion (slopes steeper than the talus angle slide down) and light smoothing. */
export function erode(grid: Grid, h: Float32Array, passes = 4): Float32Array {
  let cur = h.slice();
  for (let pass = 0; pass < passes; pass++) {
    const next = cur.slice();
    for (let i = 0; i < grid.size; i++) {
      let low = -1, drop = 0, sum = 0, count = 0;
      for (let n = 0; n < 6; n++) {
        const j = grid.nb[i * 6 + n]!;
        if (j < 0) continue;
        sum += cur[j]!; count++;
        if (cur[i]! - cur[j]! > drop) { drop = cur[i]! - cur[j]!; low = j; }
      }
      if (low >= 0 && drop > 0.14) { const move = 0.25 * (drop - 0.14); next[i] = next[i]! - move; next[low] = next[low]! + move; }
      next[i] = next[i]! * 0.85 + (count ? sum / count : cur[i]!) * 0.15;
    }
    cur = next;
  }
  return cur;
}
