// Connected continental crust, shaped before elevation. Rifts retain pieces of one parent mask.
import { cellDistance, wrapDx, type Grid } from './grid';
import { distanceFrom, type Plates } from './geology';
import { wrappedFbm, type Rng } from './rng';
import type { GenOptions, WorldLayout } from './world';

class Frontier {
  private cells: [number, number][] = [];
  get size(): number { return this.cells.length; }
  push(cell: number, cost: number): void {
    const entry: [number, number] = [cell, cost];
    let k = this.cells.length;
    this.cells.push(entry);
    while (k > 0) {
      const p = (k - 1) >> 1;
      if (this.cells[p]![1] <= cost) break;
      this.cells[k] = this.cells[p]!; k = p;
    }
    this.cells[k] = entry;
  }
  pop(): number {
    const first = this.cells[0]!, last = this.cells.pop()!;
    if (this.cells.length) {
      let k = 0;
      while (2 * k + 1 < this.cells.length) {
        let child = 2 * k + 1;
        if (child + 1 < this.cells.length && this.cells[child + 1]![1] < this.cells[child]![1]) child++;
        if (last[1] <= this.cells[child]![1]) break;
        this.cells[k] = this.cells[child]!; k = child;
      }
      this.cells[k] = last;
    }
    return first[0];
  }
}

interface Rift {
  centre: number;
  radius: number;
  ca: number;
  sa: number;
}

/** Grows the parent landmass outward from the rift centre, cheapest cells first. */
function riftParent(grid: Grid, shape: Float32Array, area: number, rift: Rift): Uint8Array {
  const { centre, radius, ca, sa } = rift;
  const parent = new Uint8Array(grid.size), queued = new Uint8Array(grid.size), heap = new Frontier();
  const push = (i: number): void => {
    if (i < 0 || queued[i] || Math.floor(i / grid.cols) < 3 || Math.floor(i / grid.cols) >= grid.rows - 3) return;
    queued[i] = 1;
    const dx = wrapDx(grid.x[i]! - grid.x[centre]!, grid.width), dy = grid.y[i]! - grid.y[centre]!;
    heap.push(i, Math.hypot((dx * ca + dy * sa) / 1.25, (-dx * sa + dy * ca) * 1.25) / radius + 0.2 * shape[i]!);
  };
  push(centre);
  for (let n = 0; n < area && heap.size; n++) {
    const i = heap.pop(); parent[i] = 1;
    for (let k = 0; k < 6; k++) push(grid.nb[i * 6 + k]!);
  }
  return parent;
}

function plateAdjacency(grid: Grid, plates: Plates, count: number): Set<number>[] {
  const adjacent = Array.from({ length: count }, () => new Set<number>());
  for (let i = 0; i < grid.size; i++) for (let k = 0; k < 6; k++) {
    const j = grid.nb[i * 6 + k]!;
    if (j >= 0 && plates.plate[i] !== plates.plate[j]) adjacent[plates.plate[i]!]!.add(plates.plate[j]!);
  }
  return adjacent;
}

/** Assigns every plate to the nearest-linked group rooted at one of the plates holding most parent cells. */
function plateGroups(grid: Grid, plates: Plates, parent: Uint8Array, count: number): { roots: number[]; groups: Int32Array } {
  const counts = new Uint32Array(plates.vx.length);
  for (let i = 0; i < grid.size; i++) if (parent[i]) counts[plates.plate[i]!]!++;
  const roots = [...counts.keys()].sort((a, b) => counts[b]! - counts[a]! || a - b).slice(0, count);
  const groups = new Int32Array(counts.length).fill(-1), adjacent = plateAdjacency(grid, plates, counts.length);
  roots.forEach((p, b) => { groups[p] = b; });
  const queue = roots.slice();
  for (let head = 0; head < queue.length; head++) for (const p of adjacent[queue[head]!]!) if (groups[p] === -1) { groups[p] = groups[queue[head]!]!; queue.push(p); }
  return { roots, groups };
}

/** Group id of each cell after warping the plate map with two noise fields. */
function warpGroups(grid: Grid, plates: Plates, groups: Int32Array, random: Rng, roughness: number): Int32Array {
  const wx = wrappedFbm(random.int(1 << 30), 4, 150, grid.width), wy = wrappedFbm(random.int(1 << 30), 4, 130, grid.width);
  const warped = new Int32Array(grid.size);
  for (let i = 0; i < grid.size; i++) {
    const x = grid.x[i]!, y = grid.y[i]!;
    const row = Math.max(0, Math.min(grid.rows - 1, Math.round((y + roughness * 100 * wy(x, y)) / 24)));
    const col = Math.round((x + roughness * 100 * wx(x, y) - row % 2 * 14) / 28);
    const sample = row * grid.cols + ((col % grid.cols + grid.cols) % grid.cols);
    warped[i] = groups[plates.plate[sample]!]!;
  }
  return warped;
}

function groupBoundaries(grid: Grid, warped: Int32Array): number[] {
  const boundaries: number[] = [];
  for (let i = 0; i < grid.size; i++) for (let k = 0; k < 6; k++) {
    const j = grid.nb[i * 6 + k]!;
    if (j >= 0 && warped[i] !== warped[j]) { boundaries.push(i); break; }
  }
  return boundaries;
}

/** A small outward drift opens the cuts; both shores still derive from the same parent/fault. */
function driftGroup(grid: Grid, cells: number[], centre: number, random: Rng, mask: Uint8Array): void {
  const dx = cells.reduce((s, i) => s + wrapDx(grid.x[i]! - grid.x[centre]!, grid.width), 0) / Math.max(1, cells.length);
  const dy = cells.reduce((s, i) => s + grid.y[i]! - grid.y[centre]!, 0) / Math.max(1, cells.length);
  const len = Math.hypot(dx, dy) || 1, drift = random.range(1, 7);
  const dc = Math.round(drift * dx / len), dr = Math.round(drift * dy / len);
  for (const i of cells) {
    const row = Math.floor(i / grid.cols) + dr, col = ((i % grid.cols + dc) % grid.cols + grid.cols) % grid.cols;
    if (row >= 3 && row < grid.rows - 3) mask[row * grid.cols + col] = 1;
  }
}

function riftDomains(grid: Grid, targets: number[], plates: Plates, random: Rng, shape: Float32Array, roughness: number): Uint8Array[] {
  const centre = Math.floor(grid.rows * ((random.next() < 0.5 ? 0.34 : 0.66) + random.range(-0.03, 0.03))) * grid.cols + random.int(grid.cols);
  const area = Math.min(grid.size * 0.78, targets.reduce((a, b) => a + b, 0) * 1.6);
  const radius = Math.sqrt(area * 28 * 24 / Math.PI), angle = random.range(0, Math.PI);
  const parent = riftParent(grid, shape, area, { centre, radius, ca: Math.cos(angle), sa: Math.sin(angle) });
  const { roots, groups } = plateGroups(grid, plates, parent, targets.length);
  const warped = warpGroups(grid, plates, groups, random, roughness);
  const fault = distanceFrom(grid, groupBoundaries(grid, warped), 3);
  const masks = targets.map(() => Uint8Array.from(parent, (v) => v ? 2 : 0));
  roots.forEach((_, b) => {
    const cells = [...parent.keys()].filter((i) => parent[i] && warped[i] === b && fault[i]! >= 1);
    driftGroup(grid, cells, centre, random, masks[b]!);
  });
  return masks;
}

/** Everything the continent seeding and growth passes share. */
interface Crust {
  grid: Grid;
  targets: number[];
  random: Rng;
  options: GenOptions;
  layout: WorldLayout;
  gap: number;
  owner: Int32Array;
  exclusion: Int32Array;
  shape: Float32Array;
  detail: Float32Array;
  boundary: Int32Array;
  masks: Uint8Array[] | null;
  margin: number;
  radii: number[];
  centreX: number;
  north: boolean;
  centreRow: number;
  angles: number[];
  stretches: number[];
  pairs: number[][];
  seeds: number[];
}

function noiseFields(grid: Grid, random: Rng): { shape: Float32Array; detail: Float32Array } {
  const coarse = wrappedFbm(random.int(1 << 30), 3, 100, grid.width), fine = wrappedFbm(random.int(1 << 30), 3, 45, grid.width);
  const shape = Float32Array.from({ length: grid.size }, (_, i) => coarse(grid.x[i]!, grid.y[i]!));
  const detail = Float32Array.from({ length: grid.size }, (_, i) => fine(grid.x[i]!, grid.y[i]!));
  return { shape, detail };
}

/** Distance from cells where neighbouring plates move toward each other. */
function convergentBoundary(grid: Grid, plates: Plates): Int32Array {
  const convergent: number[] = [];
  for (let i = 0; i < grid.size; i++) for (let k = 0; k < 6; k++) {
    const j = grid.nb[i * 6 + k]!;
    if (j < 0 || plates.plate[i] === plates.plate[j]) continue;
    const a = plates.plate[i]!, b = plates.plate[j]!;
    const dx = wrapDx(grid.x[j]! - grid.x[i]!, grid.width), dy = grid.y[j]! - grid.y[i]!;
    if ((plates.vx[a]! - plates.vx[b]!) * dx + (plates.vy[a]! - plates.vy[b]!) * dy > 0) { convergent.push(i); break; }
  }
  return distanceFrom(grid, convergent, 8);
}

function adjustPairedLayout(layout: WorldLayout, count: number, options: GenOptions, angles: number[], stretches: number[], random: Rng): void {
  if (layout !== 'rifted' && count >= 2) {
    angles[1] = angles[0]!;
    stretches[0] = stretches[1] = options.stretchRange[1];
  }
  if (layout === 'scattered' && count >= 4) {
    angles[3] = angles[2]! + random.range(-0.2, 0.2);
    stretches[2] = stretches[3] = options.stretchRange[1];
  }
}

function seedPairs(targets: number[], layout: WorldLayout, options: GenOptions, random: Rng): number[][] {
  const pairs = targets.map((_, b) => targets.map((__, k) => k < b ? random.range(1 - options.spacingVariation, 1 + options.spacingVariation) : 1));
  if (layout === 'clustered') for (let b = 0; b < targets.length - 1; b++) for (let k = 0; k < b; k++) pairs[b]![k]! *= 0.6;
  return pairs;
}

function createCrust(grid: Grid, targets: number[], random: Rng, options: GenOptions, plates: Plates, layout: WorldLayout, gap: number): Crust {
  const owner = new Int32Array(grid.size).fill(-1);
  const { shape, detail } = noiseFields(grid, random);
  const boundary = convergentBoundary(grid, plates);
  const masks = layout === 'rifted' ? riftDomains(grid, targets, plates, random, shape, options.riftRoughness) : null;
  const margin = Math.min(6, Math.floor(grid.rows / 4)), radii = targets.map((n) => Math.sqrt(n * 28 * 24 / Math.PI));
  const centreX = random.range(0, grid.width), north = random.next() < 0.5;
  const centreRow = grid.rows * (north ? 0.3 : 0.7);
  const angles = targets.map(() => random.range(0, Math.PI));
  const stretches = targets.map((_, b) => options.stretchRange[0] + (options.stretchRange[1] - options.stretchRange[0]) * (b + random.next()) / targets.length);
  adjustPairedLayout(layout, targets.length, options, angles, stretches, random);
  const pairs = seedPairs(targets, layout, options, random);
  return { grid, targets, random, options, layout, gap, owner, exclusion: owner.slice(), shape, detail, boundary, masks, margin, radii, centreX, north, centreRow, angles, stretches, pairs, seeds: [] };
}

/** Seed of a rift fragment: the cell of its mask with the lowest shape value, or -1 when the mask is empty. */
function maskSeed(c: Crust, mask: Uint8Array): number {
  const cells = [...mask.keys()].filter((i) => mask[i] === 1);
  if (!cells.length) return -1;
  return cells.reduce((best, i) => c.shape[i]! < c.shape[best]! ? i : best, cells[0]!);
}

function farSeedRow(c: Crust): number {
  const { grid, layout, margin, north, random } = c;
  if (layout === 'scattered') return margin + random.int(grid.rows - 2 * margin);
  return north ? Math.floor(grid.rows * 0.65) + random.int(grid.rows - margin - Math.floor(grid.rows * 0.65)) : margin + random.int(Math.floor(grid.rows * 0.35) - margin);
}

/** The last continent takes the best of 400 random cells, the one farthest from every earlier seed. */
function farthestSeed(c: Crust): number {
  let best = -1, distance = -1;
  for (let k = 0; k < 400; k++) {
    const row = farSeedRow(c);
    const i = row * c.grid.cols + c.random.int(c.grid.cols), d = Math.min(...c.seeds.map((s) => cellDistance(c.grid, s, i)));
    if (d > distance) { distance = d; best = i; }
  }
  return best;
}

function scatteredPosition(c: Crust, b: number): [number, number] {
  const { grid, random } = c;
  const side = random.next() < 0.5 ? -1 : 1, s = c.seeds[b - 1]!, angle = c.angles[b - 1]!;
  const col = Math.round((grid.x[s]! - Math.sin(angle) * side * 470 + random.range(-50, 50)) / 28);
  const row = Math.round((grid.y[s]! + Math.cos(angle) * side * 470 + random.range(-50, 50)) / 24);
  return [row, col];
}

function clusteredPosition(c: Crust, b: number): [number, number] {
  const { grid, random, margin, centreX, centreRow, north, angles } = c;
  const last = c.targets.length - 1;
  let col: number, row: number;
  if (b < 2) {
    const offset = (b === 0 ? -1 : 1) * 235;
    col = Math.round((centreX - Math.sin(angles[0]!) * offset + random.range(-35, 35)) / 28);
    row = Math.round(centreRow + (Math.cos(angles[0]!) * offset + random.range(-35, 35)) / 24);
  } else if (b < last) {
    const offset = (b % 2 === 0 ? -1 : 1) * random.range(500, 650);
    col = Math.round((centreX + Math.cos(angles[0]!) * offset + random.range(-70, 70)) / 28);
    row = Math.max(margin + 1, Math.min(grid.rows - margin - 2, Math.round(centreRow + (Math.sin(angles[0]!) * offset + random.range(-70, 70)) / 24)));
  } else {
    col = Math.round((centreX + grid.width * random.range(0.4, 0.6)) / 28);
    row = Math.round(grid.rows * (north ? 0.72 : 0.28) + random.range(-6, 6));
  }
  if (b < last) row = north ? Math.min(row, Math.floor(grid.rows * 0.4)) : Math.max(row, Math.ceil(grid.rows * 0.6));
  return [row, col];
}

function candidatePosition(c: Crust, b: number): [number, number] {
  const { grid, random, margin, layout } = c;
  let position: [number, number] = [margin + random.int(grid.rows - 2 * margin), random.int(grid.cols)];
  if (layout === 'scattered' && (b === 1 || b === 3)) position = scatteredPosition(c, b);
  if (layout === 'clustered') position = clusteredPosition(c, b);
  return position;
}

/** Rejection sampling that favours the equator and spreads seed latitudes apart. */
function acceptsLatitude(c: Crust, row: number): boolean {
  const { grid, options, random, seeds } = c;
  const lat = options.latitudeTop + (options.latitudeBottom - options.latitudeTop) * row / (grid.rows - 1);
  if (random.next() < options.lowLatitudeBias * Math.abs(lat) / Math.max(options.latitudeTop, -options.latitudeBottom)) return false;
  const accept = options.latitudeSpacing > 0 ? seeds.reduce((p, s) => {
    const d = (Math.floor(s / grid.cols) - row) * (options.latitudeTop - options.latitudeBottom) / (grid.rows - 1);
    return p * (1 - 0.7 * Math.exp(-((d / options.latitudeSpacing) ** 2)));
  }, 1) : 1;
  return !(random.next() > accept);
}

/** Random search for a seed far enough from the others; -1 when 6000 tries find none. */
function searchSeed(c: Crust, b: number): number {
  const { grid, margin, seeds, pairs, radii } = c;
  let spacing = c.options.seedSpacing, seed = -1;
  for (let tries = 0; tries < 6000 && seed < 0; tries++) {
    const [row, col] = candidatePosition(c, b);
    if (row < margin || row >= grid.rows - margin) continue;
    const i = row * grid.cols + ((col % grid.cols + grid.cols) % grid.cols);
    if (!acceptsLatitude(c, row)) continue;
    if (seeds.every((s, k) => cellDistance(grid, s, i) > Math.max(140, spacing * pairs[b]![k]! * (radii[k]! + radii[b]!)))) seed = i;
    else spacing *= 0.999;
  }
  return seed;
}

/** Places one seed per continent; false when a seed cannot be found. */
function placeSeeds(c: Crust): boolean {
  const count = c.targets.length;
  for (let b = 0; b < count; b++) {
    let seed: number;
    if (c.masks) seed = maskSeed(c, c.masks[b]!);
    else if (c.layout !== 'rifted' && b === count - 1 && c.seeds.length) seed = farthestSeed(c);
    else seed = searchSeed(c, b);
    if (seed < 0) return false;
    c.seeds.push(seed);
  }
  return true;
}

/** Growth state: one frontier per continent plus the exclusion buffer between them. */
interface Growth {
  c: Crust;
  zones: [number, number][][];
  heaps: Frontier[];
  queued: Uint8Array[];
  strokes: number[][];
}

function exclusionZones(gap: number): [number, number][][] {
  return [0, 1].map((parity) => {
    const out: [number, number][] = [];
    for (let dr = -gap; dr <= gap; dr++) for (let dq = -gap; dq <= gap; dq++) if ((Math.abs(dq) + Math.abs(dr) + Math.abs(dq + dr)) / 2 <= gap) out.push([dr, dq + Math.floor((parity + dr) / 2)]);
    return out;
  });
}

function createGrowth(c: Crust): Growth {
  const zones = exclusionZones(c.gap);
  const heaps = c.seeds.map(() => new Frontier()), queued = c.seeds.map(() => new Uint8Array(c.grid.size));
  const strokes = c.seeds.map((_, b) => [c.angles[b]! + c.random.range(-0.8, 0.8), c.angles[b]! + Math.PI + c.random.range(-0.8, 0.8)]);
  return { c, zones, heaps, queued, strokes };
}

function claim(g: Growth, cell: number, b: number): void {
  const { grid, owner, exclusion } = g.c;
  owner[cell] = b;
  const row = Math.floor(cell / grid.cols), col = cell % grid.cols;
  for (const [dr, dc] of g.zones[row % 2]!) {
    const r = row + dr;
    if (r < 0 || r >= grid.rows) continue;
    const cc = ((col + dc) % grid.cols + grid.cols) % grid.cols, j = r * grid.cols + cc;
    exclusion[j] = exclusion[j] === -1 || exclusion[j] === b ? b : -2;
  }
}

function frontierCost(g: Growth, i: number, b: number, row: number): number {
  const { grid, options, masks, layout, targets, seeds, angles, stretches, radii, shape, detail, boundary, north } = g.c;
  const dx = wrapDx(grid.x[i]! - grid.x[seeds[b]!]!, grid.width), dy = grid.y[i]! - grid.y[seeds[b]!]!;
  const ca = Math.cos(angles[b]!), sa = Math.sin(angles[b]!), stretch = Math.sqrt(stretches[b]!);
  const u = (dx * ca + dy * sa) / (radii[b]! * stretch), v = (-dx * sa + dy * ca) * stretch / radii[b]!;
  let cost = Math.hypot(u, v) + 0.2 * shape[i]! + 0.18 * detail[i]! - options.boundaryAttraction * 0.18 * Math.exp(-boundary[i]! / 3);
  // Small plate pieces may borrow from the parent so they can still hold their places.
  if (masks && masks[b]![i] === 2) cost += 0.45;
  if (layout === 'clustered' && b < targets.length - 1) cost += 3 * Math.max(0, (row / (grid.rows - 1) - 0.5) * (north ? 1 : -1));
  for (const a of g.strokes[b]!) {
    const along = (dx * Math.cos(a) + dy * Math.sin(a)) / radii[b]!, across = (-dx * Math.sin(a) + dy * Math.cos(a)) / radii[b]!;
    cost -= options.peninsulaStrength * 0.65 * Math.exp(-(((along - 1.25) / 0.85) ** 2) - (across / 0.18) ** 2);
  }
  cost += options.peninsulaStrength * 0.35 * Math.exp(-(((u - 0.5) / 0.4) ** 2) - ((v + 0.6) / 0.25) ** 2);
  return cost;
}

function pushCell(g: Growth, i: number, b: number): void {
  const { grid, owner, exclusion, masks } = g.c;
  if (i < 0 || g.queued[b]![i] || owner[i] !== -1 || masks && !masks[b]![i]) return;
  const row = Math.floor(i / grid.cols);
  if (row < 3 || row >= grid.rows - 3 || exclusion[i] !== -1 && exclusion[i] !== b) return;
  g.queued[b]![i] = 1;
  g.heaps[b]!.push(i, frontierCost(g, i, b, row));
}

function pushNeighbours(g: Growth, i: number, b: number): void {
  for (let k = 0; k < 6; k++) pushCell(g, g.c.grid.nb[i * 6 + k]!, b);
}

/** Claims the cheapest still-free frontier cell of continent `b`; false when its frontier ran dry. */
function growOnce(g: Growth, b: number): boolean {
  const { owner, exclusion } = g.c;
  while (g.heaps[b]!.size) {
    const i = g.heaps[b]!.pop();
    if (owner[i] !== -1 || exclusion[i] !== -1 && exclusion[i] !== b) continue;
    claim(g, i, b);
    pushNeighbours(g, i, b);
    return true;
  }
  return false;
}

/** Round-robin growth: each continent below its target claims one cell per round. */
function growFrontiers(c: Crust): void {
  const g = createGrowth(c);
  const sizes = c.seeds.map((s, b) => { claim(g, s, b); return 1; });
  c.seeds.forEach((s, b) => pushNeighbours(g, s, b));
  let grew = true;
  while (grew) {
    grew = false;
    for (let b = 0; b < c.seeds.length; b++) {
      if (sizes[b]! >= c.targets[b]!) continue;
      if (growOnce(g, b)) { sizes[b]!++; grew = true; }
    }
  }
}

export function growContinents(grid: Grid, targets: number[], random: Rng, options: GenOptions, plates: Plates, layout: WorldLayout, gap = 4): Int32Array {
  const c = createCrust(grid, targets, random, options, plates, layout, gap);
  if (!placeSeeds(c)) return c.owner;
  // Even initial rift seeds must respect the buffer before any frontier is claimed.
  if (c.seeds.some((s, b) => c.seeds.slice(0, b).some((t) => cellDistance(grid, s, t) <= gap * 28))) return c.owner;
  growFrontiers(c);
  return c.owner;
}
