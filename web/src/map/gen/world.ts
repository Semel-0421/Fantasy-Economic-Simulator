// One seeded world: plates -> elevation -> erosion -> sea level -> drainage -> climate -> biomes
// -> rivers. Retries deterministically (seed|attempt) until the land splits into enough separate
// landmasses, each big enough for the continent the simulation's map data puts on it.
import { makeGrid, gridFor, type Grid } from './grid';
import { growPlates, growContinents, elevation, erode, type Plates } from './geology';
import { drain, landComponents, accumulate, type Drainage } from './hydro';
import { climate, biomes, type Biome, type Climate } from './climate';
import { rng } from './rng';

export interface GenWorld {
  seed: string;
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

export function generateWorld(seed: string, needs: number[]): GenWorld {
  const total = needs.reduce((a, b) => a + b, 0);
  const { cols, rows } = gridFor(total * 1.3, 0.33);
  const grid = makeGrid(cols, rows);
  for (let attempt = 0; attempt < 24; attempt++) {
    const random = rng(`${seed}|${attempt}`);
    const crust = growContinents(grid, needs.map((n) => Math.ceil(n * 1.32)), random);
    const plates = growPlates(grid, 10 + random.int(5), random);
    const height = erode(grid, elevation(grid, plates, crust, random));
    {
      // Sea level is the crust's own datum: continental crust stands above it, ocean floor below.
      const sea = 0;
      const drainage = drain(grid, height, sea);
      const land = new Uint8Array(grid.size);
      for (let i = 0; i < grid.size; i++) land[i] = height[i]! >= sea && !drainage.lake[i] ? 1 : 0;
      // Each continent is the landmass holding most of its crust blob.
      const comps = landComponents(grid, land), continents: number[][] = [];
      let ok = true;
      for (let b = 0; b < needs.length && ok; b++) {
        const best = comps.map((c) => c.filter((i) => crust[i] === b).length).reduce((m, v, k, a) => v > a[m]! ? k : m, 0);
        const comp = comps[best]!;
        if (comp.length < needs[b]! || continents.includes(comp)) ok = false; else continents.push(comp);
      }
      if (!ok) continue;
      const water = new Uint8Array(grid.size);
      for (let i = 0; i < grid.size; i++) water[i] = land[i] ? 0 : 1;
      let peak = sea;
      for (let i = 0; i < grid.size; i++) if (land[i] && height[i]! > peak) peak = height[i]!;
      // Temperate-centred worlds (a playable band, like a regional map rather than pole to pole).
      const latTop = random.range(38, 60), latBottom = latTop - random.range(70, 95);
      const c = climate(grid, height, water, sea, peak, latTop, latBottom);
      const biome = biomes(grid, height, land, c);
      const flow = accumulate(grid, drainage, c.rain, land);
      // Rivers: cells draining a large catchment (9+ hexes) that also gets at least 50 % of the
      // land's average rain per hex, so dry basins have fewer rivers than wet ones.
      const area = accumulate(grid, drainage, Float32Array.from({ length: grid.size }, () => 1), land);
      let rainSum = 0, landCount = 0;
      for (let i = 0; i < grid.size; i++) if (land[i]) { rainSum += c.rain[i]!; landCount++; }
      const meanRain = rainSum / Math.max(1, landCount);
      const river = new Uint8Array(grid.size);
      for (let i = 0; i < grid.size; i++) if (land[i] && area[i]! >= 9 && flow[i]! >= 0.5 * area[i]! * meanRain && biome[i] !== 'mountain') river[i] = 1;
      return { seed, attempt, grid, plates, height, sea, land, drainage, climate: c, biome, flow, river, continents };
    }
  }
  throw new Error(`No world for seed ${seed}: land would not split into ${needs.length} large enough landmasses`);
}
