import { readFileSync } from 'node:fs';
import type { MapData } from '../src/map/layout';
import { placeName } from '../src/data/names';

const levels = ['Continent', 'Nation', 'Region', 'City', 'Village'];
const rows = readFileSync('public/data/fullscale/map_nodes.csv', 'utf8').trim().split(/\r?\n/).slice(1).map((row) => row.split(','))
  .sort((a, b) => levels.indexOf(a[2]!) - levels.indexOf(b[2]!) || a[0]!.localeCompare(b[0]!, 'en'));
const ids = new Map(rows.map((row, id) => [row[0]!, id]));
export const fullscaleMap: MapData = {
  nodes: rows.map(([key, , level, parent], id) => ({ id, key: key!, name: placeName(key!), level: levels.indexOf(level!), parent: ids.get(parent!) ?? -1, continent: Number(key![1]) })),
  routes: readFileSync('public/data/fullscale/map_routes.csv', 'utf8').trim().split(/\r?\n/).slice(1).map((row, id) => {
    const [from, to, turns] = row.split(',');
    return { id, from: ids.get(from!)!, to: ids.get(to!)!, turns: Number(turns), terrain: 0 };
  }),
};
