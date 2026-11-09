import { describe, expect, it } from 'vitest';
import { axialToPixel, pixelToAxial, neighbours, hexLine, hexKey, hexDistance } from '../src/map/hex';
import { layoutHexMap, pickHexTown, hexRouteEdges, nearestCopy, wrapHex, type HexTile } from '../src/map/hex_layout';
import { fullscaleMap as map } from './fullscale-map';
import { NATION_COLOURS, HEX_TERRAIN_COLOURS, INK } from '../src/map/colours';

describe('pointy 28x32 hex grid', () => {
  it('uses the Planner terrain palette and an ink-backed eight-colour territory border', () => {
    const luminance = (colour: number): number => {
      const channels = [colour >> 16 & 255, colour >> 8 & 255, colour & 255].map((value) => value / 255).map((v) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722;
    };
    expect(new Set(NATION_COLOURS).size).toBe(8);
    expect(HEX_TERRAIN_COLOURS).toEqual({ plains: 0x8aa374, forest: 0x3f5f43, hills: 0xa67c52, mountain: 0x7d7d7d, water: 0x2f5d62, desert: 0xe4d4ae, snow: 0xf3e9d2 });
    expect((luminance(HEX_TERRAIN_COLOURS.plains) + 0.05) / (luminance(INK) + 0.05)).toBeGreaterThanOrEqual(3);
  });
  it('round trips centres and identifies their six neighbours and a continuous line', () => {
    for (const q of [-8, 0, 7]) for (const r of [-5, 0, 4]) {
      const hex = { q, r }, p = axialToPixel(hex);
      expect(pixelToAxial(p.x + 1, p.y - 2)).toEqual(hex);
      expect(neighbours(hex)).toHaveLength(6);
      for (const n of neighbours(hex)) expect(hexDistance(hex, n)).toBe(1);
    }
    const a = { q: -4, r: 3 }, b = { q: 6, r: -2 }, line = hexLine(a, b);
    expect(line[0]).toEqual(a); expect(line.at(-1)).toEqual(b);
    expect(line).toHaveLength(hexDistance(a, b) + 1);
    for (let i = 1; i < line.length; i++) expect(hexDistance(line[i - 1]!, line[i]!)).toBe(1);
  });
});

describe('full-scale seeded hex layout (wrapped east-west)', () => {
  const layout = layoutHexMap(map, 'scenario-1');
  const at = (hex: { q: number; r: number }): HexTile | undefined => layout.hexes.get(hexKey(wrapHex(layout, hex)));
  it('is deterministic per seed, independent of input order, and differs between seeds', () => {
    const again = layoutHexMap({ nodes: [...map.nodes].reverse(), routes: [...map.routes].reverse() }, 'scenario-1');
    expect(again).toEqual(layout);
    const other = layoutHexMap(map, 'sample-7');
    expect([...other.hexes.values()].map((t) => t.terrain).join()).not.toBe([...layout.hexes.values()].map((t) => t.terrain).join());
  });
  it('puts every place on its own land hex inside its region, and picks it back', () => {
    expect(layout.nodeHexes.size).toBe(1580);
    expect(new Set([...layout.nodeHexes.values()].map(hexKey)).size).toBe(1580);
    for (const node of map.nodes) {
      const hex = layout.nodeHexes.get(node.id)!, tile = at(hex)!;
      expect(layout.points.get(node.id)).toEqual(axialToPixel(hex));
      expect(tile.terrain, node.key).not.toBe('water');
      if (node.level < 3) continue;
      const region = node.level === 3 ? node.parent : map.nodes[node.parent]!.parent;
      expect(tile.region).toBe(region);
      const p = layout.points.get(node.id)!;
      expect(pickHexTown(map, layout, p.x, p.y)).toBe(node.id);
      // The same place is picked one full turn round the cylinder.
      expect(pickHexTown(map, layout, p.x + layout.width, p.y)).toBe(node.id);
    }
    expect(pickHexTown(map, layout, 10, -1000)).toBe(-1);
  });
  for (const seed of ['scenario-1', 'sample-7']) {
    it(`keeps every continent, nation and region connected across the seam (${seed})`, () => {
      const l = seed === 'scenario-1' ? layout : layoutHexMap(map, seed);
      for (const [level, field] of [[0, 'continent'], [1, 'nation'], [2, 'region']] as const) {
        for (const node of map.nodes.filter((n) => n.level === level)) {
          const cells = [...l.hexes.values()].filter((tile) => tile[field] === node.id);
          const pending = [cells[0]!], seen = new Set<string>(), members = new Set(cells.map(hexKey));
          while (pending.length) {
            const tile = pending.pop()!, key = hexKey(tile);
            if (seen.has(key)) continue;
            seen.add(key);
            for (const n of neighbours(tile)) {
              const k = hexKey(wrapHex(l, n));
              if (members.has(k) && !seen.has(k)) pending.push(l.hexes.get(k)!);
            }
          }
          expect(seen.size, `${field} ${node.key}`).toBe(cells.length);
        }
      }
    });
  }
  it('has realistic proportions: separate continents, mountains, rivers that reach water', () => {
    const land = [...layout.hexes.values()].filter((t) => t.terrain !== 'water');
    expect(land.length / layout.hexes.size).toBeGreaterThan(0.2);
    expect(land.length / layout.hexes.size).toBeLessThan(0.45);
    expect(land.filter((t) => t.terrain === 'mountain').length / land.length).toBeGreaterThan(0.03);
    expect(layout.rivers.length).toBeGreaterThan(20);
    const down = new Map(layout.rivers.map((r) => [hexKey(r.from), r.to]));
    for (const r of layout.rivers) {
      expect(hexDistance(r.from, nearestCopy(layout, r.from, r.to))).toBe(1);
      let hex = r.to, steps = 0;
      while (down.has(hexKey(wrapHex(layout, hex))) && steps++ < 400) hex = down.get(hexKey(wrapHex(layout, hex)))!;
      const end = at(hex)!;
      // A river ends in the sea, a lake, or where it leaves the river network.
      expect(end.terrain === 'water' || !end.river).toBe(true);
    }
    for (const id of new Set(land.map((t) => t.continent).filter((c) => c >= 0))) {
      // No land hex of one continent touches another continent: the sea keeps them apart.
      for (const tile of land.filter((t) => t.continent === id)) for (const n of neighbours(tile)) {
        const other = at(n);
        if (other && other.continent >= 0) expect(other.continent).toBe(id);
      }
    }
  });
  it('draws each road edge once and marks road hexes only on land', () => {
    const edges = hexRouteEdges(map, layout);
    expect(new Set(edges.map((e) => [hexKey(e.from), hexKey(wrapHex(layout, e.to))].sort().join('|'))).size).toBe(edges.length);
    for (const tile of layout.hexes.values()) if (tile.road) expect(tile.terrain).not.toBe('water');
  });
  it('generates the full world within the time budget (it runs in a Web Worker in the game)', () => {
    const start = performance.now();
    layoutHexMap(map, 'sample-5');
    const ms = performance.now() - start;
    console.info(`Full-scale seeded layout: ${ms.toFixed(1)}ms`);
    expect(ms).toBeLessThan(process.env.CI === 'true' ? 3000 : 1200);
  });
});

