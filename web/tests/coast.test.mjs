import { expect, it } from 'vitest';
import { makeGrid } from '../src/map/gen/grid.ts';
import { rng } from '../src/map/gen/rng.ts';
import { landComponents } from '../src/map/gen/hydro.ts';
import { roughenCoasts } from '../src/map/gen/coast.ts';
import { straightCoastRuns, minimumSeaGap } from '../scripts/map-metrics.mjs';

function field(thin = false) {
  const grid = makeGrid(40, 24), height = new Float32Array(grid.size).fill(-0.3), owner = new Int32Array(grid.size).fill(-1);
  for (let row = thin ? 12 : 6; row <= (thin ? 12 : 17); row++) for (let col = 8; col <= 28; col++) {
    const i = row * grid.cols + col; height[i] = 0.3; owner[i] = 0;
  }
  return { grid, height, owner };
}

it.each([false, true])('breaks long cut coasts without disconnecting the land (thin=%s)', (thin) => {
  const { grid, height, owner } = field(thin), original = height.slice();
  roughenCoasts(grid, height, owner, rng('cut'));
  const land = Uint8Array.from(height, (h) => Number(h >= 0)), comps = landComponents(grid, land);
  expect(comps).toHaveLength(1);
  expect(straightCoastRuns(grid, Uint8Array.from(land, (v) => 1 - v), comps)[0]).toBeLessThanOrEqual(6);
  expect(height).not.toEqual(original);
  const again = field(thin);
  roughenCoasts(again.grid, again.height, again.owner, rng('cut'));
  expect(height).toEqual(again.height);
});

it('keeps three water cells between facing coastlines', () => {
  const { grid, height, owner } = field();
  for (let i = 0; i < grid.size; i++) { height[i] = -0.3; owner[i] = -1; }
  for (let row = 4; row <= 19; row++) for (let col = 4; col <= 31; col++) if (col <= 14 || col >= 20) {
    const i = row * grid.cols + col; height[i] = 0.3; owner[i] = col <= 14 ? 0 : 1;
  }
  roughenCoasts(grid, height, owner, rng('strait'));
  const comps = landComponents(grid, Uint8Array.from(height, (h) => Number(h >= 0)));
  expect(comps).toHaveLength(2);
  expect(minimumSeaGap(grid, comps)).toBeGreaterThanOrEqual(3);
});
