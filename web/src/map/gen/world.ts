// One seeded world: plates -> elevation -> erosion -> sea level -> drainage -> climate -> biomes
// -> rivers. Retries deterministically (seed|attempt) until the land splits into enough separate
// landmasses, each big enough for the continent the simulation's map data puts on it.
import { makeGrid, gridFor, type Grid } from './grid';
import { growPlates, elevation, erode, distanceFrom, type Plates } from './geology';
import { growContinents } from './crust';
import { roughenCoasts } from './coast';
import { drain, landComponents, accumulate, type Drainage } from './hydro';
import { climate, biomes, type Biome, type Climate } from './climate';
import { rng } from './rng';

export type WorldLayout = 'scattered' | 'clustered' | 'rifted';

export interface GenOptions {
  layout: WorldLayout | 'mixed';
  /** Scattered, clustered, rifted weights; selected once per world seed. */
  layoutWeights: readonly [number, number, number];
  boundaryAttraction: number;
  stretchRange: readonly [number, number];
  spacingVariation: number;
  peninsulaStrength: number;
  riftRoughness: number;
  /** Initial seed separation as a fraction of the two crust blobs' estimated radii. */
  seedSpacing: number;
  /** Rejection bias toward the equator: 0 uniform, 1 strongest at the latitude limits. */
  lowLatitudeBias: number;
  /** Soft repulsion between seed latitudes, in degrees; 0 disables it. */
  latitudeSpacing: number;
  latitudeTop: number;
  latitudeBottom: number;
  /** Crust target / required land cells, sampled in shuffled strata to retain size variety. */
  continentSizeRange: readonly [number, number];
  plateCount: number;
  noiseStrength: number;
  erosionStrength: number;
  /** Minimum upstream catchment in hexes. */
  riverThreshold: number;
}

export const DEFAULT_GEN_OPTIONS: Readonly<GenOptions> = Object.freeze({
  layout: 'mixed', layoutWeights: Object.freeze([0.3, 0.45, 0.25] as const),
  boundaryAttraction: 0.7, stretchRange: Object.freeze([1.2, 2.5] as const),
  spacingVariation: 0.55, peninsulaStrength: 0.7, riftRoughness: 0.8,
  seedSpacing: 0.9, lowLatitudeBias: 0.5, latitudeSpacing: 12.8,
  latitudeTop: 48, latitudeBottom: -48,
  continentSizeRange: Object.freeze([1.3, 2.1] as const),
  plateCount: 14, noiseStrength: 1, erosionStrength: 1, riverThreshold: 6,
});

export interface GenWorld {
  seed: string;
  layout: WorldLayout;
  attempt: number;
  grid: Grid;
  plates: Plates;
  height: Float32Array;
  sea: number;
  /** 1 for land (not sea, not lake). */
  land: Uint8Array;
  drainage: Drainage;
  climate: Climate;
  biome: Biome[];
  flow: Float32Array;
  /** 1 where a river runs (flows on to `drainage.down`). */
  river: Uint8Array;
  /** Land cells per continent, in the order of `needs`. */
  continents: number[][];
}

function layoutShapeInvalid(o: GenOptions): boolean {
  return !Array.isArray(o.continentSizeRange) || o.continentSizeRange.length !== 2
    || !Array.isArray(o.stretchRange) || o.stretchRange.length !== 2
    || !Array.isArray(o.layoutWeights) || o.layoutWeights.length !== 3;
}

function layoutOptionsInvalid(o: GenOptions, weightTotal: number): boolean {
  return Object.entries(o).some(([key, v]) => !['layout', 'layoutWeights', 'stretchRange', 'continentSizeRange'].includes(key) && !Number.isFinite(v))
    || !['mixed', 'scattered', 'clustered', 'rifted'].includes(o.layout)
    || o.layoutWeights.some((v) => !Number.isFinite(v) || v < 0) || !Number.isFinite(weightTotal) || weightTotal <= 0
    || o.stretchRange.some((v) => !Number.isFinite(v)) || o.stretchRange[0] < 1 || o.stretchRange[1] < o.stretchRange[0] || o.stretchRange[1] > 2.5;
}

function shapeOptionsInvalid(o: GenOptions): boolean {
  return o.boundaryAttraction < 0 || o.boundaryAttraction > 2 || o.spacingVariation < 0 || o.spacingVariation > 0.9 || o.peninsulaStrength < 0 || o.peninsulaStrength > 1
    || o.riftRoughness < 0 || o.riftRoughness > 1;
}

function seedOptionsInvalid(o: GenOptions, minSize: number, maxSize: number): boolean {
  return !Number.isFinite(minSize) || !Number.isFinite(maxSize)
    || o.latitudeTop <= 0 || o.latitudeTop > 90 || o.latitudeBottom >= 0 || o.latitudeBottom < -90
    || o.seedSpacing <= 0 || o.seedSpacing > 2 || o.lowLatitudeBias < 0 || o.lowLatitudeBias > 1
    || o.latitudeSpacing < 0 || o.latitudeSpacing > 30
    || minSize < 1.2 || maxSize < minSize || maxSize > 2.5;
}

function terrainOptionsInvalid(o: GenOptions): boolean {
  return !Number.isInteger(o.plateCount) || o.plateCount < 2 || o.plateCount > 32
    || o.noiseStrength < 0 || o.noiseStrength > 2 || o.erosionStrength < 0 || o.erosionStrength > 1
    || o.riverThreshold < 1;
}

function pickLayout(seed: string, o: GenOptions, weightTotal: number): WorldLayout {
  let layout: WorldLayout = o.layout === 'mixed' ? 'rifted' : o.layout;
  if (o.layout === 'mixed') {
    let pick = rng(`${seed}|layout`).next() * weightTotal;
    for (const [k, type] of (['scattered', 'clustered', 'rifted'] as const).entries()) if ((pick -= o.layoutWeights[k]!) < 0) { layout = type; break; }
  }
  return layout;
}

/** Each continent is the landmass holding most of its crust blob; null when one is too small or shared. */
function pickContinents(comps: number[][], crust: Int32Array, needs: number[]): number[][] | null {
  const continents: number[][] = [];
  for (let b = 0; b < needs.length; b++) {
    const best = comps.map((c) => c.filter((i) => crust[i] === b).length).reduce((m, v, k, a) => v > a[m]! ? k : m, 0);
    const comp = comps[best]!;
    if (comp.length < needs[b]! || continents.includes(comp)) return null;
    continents.push(comp);
  }
  return continents;
}

/** Rivers: cells draining a large catchment that also gets at least 50 % of the
 * land's average rain per hex, so dry basins have fewer rivers than wet ones. */
function riverCells(grid: Grid, land: Uint8Array, drainage: Drainage, rain: Float32Array, biome: Biome[], flow: Float32Array, threshold: number): Uint8Array {
  const area = accumulate(grid, drainage, Float32Array.from({ length: grid.size }, () => 1), land);
  let rainSum = 0, landCount = 0;
  for (let i = 0; i < grid.size; i++) if (land[i]) { rainSum += rain[i]!; landCount++; }
  const meanRain = rainSum / Math.max(1, landCount);
  const river = new Uint8Array(grid.size);
  const channels = [...land.keys()].filter((i) => land[i] && area[i]! >= threshold && flow[i]! >= 0.5 * area[i]! * meanRain && biome[i] !== 'mountain');
  // Long, narrow continents split catchments: admit small tributaries, then retain the main flow.
  channels.sort((a, b) => flow[b]! - flow[a]! || a - b);
  for (const i of channels.slice(0, Math.ceil(landCount * 0.035))) river[i] = 1;
  return river;
}

interface Attempt {
  seed: string;
  layout: WorldLayout;
  attempt: number;
  grid: Grid;
  plates: Plates;
  crust: Int32Array;
  height: Float32Array;
  needs: number[];
}

/** Drainage, climate, biomes and rivers for a crust that passed the size checks; null when the land does not split as needed. */
function finishWorld(a: Attempt, o: GenOptions): GenWorld | null {
  const { seed, layout, attempt, grid, plates, crust, height, needs } = a;
  // Sea level is the crust's own datum: continental crust stands above it, ocean floor below.
  const sea = 0;
  const drainage = drain(grid, height, sea);
  const land = new Uint8Array(grid.size);
  for (let i = 0; i < grid.size; i++) land[i] = height[i]! >= sea && !drainage.lake[i] ? 1 : 0;
  const comps = landComponents(grid, land);
  if (comps.length < needs.length) return null;
  const continents = pickContinents(comps, crust, needs);
  if (!continents) return null;
  const water = new Uint8Array(grid.size);
  for (let i = 0; i < grid.size; i++) water[i] = land[i] ? 0 : 1;
  let peak = sea;
  for (let i = 0; i < grid.size; i++) if (land[i] && height[i]! > peak) peak = height[i]!;
  const c = climate(grid, height, water, sea, peak, o.latitudeTop, o.latitudeBottom);
  const biome = biomes(grid, height, land, c);
  const flow = accumulate(grid, drainage, c.rain, land);
  const river = riverCells(grid, land, drainage, c.rain, biome, flow, o.riverThreshold);
  return { seed, layout, attempt, grid, plates, height, sea, land, drainage, climate: c, biome, flow, river, continents };
}

/** One deterministic try at a world; null when the crust or land fails the size checks. */
function tryWorld(seed: string, needs: number[], o: GenOptions, layout: WorldLayout, grid: Grid, attempt: number): GenWorld | null {
  const [minSize, maxSize] = o.continentSizeRange;
  const random = rng(`${seed}|${attempt}`);
  const sizes = needs.map((_, k) => minSize + (maxSize - minSize) * (k + random.next()) / needs.length);
  for (let k = sizes.length - 1; k > 0; k--) { const j = random.int(k + 1); [sizes[k], sizes[j]] = [sizes[j]!, sizes[k]!]; }
  const plates = growPlates(grid, o.plateCount, random);
  const crust = growContinents(grid, needs.map((n, k) => Math.ceil(n * sizes[k]!)), random, o, plates, layout);
  const crustCounts = new Uint32Array(needs.length);
  for (const b of crust) if (b >= 0) crustCounts[b]!++;
  if (needs.some((n, b) => crustCounts[b]! < n)) return null;
  const height = erode(grid, elevation(grid, plates, crust, random, o.noiseStrength), 4, o.erosionStrength);
  // Preserve the rift/strait buffer after uplift and erosion; offshore islands remain possible.
  const shore = distanceFrom(grid, [...crust.keys()].filter((i) => crust[i]! >= 0), 2);
  for (let i = 0; i < grid.size; i++) if (crust[i] === -1 && shore[i]! <= 2) height[i] = Math.min(height[i]!, -0.01);
  roughenCoasts(grid, height, crust, random);
  return finishWorld({ seed, layout, attempt, grid, plates, crust, height, needs }, o);
}

export function generateWorld(seed: string, needs: number[], options: Partial<GenOptions> = {}): GenWorld {
  const o: GenOptions = { ...DEFAULT_GEN_OPTIONS, ...options };
  if (layoutShapeInvalid(o)) throw new Error('Invalid map generation options');
  const [minSize, maxSize] = o.continentSizeRange;
  const weightTotal = o.layoutWeights.reduce((a, b) => a + b, 0);
  if (layoutOptionsInvalid(o, weightTotal) || shapeOptionsInvalid(o) || seedOptionsInvalid(o, minSize, maxSize) || terrainOptionsInvalid(o)) throw new Error('Invalid map generation options');
  const total = needs.reduce((a, b) => a + b, 0);
  const { cols, rows } = gridFor(total * 1.4, 0.33);
  const grid = makeGrid(cols, rows);
  const layout = pickLayout(seed, o, weightTotal);
  if (layout === 'rifted' && o.plateCount < needs.length) throw new Error('Rifted worlds need at least one plate per continent');
  for (let attempt = 0; attempt < 24; attempt++) {
    const world = tryWorld(seed, needs, o, layout, grid, attempt);
    if (world) return world;
  }
  throw new Error(`No world for seed ${seed}: land would not split into ${needs.length} large enough landmasses`);
}
