// Organic, deterministic islands partitioned into compact connected hierarchy territories.
import { WORLD_H, WORLD_W, routePairs, type Layout, type MapData, type MapNode, type Box } from './layout';
import { partitionHexes } from './hex_partition';
import { axialToPixel, hexDistance, hexKey, hexLine, neighbours, pixelToAxial, type Axial } from './hex';

export type Terrain = 'plains' | 'forest' | 'hills' | 'mountain' | 'water';
export interface HexTile extends Axial { continent: number; nation: number; region: number; terrain: Terrain; road: boolean }
export interface HexLayout extends Layout {
  hexes: Map<string, HexTile>;
  nodeHexes: Map<number, Axial>;
  occupants: Map<string, number>;
  islandCentres: Map<number, Axial>;
  landBounds: Box;
}

function island(radius: number, centre: Axial, bounds: Box, seed: number): Axial[] {
  const candidates = new Map<string, Axial>();
  const phase = seed * 0.73;
  for (let r = -radius - 3; r <= radius + 3; r++) for (let q = -radius - 3; q <= radius + 3; q++) {
    const hex = { q: q + centre.q, r: r + centre.r }, p = axialToPixel(hex), local = axialToPixel({ q, r });
    const angle = Math.atan2(local.y, local.x);
    const hash = (Math.imul(hex.q, 73856093) ^ Math.imul(hex.r, 19349663)) >>> 0;
    const coast = radius + 1.5 * Math.sin(3 * angle + phase) + Math.sin(5 * angle - phase)
      + 2 * Math.max(0, Math.cos(angle + phase)) ** 12 - 2 * Math.max(0, Math.cos(angle - phase - 1.5)) ** 10 + (hash % 101 / 100 - 0.5);
    if (hexDistance({ q, r }, { q: 0, r: 0 }) <= coast && p.x >= bounds.x && p.x <= bounds.x + bounds.w && p.y >= bounds.y && p.y <= bounds.y + bounds.h) candidates.set(hexKey(hex), hex);
  }
  const pending = [centre], connected = new Map<string, Axial>();
  while (pending.length) {
    const hex = pending.pop()!, key = hexKey(hex);
    if (connected.has(key) || !candidates.has(key)) continue;
    connected.set(key, hex); pending.push(...neighbours(hex));
  }
  return [...connected.values()].sort((a, b) => a.r - b.r || a.q - b.q);
}

export function layoutHexMap(map: MapData): HexLayout {
  const out: HexLayout = { points: new Map(), boxes: new Map(), hexes: new Map(), nodeHexes: new Map(), occupants: new Map(), islandCentres: new Map(), landBounds: { x: 0, y: 0, w: WORLD_W, h: WORLD_H } };
  const children = new Map<number, MapNode[]>(), counts = new Map<number, number>();
  for (const node of [...map.nodes].sort((a, b) => a.id - b.id)) {
    const siblings = children.get(node.parent) ?? [];
    siblings.push(node); children.set(node.parent, siblings);
  }
  const count = (node: MapNode): number => {
    const size = 1 + (children.get(node.id) ?? []).reduce((sum, child) => sum + count(child), 0);
    counts.set(node.id, size); return size;
  };
  const place = (node: MapNode, cells: Axial[], continent: number, nation: number, region: number): void => {
    if (node.level === 1) nation = node.id;
    if (node.level === 2) region = node.id;
    const kids = children.get(node.id) ?? [];
    let remaining = cells.length - 1, remainingNeeded = kids.reduce((sum, child) => sum + counts.get(child.id)!, 0);
    const sizes = kids.map((child) => { const required = counts.get(child.id)!, size = Math.floor(remaining * required / remainingNeeded); remaining -= size; remainingNeeded -= required; return size; });
    const areas = node.level >= 3 ? [[cells[0]!], ...kids.map((_, i) => [cells[i + 1]!])]
      : kids.length ? partitionHexes(cells, [1, ...sizes], node.id, [1, ...kids.map((child) => counts.get(child.id)!)]) : [cells];
    const first = areas[0]![0]!;
    out.nodeHexes.set(node.id, first); out.points.set(node.id, axialToPixel(first)); out.occupants.set(hexKey(first), node.id);
    for (const hex of cells) out.hexes.set(hexKey(hex), { ...hex, continent, nation, region, terrain: 'plains', road: false });
    if (node.level <= 2) {
      const points = cells.map(axialToPixel), xs = points.map((p) => p.x), ys = points.map((p) => p.y);
      const x = Math.min(...xs) - 14, y = Math.min(...ys) - 16;
      out.boxes.set(node.id, { x, y, w: Math.max(...xs) + 14 - x, h: Math.max(...ys) + 16 - y });
    }
    kids.forEach((child, i) => place(child, areas[i + 1]!, continent, nation, region));
  };
  const roots = children.get(-1) ?? [];
  const cols = Math.ceil(Math.sqrt(roots.length * WORLD_W / WORLD_H)), rows = Math.ceil(roots.length / cols);
  roots.forEach((root, i) => {
    const needed = count(root);
    const centre = pixelToAxial((i % cols + 0.5) * WORLD_W / cols, (Math.floor(i / cols) + 0.5) * WORLD_H / rows);
    out.islandCentres.set(root.id, centre);
    const bounds = { x: i % cols * WORLD_W / cols + 28, y: Math.floor(i / cols) * WORLD_H / rows + 32, w: WORLD_W / cols - 56, h: WORLD_H / rows - 64 };
    let radius = Math.max(1, Math.ceil(Math.sqrt(needed * 1.2 / 3)) - 2), cells: Axial[] = [];
    while (cells.length < Math.ceil(needed * 1.2)) {
      if (++radius > 40) throw new Error('World hierarchy exceeds hex map bounds');
      cells = island(radius, centre, bounds, root.id);
    }
    place(root, cells, root.id, -1, -1);
  });
  // Water fills the same fixed world bounds as the existing camera contract.
  for (let r = 0; r <= WORLD_H / 24; r++) for (let col = 0; col <= WORLD_W / 28; col++) {
    const hex = { q: col - Math.floor(r / 2), r }, key = hexKey(hex);
    if (!out.hexes.has(key)) out.hexes.set(key, { ...hex, continent: -1, nation: -1, region: -1, terrain: 'water', road: false });
  }
  const routes = routePairs(map).sort((a, b) => a.from - b.from || a.to - b.to);
  const mountainNeighbours = new Set<string>();
  for (const route of routes.filter((route) => route.terrain === 1)) {
    const a = out.nodeHexes.get(route.from), b = out.nodeHexes.get(route.to);
    if (a && b) for (const hex of hexLine(a, b)) for (const n of neighbours(hex)) mountainNeighbours.add(hexKey(n));
  }
  for (const [key, tile] of out.hexes) if (tile.continent >= 0) {
    const hash = (Math.imul(tile.q, 73856093) ^ Math.imul(tile.r, 19349663)) >>> 0;
    tile.terrain = hash % 100 < (mountainNeighbours.has(key) ? 70 : 15) ? 'hills' : hash % 100 < 45 ? 'forest' : 'plains';
  }
  for (const route of routes) {
    const a = out.nodeHexes.get(route.from), b = out.nodeHexes.get(route.to);
    if (!a || !b) continue;
    for (const hex of hexLine(a, b)) {
      const tile = out.hexes.get(hexKey(hex));
      if (!tile) continue;
      if (route.terrain === 1) tile.terrain = 'mountain';
      else if (route.terrain >= 2) tile.terrain = 'water';
      tile.road ||= route.terrain === 0;
    }
  }
  const land = [...out.hexes.values()].filter((tile) => tile.continent >= 0).map(axialToPixel);
  const x = Math.min(...land.map((p) => p.x)) - 14, y = Math.min(...land.map((p) => p.y)) - 16;
  out.landBounds = { x, y, w: Math.max(...land.map((p) => p.x)) + 14 - x, h: Math.max(...land.map((p) => p.y)) + 16 - y };
  return out;
}

/** Exact occupied hex first; otherwise the closest town at most one hex away (id breaks ties). */
export function pickHexTown(map: MapData, layout: HexLayout, x: number, y: number): number {
  const hex = pixelToAxial(x, y), exact = layout.occupants.get(hexKey(hex));
  if (exact !== undefined) return exact;
  let chosen = -1, distance = Infinity;
  for (const node of [...map.nodes].sort((a, b) => a.id - b.id)) {
    const cell = layout.nodeHexes.get(node.id), p = layout.points.get(node.id);
    if (node.level < 3 || !cell || !p || hexDistance(cell, hex) > 1) continue;
    const d = Math.hypot(p.x - x, p.y - y);
    if (d < distance) { distance = d; chosen = node.id; }
  }
  return chosen;
}


/** Multiple engine routes can share a map edge; paint each edge once to prevent dark streaks. */
export function hexRouteEdges(map: MapData, layout: HexLayout): { from: Axial; to: Axial; terrain: number }[] {
  const edges = new Map<string, { from: Axial; to: Axial; terrain: number }>();
  for (const route of routePairs(map).sort((a, b) => a.from - b.from || a.to - b.to)) {
    const a = layout.nodeHexes.get(route.from), b = layout.nodeHexes.get(route.to);
    if (!a || !b) continue;
    const line = hexLine(a, b);
    for (let i = 1; i < line.length; i++) {
      const from = line[i - 1]!, to = line[i]!, key = [hexKey(from), hexKey(to)].sort().join('|');
      if (!edges.has(key) || edges.get(key)!.terrain < route.terrain) edges.set(key, { from, to, terrain: route.terrain });
    }
  }
  return [...edges].sort(([a], [b]) => a.localeCompare(b, 'en')).map(([, edge]) => edge);
}
