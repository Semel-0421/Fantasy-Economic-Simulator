// Unit tests of the seeded map generator stages (src/map/gen).
import { describe, expect, it } from 'vitest';
import { rng, wrappedFbm } from '../src/map/gen/rng';
import { makeGrid, wrapDx } from '../src/map/gen/grid';
import { drain } from '../src/map/gen/hydro';
import { generateWorld } from '../src/map/gen/world';

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
});
