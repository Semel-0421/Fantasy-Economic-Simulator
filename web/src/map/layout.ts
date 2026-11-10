// Deterministic map layout. The map data (map_nodes.csv) has no coordinates, so positions follow
// the node tree: the world box is split into a grid of continents, each continent into a grid of
// its nations, and so on down to regions; a city sits in the middle of its box and its villages
// ring it. Same map in, same positions out; nothing random.
import type { Views } from '../sim/types';

export type MapData = Views['map'];
export type MapNode = MapData['nodes'][number];
export type MapRoute = MapData['routes'][number];

export const WORLD_W = 2000;
export const WORLD_H = 1200;

export const LEVEL = { continent: 0, nation: 1, region: 2, city: 3, village: 4 } as const;
export const TERRAIN_NAMES = ['Road', 'Mountain', 'River', 'Sea'] as const;

export interface Point {
  x: number;
  y: number;
}
export interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface Layout {
  points: Map<number, Point>;
  /** The box each continent, nation and region was given (for outlines and labels). */
  boxes: Map<number, Box>;
}

export const isTown = (n: MapNode): boolean => n.level >= LEVEL.city;

function childrenOf(map: MapData): Map<number, MapNode[]> {
  const out = new Map<number, MapNode[]>();
  for (const n of map.nodes) {
    const list = out.get(n.parent) ?? [];
    list.push(n);
    out.set(n.parent, list);
  }
  for (const list of out.values()) list.sort((a, b) => a.id - b.id);
  return out;
}

/** Splits a box into a near-square grid of `k` cells, inset by a margin. */
function grid(box: Box, k: number): Box[] {
  const cols = Math.ceil(Math.sqrt(k * (box.w / box.h)));
  const c = Math.max(1, Math.min(k, cols));
  const rows = Math.ceil(k / c);
  const w = box.w / c;
  const h = box.h / rows;
  return Array.from({ length: k }, (_, i) => {
    const m = Math.min(w, h) * 0.04;
    return { x: box.x + (i % c) * w + m, y: box.y + Math.floor(i / c) * h + m, w: w - 2 * m, h: h - 2 * m };
  });
}

function placeTown(city: MapNode, box: Box, kids: Map<number, MapNode[]>, out: Layout): void {
  const cx = box.x + box.w / 2;
  const cy = box.y + box.h / 2;
  out.points.set(city.id, { x: cx, y: cy });
  const villages = kids.get(city.id) ?? [];
  const r = Math.min(box.w, box.h) * 0.36;
  villages.forEach((v, i) => {
    const a = -Math.PI / 2 + (2 * Math.PI * i) / villages.length;
    out.points.set(v.id, { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a) });
  });
}

function placeChildren(node: MapNode, box: Box, kids: Map<number, MapNode[]>, out: Layout): void {
  const list = kids.get(node.id) ?? [];
  if (list.length === 0) return;
  grid(box, list.length).forEach((cell, i) => place(list[i] as MapNode, cell, kids, out));
}

function place(node: MapNode, box: Box, kids: Map<number, MapNode[]>, out: Layout): void {
  if (node.level === LEVEL.city) {
    placeTown(node, box, kids, out);
    return;
  }
  if (node.level === LEVEL.village) {
    // A village whose parent is not a city: it gets its own box.
    out.points.set(node.id, { x: box.x + box.w / 2, y: box.y + box.h / 2 });
    return;
  }
  out.boxes.set(node.id, box);
  out.points.set(node.id, { x: box.x + box.w / 2, y: box.y + box.h / 2 });
  placeChildren(node, box, kids, out);
}

export function layoutMap(map: MapData): Layout {
  const out: Layout = { points: new Map(), boxes: new Map() };
  const kids = childrenOf(map);
  const roots = kids.get(-1) ?? [];
  grid({ x: 0, y: 0, w: WORLD_W, h: WORLD_H }, roots.length).forEach((cell, i) => place(roots[i] as MapNode, cell, kids, out));
  // Anything unreachable from a continent still gets a point, in the corner.
  for (const n of map.nodes) if (!out.points.has(n.id)) out.points.set(n.id, { x: 0, y: 0 });
  return out;
}

/** Each unordered pair of linked nodes once, with the shorter of its two directions' times. */
export function routePairs(map: MapData): MapRoute[] {
  const seen = new Map<string, MapRoute>();
  for (const r of map.routes) {
    const key = `${Math.min(r.from, r.to)}-${Math.max(r.from, r.to)}`;
    const old = seen.get(key);
    if (!old || r.turns < old.turns || r.turns === old.turns && (r.from < old.from || r.from === old.from && r.terrain < old.terrain)) seen.set(key, r);
  }
  return [...seen.values()];
}

/** Routes leaving a node: where Travel can go from there. */
export function destinations(map: MapData, from: number): MapRoute[] {
  return map.routes.filter((r) => r.from === from);
}

/** The town nearest a world point within `radius`, or -1. */
export function pickTown(map: MapData, points: Map<number, Point>, x: number, y: number, radius: number): number {
  let best = -1;
  let bestD = radius;
  for (const n of map.nodes) {
    if (!isTown(n)) continue;
    const p = points.get(n.id);
    if (!p) continue;
    const d = Math.hypot(p.x - x, p.y - y);
    if (d <= bestD) {
      best = n.id;
      bestD = d;
    }
  }
  return best;
}
