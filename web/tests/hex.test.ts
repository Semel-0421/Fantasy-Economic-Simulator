import { describe, expect, it } from 'vitest';
import { axialToPixel, pixelToAxial, neighbours, hexLine, hexKey, hexDistance } from '../src/map/hex';
import { layoutHexMap, pickHexTown, hexRouteEdges } from '../src/map/hex_layout';
import { fullscaleMap as map } from './fullscale-map';
import { NATION_COLOURS, HEX_TERRAIN_COLOURS, INK } from '../src/map/colours';

describe('pointy 28x32 hex grid', () => {
  it('uses the Planner terrain palette and an ink-backed eight-colour territory border', () => {
    const luminance = (colour: number): number => {
      const channels = [colour >> 16 & 255, colour >> 8 & 255, colour & 255].map((value) => value / 255).map((v) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722;
    };
    expect(new Set(NATION_COLOURS).size).toBe(8);
    expect(HEX_TERRAIN_COLOURS).toEqual({ plains: 0x8aa374, forest: 0x3f5f43, hills: 0xa67c52, mountain: 0x7d7d7d, water: 0x2f5d62 });
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

describe('full-scale hex layout', () => {
  it('decorates route lines without changing territory or travel data', () => {
    for (const terrain of [0, 1, 2, 3]) {
      const route = { ...map.routes[0]!, terrain }, input = { ...map, routes: [route] };
      const before = JSON.stringify(input), layout = layoutHexMap(input);
      for (const hex of hexLine(layout.nodeHexes.get(route.from)!, layout.nodeHexes.get(route.to)!)) {
        const tile = layout.hexes.get(hexKey(hex))!;
        if (terrain > 0) expect(tile.terrain).toBe(['plains', 'mountain', 'water', 'water'][terrain]);
        expect(tile.road).toBe(terrain === 0);
      }
      expect(JSON.stringify(input)).toBe(before);
    }
  });
  it('keeps green terrain visible, coastlines irregular and road strokes unique', () => {
    const layout = layoutHexMap(map);
    const land = [...layout.hexes.values()].filter((tile) => tile.continent >= 0);
    expect(land.filter((tile) => tile.terrain === 'forest' || tile.terrain === 'hills').length / land.length).toBeGreaterThan(0.3);
    for (const [id, centre] of layout.islandCentres) {
      const coast = land.filter((tile) => tile.continent === id && neighbours(tile).some((n) => layout.hexes.get(hexKey(n))?.continent !== id));
      expect(new Set(coast.map((tile) => hexDistance(tile, centre))).size).toBeGreaterThanOrEqual(3);
    }
    const edges = hexRouteEdges(map, layout);
    expect(new Set(edges.map((edge) => [hexKey(edge.from), hexKey(edge.to)].sort().join('|'))).size).toBe(edges.length);
    for (const nation of map.nodes.filter((node) => node.level === 1)) {
      const points = land.filter((tile) => tile.nation === nation.id).map(axialToPixel);
      const w = Math.max(...points.map((p) => p.x)) - Math.min(...points.map((p) => p.x)) + 28;
      const h = Math.max(...points.map((p) => p.y)) - Math.min(...points.map((p) => p.y)) + 32;
      expect(Math.max(w / h, h / w)).toBeLessThan(3);
    }
  });
  it('is deterministic, separates all nodes and puts each town inside its region', () => {
    const a = layoutHexMap(map), b = layoutHexMap({ nodes: [...map.nodes].reverse(), routes: [...map.routes].reverse() });
    expect(a).toEqual(b);
    expect(a.nodeHexes.size).toBe(1580);
    expect(new Set([...a.nodeHexes.values()].map(hexKey)).size).toBe(1580);
    for (const node of map.nodes) {
      const hex = a.nodeHexes.get(node.id)!;
      expect(a.points.get(node.id)).toEqual(axialToPixel(hex));
      if (node.level < 3) continue;
      const region = node.level === 3 ? node.parent : map.nodes[node.parent]!.parent;
      expect(a.hexes.get(hexKey(hex))!.region).toBe(region);
      const p = a.points.get(node.id)!;
      expect(pickHexTown(map, a, p.x, p.y)).toBe(node.id);
    }
    expect(pickHexTown(map, a, -1000, -1000)).toBe(-1);
  });
  it('keeps every continent, nation and region connected through hex neighbours', () => {
    const layout = layoutHexMap(map);
    for (const [level, field] of [[0, 'continent'], [1, 'nation'], [2, 'region']] as const) {
      for (const node of map.nodes.filter((node) => node.level === level)) {
        const cells = [...layout.hexes.values()].filter((tile) => tile[field] === node.id);
        const pending = [cells[0]!], seen = new Set<string>(), members = new Set(cells.map(hexKey));
        while (pending.length) {
          const tile = pending.pop()!, key = hexKey(tile);
          if (seen.has(key)) continue;
          seen.add(key);
          for (const n of neighbours(tile)) if (members.has(hexKey(n)) && !seen.has(hexKey(n))) pending.push(n as typeof tile);
        }
        expect(seen.size, `${field} ${node.key}`).toBe(cells.length);
      }
    }
  });
  it('lays out the full world once within the development time budget', () => {
    const start = performance.now();
    const layout = layoutHexMap(map);
    const ms = performance.now() - start;
    console.info(`Full-scale hex layout: ${map.nodes.length} nodes, ${layout.hexes.size} tiles, ${ms.toFixed(3)}ms`);
    expect(ms).toBeLessThan(process.env.CI === 'true' ? 200 : 100);
  });
});
