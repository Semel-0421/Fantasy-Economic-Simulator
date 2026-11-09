import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { labelCandidates, placeLabels, overlaps } from '../src/map/labels';
import { layoutMap, LEVEL, type MapData } from '../src/map/layout';
import { fitCamera } from '../src/map/camera';
import { scenarioNodeKeys } from '../src/data/scenarios';

const rows = readFileSync('../engine/fixtures/fullscale/map_nodes.csv', 'utf8').trim().split(/\r?\n/).slice(1).map((row) => row.split(','));
const ids = new Map(rows.map((row, id) => [row[0]!, id]));
const levels: Record<string, number> = { Continent: 0, Nation: 1, Region: 2, City: 3, Village: 4 };
const map: MapData = { nodes: rows.map(([key, name, level, parent], id) => ({
  id, key: key!, name: name || key!, level: levels[level!]!, parent: ids.get(parent!) ?? -1, continent: 0,
})), routes: [] };
const layout = layoutMap(map);
const candidates = labelCandidates(map, layout, (name) => ({ w: name.length * 9 + 4, h: 24 }));
const player = map.nodes.find((node) => node.level === LEVEL.city)!.id;
const state = { player, selected: -1, targets: new Set<number>(), neighbours: new Set<number>() };

describe('map label placement', () => {
  it('reads scenario target nodes from the shipped RON', () => {
    expect(scenarioNodeKeys(readFileSync('../data/scenarios/s1_first_venture.ron', 'utf8'))).toContain('k0n0r0c1');
  });
  it('fits the full-scale map with at most 40 nonoverlapping labels and keeps the player', () => {
    expect(map.nodes.length).toBeGreaterThan(1000);
    for (const [width, height] of [[780, 688], [360, 333]]) {
      const placed = placeLabels(candidates, fitCamera(width!, height!), 1, state, width!, height!);
      expect(placed.length).toBeLessThanOrEqual(40);
      expect(placed.some((label) => label.id === player)).toBe(true);
      for (const [index, a] of placed.entries()) for (const b of placed.slice(index + 1)) expect(overlaps(a, b)).toBe(false);
    }
  });
  it('reveals regions and towns with zoom and gives selection priority over targets', () => {
    const pair = candidates.filter((label) => label.level === LEVEL.city).slice(0, 2);
    const first = { ...pair[0]!, x: 500, y: 500 }, second = { ...pair[1]!, x: 500, y: 500 };
    const priority = { ...state, player: -1, selected: second.id, targets: new Set([first.id]) };
    expect(placeLabels([first, second], { x: 0, y: 0, scale: 1 }, 1, priority, 1000, 1000).map((x) => x.id)).toEqual([second.id]);
    const region = candidates.find((label) => label.level === LEVEL.region)!;
    const empty = { ...state, player: -1 };
    expect(placeLabels([region], fitCamera(780, 688), 1, empty, 780, 688)).toHaveLength(0);
    expect(placeLabels([region], fitCamera(780, 688), 2, empty, 780, 688)).toHaveLength(1);
  });
  it('finishes a full-scale label pass within 4ms', () => {
    const camera = fitCamera(780, 688);
    placeLabels(candidates, camera, 1, state, 780, 688);
    const times = Array.from({ length: 30 }, () => {
      const start = performance.now();
      placeLabels(candidates, camera, 1, state, 780, 688);
      return performance.now() - start;
    });
    console.info(`Full-scale labels: ${map.nodes.length} nodes, worst ${Math.max(...times).toFixed(3)}ms, mean ${(times.reduce((a, b) => a + b, 0) / times.length).toFixed(3)}ms`);
    expect(Math.max(...times)).toBeLessThanOrEqual(4);
  });
});
