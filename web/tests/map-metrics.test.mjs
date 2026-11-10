import { expect, it } from 'vitest';
import { coefficientVariation, principalAxes, hasRegularRow, morphology, minimumSeaGap, straightCoastRuns } from '../scripts/map-metrics.mjs';

it('measures population variation rather than the absolute distance scale', () => {
  expect(coefficientVariation([2, 2, 2])).toBe(0);
  expect(coefficientVariation([1, 3])).toBeCloseTo(0.5);
});

it('finds principal axes across the cylindrical seam and after rotation', () => {
  expect(principalAxes([{ x: 98, y: -1 }, { x: 2, y: -1 }, { x: 98, y: 1 }, { x: 2, y: 1 }], 100).ratio).toBeCloseTo(2);
  const points = [{ x: -2, y: -1 }, { x: 2, y: -1 }, { x: -2, y: 1 }, { x: 2, y: 1 }]
    .map(({ x, y }) => ({ x: (x - y) / Math.sqrt(2) + 50, y: (x + y) / Math.sqrt(2) }));
  const axes = principalAxes(points, 100);
  expect(axes.ratio).toBeCloseTo(2);
  expect(axes.angle).toBeCloseTo(45);
});

it('detects sliding latitude rows only when longitudes are regularly spaced', () => {
  expect(hasRegularRow([{ latitude: 9, longitude: 0 }, { latitude: 15, longitude: 120 }, { latitude: 20, longitude: 240 }])).toBe(true);
  expect(hasRegularRow([{ latitude: 9, longitude: 0 }, { latitude: 15, longitude: 25 }, { latitude: 20, longitude: 240 }])).toBe(false);
  expect(hasRegularRow([{ latitude: 9, longitude: 0 }, { latitude: 15, longitude: 120 }, { latitude: 22, longitude: 240 }])).toBe(false);
});

it('splits equatorial land evenly and uses wrapped centroid distances', () => {
  const w = { grid: { cols: 1, width: 100, x: [98, 2, 50], y: [0, 0, 10] },
    climate: { latitude: [10, 0, -10] }, land: [1, 1, 0], continents: [[0], [1]] };
  const result = morphology(w, [{ latitude: 10, longitude: 352.8 }, { latitude: 0, longitude: 7.2 }]);
  expect(result.hemisphereShare).toBe(0.75);
  result.nearestDistances.forEach((d) => expect(d).toBeCloseTo(4));
});

it('counts the water cells between mainland shores including across the seam', () => {
  const nb = new Int32Array(8 * 6).fill(-1);
  for (let i = 0; i < 8; i++) { nb[i * 6] = (i + 1) % 8; nb[i * 6 + 1] = (i + 7) % 8; }
  expect(minimumSeaGap({ size: 8, nb }, [[1], [5]])).toBe(3);
  expect(minimumSeaGap({ size: 8, nb }, [[0], [7]])).toBe(0);
});

it('counts an aligned coast across the seam and breaks the run at a promontory', () => {
  const nb = new Int32Array(16 * 6).fill(-1), land = new Uint8Array(16);
  land.fill(1, 0, 8);
  for (let i = 0; i < 8; i++) { nb[i * 6] = (i + 1) % 8; nb[i * 6 + 3] = (i + 7) % 8; nb[i * 6 + 2] = i + 8; }
  const continents = [Array.from({ length: 8 }, (_, i) => i)];
  const water = Uint8Array.from(land, (v) => 1 - v);
  expect(straightCoastRuns({ size: 16, nb }, water, continents)).toEqual([8]);
  water[11] = 0;
  // The missing coastal face cuts this ring into one seven-cell run.
  expect(straightCoastRuns({ size: 16, nb }, water, continents)).toEqual([7]);
});
