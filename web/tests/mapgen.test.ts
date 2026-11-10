// Unit tests of the seeded map generator stages (src/map/gen).
import { describe, expect, it } from 'vitest';
import { rng, wrappedFbm } from '../src/map/gen/rng';
import { makeGrid, wrapDx } from '../src/map/gen/grid';
import { drain } from '../src/map/gen/hydro';
import { DEFAULT_GEN_OPTIONS, generateWorld, type GenOptions } from '../src/map/gen/world';
import { elevation, erode, growPlates } from '../src/map/gen/geology';
import { growContinents } from '../src/map/gen/crust';
import { fullscaleMap } from './fullscale-map';
import { continentNeeds } from '../src/map/hex_layout';

/** Every cell within three hexes of crust cell i belongs to the same continent or to the sea. */
function expectBufferAround(grid: ReturnType<typeof makeGrid>, crust: Int32Array, i: number): void {
  let ring = new Set([i]);
  for (let d = 0; d < 3; d++) {
    const next = new Set<number>();
    for (const c of ring) expectNeighbourBuffer(grid, crust, i, c, next);
    ring = next;
  }
}

function expectNeighbourBuffer(grid: ReturnType<typeof makeGrid>, crust: Int32Array, i: number, c: number, next: Set<number>): void {
  for (let n = 0; n < 6; n++) {
    const j = grid.nb[c * 6 + n]!;
    if (j >= 0) { expect(crust[j] === -1 || crust[j] === crust[i]).toBe(true); next.add(j); }
  }
}

describe('map generator', () => {
  it('draws the same numbers from the same seed and different ones from another', () => {
    const seq = (seed: string): number[] => { const r = rng(seed); return Array.from({ length: 5 }, () => r.next()); };
    expect(seq('x')).toEqual(seq('x'));
    expect(seq('x')).not.toEqual(seq('y'));
  });
  it('makes noise that tiles round the cylinder', () => {
    const f = wrappedFbm(7, 4, 190, 2688);
    for (const [x, y] of [[0, 10], [333.3, 500], [2000, 77]] as const) expect(f(x + 2688, y)).toBeCloseTo(f(x, y), 9);
  });
  it('joins the east and west edges of the grid and keeps neighbours symmetric', () => {
    const g = makeGrid(20, 9);
    for (let i = 0; i < g.size; i++) for (let n = 0; n < 6; n++) {
      const j = g.nb[i * 6 + n]!;
      if (j < 0) continue;
      expect([...g.nb.slice(j * 6, j * 6 + 6)]).toContain(i);
      expect(Math.hypot(wrapDx(g.x[j]! - g.x[i]!, g.width), g.y[j]! - g.y[i]!)).toBeLessThan(29);
    }
    // Only the polar rows have missing neighbours.
    for (let i = g.cols; i < g.size - g.cols; i++) expect([...g.nb.slice(i * 6, i * 6 + 6)].every((j) => j >= 0)).toBe(true);
  });
  it('drains every land cell to the sea downhill over filled elevation', () => {
    const g = makeGrid(30, 12), h = new Float32Array(g.size);
    for (let i = 0; i < g.size; i++) {
      const row = Math.floor(i / g.cols);
      h[i] = row === 0 || row === g.rows - 1 ? -1 : Math.sin(i * 1.7) * 0.3 + 0.2;
    }
    const d = drain(g, h, 0);
    for (let i = 0; i < g.size; i++) {
      if (h[i]! < 0) continue;
      let c = i, steps = 0;
      while (h[c]! >= 0 && steps++ < g.size) {
        const next = d.down[c]!;
        expect(d.filled[next]!).toBeLessThan(d.filled[c]!);
        c = next;
      }
      expect(h[c]!).toBeLessThan(0);
    }
  });
  it('gives each continent its own landmass, large enough for its places', () => {
    const needs = [380, 400, 360, 390, 370], w = generateWorld('test-world', needs);
    needs.forEach((n, k) => expect(w.continents[k]!.length).toBeGreaterThanOrEqual(n));
    expect(new Set(w.continents.flat()).size).toBe(w.continents.flat().length);
  });
  it('avoids repeated latitude rows and near-equal sizes across the review seeds', () => {
    const needs = continentNeeds(fullscaleMap);
    let varied = 0, spread = 0, equatorial = 0;
    for (const seed of ['scenario-1', 'scenario-2', 'scenario-3', ...Array.from({ length: 27 }, (_, i) => `sample-${i + 4}`)]) {
      const w = generateWorld(seed, needs), bands = new Map<number, number>();
      for (const cells of w.continents) {
        const lat = cells.reduce((s, i) => s + w.climate.latitude[Math.floor(i / w.grid.cols)]!, 0) / cells.length;
        const band = Math.floor(lat / 10);
        bands.set(band, (bands.get(band) ?? 0) + 1);
      }
      const occupied = [...bands.values()].sort((a, b) => b - a);
      if (occupied[0]! <= 2 && occupied[0]! + occupied[1]! < 4) spread++;
      const sizes = w.continents.map((c) => c.length);
      if (Math.max(...sizes) / Math.min(...sizes) >= 1.4) varied++;
      if (w.land.some((v, i) => v === 1 && Math.abs(w.climate.latitude[Math.floor(i / w.grid.cols)]!) < 10)) equatorial++;
      needs.forEach((n, k) => expect(sizes[k]).toBeGreaterThanOrEqual(n));
    }
    expect(varied).toBeGreaterThanOrEqual(20);
    expect(spread).toBeGreaterThanOrEqual(20);
    expect(equatorial).toBeGreaterThanOrEqual(24);
  });
  it('applies explicit generation controls deterministically', () => {
    const needs = [380, 380, 380, 380, 380];
    const options = { latitudeTop: 40, latitudeBottom: -35, plateCount: 8, noiseStrength: 0.5, erosionStrength: 0.5, riverThreshold: 10000, seedSpacing: 0.8, lowLatitudeBias: 0.4, continentSizeRange: [1.4, 1.9] as const };
    const w = generateWorld('options', needs, options), again = generateWorld('options', needs, options);
    expect(w.height).toEqual(again.height);
    expect(w.continents).toEqual(again.continents);
    expect(w.climate.latitude[0]).toBe(40);
    expect(w.climate.latitude[w.grid.rows - 1]).toBe(-35);
    expect(new Set(w.plates.plate).size).toBe(8);
    expect(w.river.some(Boolean)).toBe(false);
    const defaults = generateWorld('options', needs);
    expect(w.height).not.toEqual(defaults.height);
  });
  it('keeps the crust separated by three hexes with cached exclusion zones', () => {
    const grid = makeGrid(40, 24), crust = growContinents(grid, [70, 90, 110], rng('gap'), DEFAULT_GEN_OPTIONS, growPlates(grid, 12, rng('gap-plates')), 'scattered');
    for (let i = 0; i < grid.size; i++) if (crust[i]! >= 0) expectBufferAround(grid, crust, i);
  });
  it('scales elevation noise and erosion without changing their input', () => {
    const grid = makeGrid(40, 24), crust = growContinents(grid, [70, 90, 110], rng('stages'), DEFAULT_GEN_OPTIONS, growPlates(grid, 12, rng('stage-plates')), 'scattered');
    const plates = growPlates(grid, 8, rng('plates'));
    const smooth = elevation(grid, plates, crust, rng('height'), 0), noisy = elevation(grid, plates, crust, rng('height'), 1);
    expect(noisy).not.toEqual(smooth);
    const copy = noisy.slice();
    expect(erode(grid, noisy, 4, 0)).toEqual(noisy);
    expect(erode(grid, noisy, 4, 1)).not.toEqual(noisy);
    expect(noisy).toEqual(copy);
  });
  it.each<Partial<GenOptions>>([
    { latitudeTop: 0 }, { latitudeBottom: 1 }, { latitudeTop: 91 },
    { seedSpacing: 0 }, { lowLatitudeBias: 2 }, { latitudeSpacing: -1 },
    { continentSizeRange: [1.3, 1.2] }, { continentSizeRange: [1.3, Infinity] },
    { plateCount: 2.5 }, { plateCount: 0 }, { noiseStrength: NaN },
    { erosionStrength: 2 }, { riverThreshold: 0 },
  ])('rejects invalid generation options %j', (options) => {
    expect(() => generateWorld('invalid', [380], options)).toThrow('Invalid map generation options');
  });
});
