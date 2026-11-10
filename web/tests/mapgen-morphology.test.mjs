import { expect, it } from 'vitest';
import { generateWorld } from '../src/map/gen/world.ts';
import { morphology, principalAxes, minimumSeaGap, straightCoastRuns } from '../scripts/map-metrics.mjs';

for (const layout of ['scattered', 'clustered', 'rifted']) it(`builds deterministic ${layout} worlds with explicit shape controls`, () => {
  const options = { layout, boundaryAttraction: 0.7, stretchRange: [1.2, 2.5], spacingVariation: 0.6, peninsulaStrength: 0.6 };
  const a = generateWorld('types', [380, 380, 380, 380, 380], options), b = generateWorld('types', [380, 380, 380, 380, 380], options);
  expect(a.layout).toBe(layout);
  expect(a.height).toEqual(b.height);
  expect(a.continents).toEqual(b.continents);
});

it('keeps a useful river network on the rifted small-catchment seed', () => {
  const w = generateWorld('sample-6', [380, 380, 380, 380, 380]);
  const count = w.river.reduce((a, b) => a + b, 0);
  expect(count).toBeGreaterThanOrEqual(40);
  expect(count).toBeLessThanOrEqual(120);
});

it.each([
  { layout: 'unknown' }, { layoutWeights: [0, 0, 0] }, { layoutWeights: [1, 2] },
  { layoutWeights: [Infinity, 1, 1] }, { layoutWeights: [1e308, 1e308, 1e308] },
  { stretchRange: [0.5, 2] }, { stretchRange: [2, 1] }, { stretchRange: [1, 3] },
  { boundaryAttraction: -1 }, { spacingVariation: 1 }, { peninsulaStrength: 2 }, { riftRoughness: -1 }, { riftRoughness: NaN },
])('rejects invalid Round 2 options %j', (options) => {
  expect(() => generateWorld('invalid', [380, 380, 380, 380, 380], options)).toThrow('Invalid map generation options');
});

it.each(['scattered', 'clustered', 'rifted'])('honors a single nonzero layout weight for %s', (layout) => {
  const options = { layoutWeights: ['scattered', 'clustered', 'rifted'].map((type) => Number(type === layout)) };
  expect(generateWorld('types', [380, 380, 380, 380, 380], options).layout).toBe(layout);
});

it('meets the Round 2 geometry targets across the paired 30-seed panel', () => {
  const results = []; let roughCoasts = 0;
  for (const seed of ['scenario-1', 'scenario-2', 'scenario-3', ...Array.from({ length: 27 }, (_, i) => `sample-${i + 4}`)]) {
    const w = generateWorld(seed, [380, 380, 380, 380, 380]);
    expect(minimumSeaGap(w.grid, w.continents), seed).toBeGreaterThanOrEqual(3);
    const axes = w.continents.map((cells) => principalAxes(cells.map((i) => ({ x: w.grid.x[i], y: w.grid.y[i] })), w.grid.width));
    const top = w.climate.latitude[0], bottom = w.climate.latitude[w.grid.rows - 1];
    const runs = straightCoastRuns(w.grid, Uint8Array.from(w.height, (h) => Number(h < 0)), w.continents);
    if (Math.max(...runs) <= 6) roughCoasts++;
    results.push({ ...morphology(w, axes.map((a) => ({ latitude: top + (bottom - top) * a.y / (24 * (w.grid.rows - 1)), longitude: a.x * 360 / w.grid.width }))), layout: w.layout });
  }
  expect(results.filter((r) => r.regularRow).length).toBeLessThanOrEqual(2);
  expect(results.filter((r) => r.hemisphereShare >= 0.58).length).toBeGreaterThanOrEqual(15);
  expect(Math.max(...results.map((r) => r.hemisphereShare))).toBeLessThanOrEqual(0.85);
  expect(results.filter((r) => r.nearestCv >= 0.25).length).toBeGreaterThanOrEqual(16);
  expect(results.filter((r) => r.maxAspect >= 2).length).toBeGreaterThanOrEqual(16);
  expect(results.filter((r) => r.elongatedNonZonal).length).toBeGreaterThanOrEqual(10);
  expect(roughCoasts).toBeGreaterThanOrEqual(28);
  for (const type of ['scattered', 'clustered', 'rifted']) expect(results.filter((r) => r.layout === type).length).toBeGreaterThanOrEqual(5);
  const scattered = results.filter((r) => r.layout === 'scattered');
  expect(scattered.filter((r) => r.nearestCv >= 0.25).length).toBeGreaterThan(scattered.length / 2);
});
