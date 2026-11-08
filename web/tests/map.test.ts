import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { WORLD_H, WORLD_W, isTown, layoutMap, pickTown, routePairs, destinations } from '../src/map/layout';
import { fitCamera, panBy, toWorld, zoomAt } from '../src/map/camera';
import type { Views } from '../src/sim/types';

const map = JSON.parse(readFileSync(new URL('./fixtures/map.json', import.meta.url), 'utf8')) as Views['map'];

describe('map layout', () => {
  it('is deterministic and keeps every node inside the world', () => {
    const a = layoutMap(map);
    const b = layoutMap(map);
    expect([...a.points.entries()]).toEqual([...b.points.entries()]);
    expect(a.points.size).toBe(map.nodes.length);
    for (const p of a.points.values()) {
      expect(p.x).toBeGreaterThanOrEqual(0);
      expect(p.x).toBeLessThanOrEqual(WORLD_W);
      expect(p.y).toBeGreaterThanOrEqual(0);
      expect(p.y).toBeLessThanOrEqual(WORLD_H);
    }
  });

  it('keeps towns apart', () => {
    const { points } = layoutMap(map);
    const towns = map.nodes.filter(isTown).map((n) => points.get(n.id)!);
    for (let i = 0; i < towns.length; i++)
      for (let j = i + 1; j < towns.length; j++)
        expect(Math.hypot(towns[i]!.x - towns[j]!.x, towns[i]!.y - towns[j]!.y)).toBeGreaterThan(20);
  });

  it('draws each route pair once and lists direct destinations', () => {
    const pairs = routePairs(map);
    const keys = pairs.map((r) => `${Math.min(r.from, r.to)}-${Math.max(r.from, r.to)}`);
    expect(new Set(keys).size).toBe(keys.length);
    const from = map.routes[0]!.from;
    const out = destinations(map, from);
    expect(out.map((d) => d.to).sort()).toEqual(map.routes.filter((r) => r.from === from).map((r) => r.to).sort());
  });

  it('picks the nearest town within the radius, or none', () => {
    const { points } = layoutMap(map);
    const town = map.nodes.find(isTown)!;
    const p = points.get(town.id)!;
    expect(pickTown(map, points, p.x + 3, p.y - 2, 15)).toBe(town.id);
    expect(pickTown(map, points, -500, -500, 15)).toBe(-1);
  });
});

describe('camera', () => {
  it('fits the world, zooms around a point and pans', () => {
    const cam = fitCamera(500, 300);
    expect(cam.scale).toBeCloseTo(Math.min(500 / WORLD_W, 300 / WORLD_H));
    const before = toWorld(cam, 100, 80);
    const zoomed = zoomAt(cam, 2, 100, 80);
    const after = toWorld(zoomed, 100, 80);
    expect(after.x).toBeCloseTo(before.x);
    expect(after.y).toBeCloseTo(before.y);
    expect(zoomed.scale).toBeCloseTo(cam.scale * 2);
    const moved = panBy(cam, 10, -5);
    expect(moved.x - cam.x).toBe(10);
    expect(moved.y - cam.y).toBe(-5);
  });

  it('clamps the zoom', () => {
    const cam = fitCamera(500, 300);
    expect(zoomAt(cam, 1e6, 0, 0).scale).toBeLessThanOrEqual(cam.scale * 1e6);
    expect(zoomAt(cam, 1e-6, 0, 0).scale).toBeGreaterThan(0);
  });
});
