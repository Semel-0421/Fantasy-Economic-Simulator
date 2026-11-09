// A seeded, geology-based world (gen/) with the simulation's places partitioned onto its land.
import { routePairs, type Layout, type MapData, type MapNode, type Box } from './layout';
import { generateWorld } from './gen/world';
import type { Biome } from './gen/climate';
import { partitionHexes } from './hex_partition';
import { axialToPixel, hexDistance, hexKey, hexLine, pixelToAxial, type Axial } from './hex';

export type Terrain = Biome;
export interface HexTile extends Axial { continent: number; nation: number; region: number; terrain: Terrain; road: boolean; river: boolean }
export interface HexLayout extends Layout {
  /** The map seed this layout was generated from. */
  seed: string;
  hexes: Map<string, HexTile>;
  nodeHexes: Map<number, Axial>;
  occupants: Map<string, number>;
  /** River segments from a hex to the hex it drains into. */
  rivers: { from: Axial; to: Axial }[];
  landBounds: Box;
  /** Circumference in pixels (x and x + width are the same place). */
  width: number;
  /** Grid size in hexes. */
  cols: number;
  rows: number;
}

/** The world wraps east-west: the canonical copy of a hex (column within 0..cols-1). */
export function wrapHex(layout: { cols: number }, hex: Axial): Axial {
  const col = ((hex.q + Math.floor(hex.r / 2)) % layout.cols + layout.cols) % layout.cols;
  return { q: col - Math.floor(hex.r / 2), r: hex.r };
}

/** `b` moved by whole turns of the cylinder so it is as close as possible to `a`. */
export function nearestCopy(layout: { cols: number }, a: Axial, b: Axial): Axial {
  const turns = Math.round((b.q + b.r / 2 - (a.q + a.r / 2)) / layout.cols);
  return { q: b.q - turns * layout.cols, r: b.r };
}

/** Hexes on the shortest line between two hexes of the wrapped world, in canonical coordinates. */
export function wrappedLine(layout: { cols: number }, a: Axial, b: Axial): Axial[] {
  return hexLine(a, nearestCopy(layout, a, b)).map((hex) => wrapHex(layout, hex));
}

/** How good a hex is for a town: rivers and coasts first, then open, then rough land. */
const SITE: Record<Terrain, number> = { plains: 2, forest: 1, hills: 0.5, desert: -1, snow: -2, mountain: -3, water: -9 };

/** Land hexes each continent needs: one per place plus 20 % open country. */
export function continentNeeds(map: MapData): number[] {
  const kids = new Map<number, number[]>();
  for (const node of map.nodes) kids.set(node.parent, [...(kids.get(node.parent) ?? []), node.id]);
  const size = (id: number): number => 1 + (kids.get(id) ?? []).reduce((sum, child) => sum + size(child), 0);
  return [...map.nodes].filter((node) => node.parent === -1).sort((a, b) => a.id - b.id).map((root) => Math.ceil(size(root.id) * 1.2));
}

/** Ragged coasts can defeat one carving direction; try a few before giving up on a territory. */
function partition(cells: Axial[], sizes: number[], id: number, minimums: number[]): Axial[][] {
  let last: unknown;
  for (let k = 0; k < 8; k++) {
    try { return partitionHexes(cells, sizes, id + 31 * k, minimums); } catch (e) { last = e; }
  }
  throw last;
}

/**
 * Generates a seeded world (see gen/) and fits the simulation's place hierarchy onto it. If a world
 * cannot hold the hierarchy, the next variant of the same seed is tried (still deterministic).
 */
export function layoutHexMap(map: MapData, seed: string): HexLayout {
  let last: unknown;
  for (let variant = 0; variant < 6; variant++) {
    try { return layoutOnce(map, seed, variant ? `${seed}~${variant}` : seed); } catch (e) { last = e; }
  }
  throw last;
}

function layoutOnce(map: MapData, seed: string, worldSeed: string): HexLayout {
  const children = new Map<number, MapNode[]>(), counts = new Map<number, number>();
  for (const node of [...map.nodes].sort((a, b) => a.id - b.id)) {
    const siblings = children.get(node.parent) ?? [];
    siblings.push(node); children.set(node.parent, siblings);
  }
  const count = (node: MapNode): number => {
    const size = 1 + (children.get(node.id) ?? []).reduce((sum, child) => sum + count(child), 0);
    counts.set(node.id, size); return size;
  };
  const roots = children.get(-1) ?? [];
  roots.forEach(count);
  const world = generateWorld(worldSeed, continentNeeds(map));
  const { grid } = world, axial = (i: number): Axial => ({ q: grid.q[i]!, r: grid.r[i]! });
  const out: HexLayout = { seed, points: new Map(), boxes: new Map(), hexes: new Map(), nodeHexes: new Map(), occupants: new Map(), rivers: [], landBounds: { x: 0, y: 0, w: 0, h: 0 }, width: grid.width, cols: grid.cols, rows: grid.rows };
  const coastal = new Uint8Array(grid.size);
  for (let i = 0; i < grid.size; i++) {
    for (let n = 0; n < 6 && world.land[i]; n++) { const j = grid.nb[i * 6 + n]!; if (j >= 0 && !world.land[j]) coastal[i] = 1; }
    out.hexes.set(hexKey(axial(i)), { ...axial(i), continent: -1, nation: -1, region: -1, terrain: world.biome[i]!, road: false, river: world.river[i] === 1 });
  }
  const site = (hex: Axial): number => {
    const i = grid.index(hex), tile = out.hexes.get(hexKey(wrapHex(out, hex)))!;
    return SITE[tile.terrain] + (tile.river ? 3 : 0) + (coastal[i] ? 2 : 0);
  };
  const place = (node: MapNode, cells: Axial[], continent: number, nation: number, region: number): void => {
    if (node.level === 1) nation = node.id;
    if (node.level === 2) region = node.id;
    const kids = children.get(node.id) ?? [];
    // A town and its villages take the best sites of their patch: the town the very best.
    if (node.level >= 3) cells = [...cells].sort((a, b) => site(b) - site(a) || a.r - b.r || a.q - b.q);
    let remaining = cells.length - 1, remainingNeeded = kids.reduce((sum, child) => sum + counts.get(child.id)!, 0);
    const sizes = kids.map((child) => { const required = counts.get(child.id)!, size = Math.floor(remaining * required / remainingNeeded); remaining -= size; remainingNeeded -= required; return size; });
    const areas = node.level >= 3 ? [[cells[0]!], ...kids.map((_, i) => [cells[i + 1]!])]
      : kids.length ? partition(cells, [1, ...sizes], node.id, [1, ...kids.map((child) => counts.get(child.id)!)]) : [cells];
    const first = wrapHex(out, areas[0]![0]!);
    out.nodeHexes.set(node.id, first); out.points.set(node.id, axialToPixel(first)); out.occupants.set(hexKey(first), node.id);
    for (const hex of cells) Object.assign(out.hexes.get(hexKey(wrapHex(out, hex)))!, { continent, nation, region });
    if (node.level <= 2) {
      const points = cells.map(axialToPixel), xs = points.map((p) => p.x), ys = points.map((p) => p.y);
      const x = Math.min(...xs) - 14, y = Math.min(...ys) - 16;
      out.boxes.set(node.id, { x, y, w: Math.max(...xs) + 14 - x, h: Math.max(...ys) + 16 - y });
    }
    kids.forEach((child, i) => place(child, areas[i + 1]!, continent, nation, region));
  };
  // A continent may straddle the east-west seam: unwrap it round its first cell so its territories
  // are carved from one connected patch of coordinates; tiles are stored canonically.
  roots.forEach((root, k) => {
    const cells = world.continents[k]!.map(axial), ref = cells[0]!;
    place(root, cells.map((hex) => nearestCopy(out, ref, hex)), root.id, -1, -1);
  });
  for (let i = 0; i < grid.size; i++) {
    const d = world.drainage.down[i]!;
    if (world.river[i] && d >= 0) out.rivers.push({ from: axial(i), to: axial(d) });
  }
  for (const route of routePairs(map).filter((route) => route.terrain === 0).sort((a, b) => a.from - b.from || a.to - b.to)) {
    const a = out.nodeHexes.get(route.from), b = out.nodeHexes.get(route.to);
    if (a && b) for (const hex of wrappedLine(out, a, b)) { const tile = out.hexes.get(hexKey(hex)); if (tile && tile.terrain !== 'water') tile.road = true; }
  }
  const land = [...out.hexes.values()].filter((tile) => tile.continent >= 0).map(axialToPixel);
  const y = Math.min(...land.map((p) => p.y)) - 16;
  // East-west the world is the whole circumference; north-south it is the land's extent.
  out.landBounds = { x: 0, y, w: grid.width, h: Math.max(...land.map((p) => p.y)) + 16 - y };
  return out;
}

/** Share of routes whose engine terrain matches the hexes they cross (sea over water, mountain over high ground, river near a river, road over land). */
export function routeConsistency(map: MapData, layout: HexLayout): number {
  const routes = routePairs(map);
  let good = 0;
  for (const route of routes) {
    const a = layout.nodeHexes.get(route.from), b = layout.nodeHexes.get(route.to);
    if (!a || !b) continue;
    const tiles = wrappedLine(layout, a, b).map((hex) => layout.hexes.get(hexKey(hex))).filter((t): t is HexTile => !!t);
    const inner = tiles.slice(1, -1);
    const ok = route.terrain === 3 ? inner.length === 0 || inner.some((t) => t.terrain === 'water')
      : route.terrain === 1 ? tiles.some((t) => t.terrain === 'mountain' || t.terrain === 'hills' || t.terrain === 'snow')
      : route.terrain === 2 ? tiles.some((t) => t.river || t.terrain === 'water')
      : inner.every((t) => t.terrain !== 'water');
    if (ok) good++;
  }
  return routes.length ? good / routes.length : 1;
}

/** Exact occupied hex first; otherwise the closest town at most one hex away (id breaks ties). */
export function pickHexTown(map: MapData, layout: HexLayout, x: number, y: number): number {
  const hex = wrapHex(layout, pixelToAxial(x, y)), exact = layout.occupants.get(hexKey(hex));
  if (exact !== undefined) return exact;
  let chosen = -1, distance = Infinity;
  for (const node of [...map.nodes].sort((a, b) => a.id - b.id)) {
    const cell = layout.nodeHexes.get(node.id), p = layout.points.get(node.id);
    if (node.level < 3 || !cell || !p || hexDistance(nearestCopy(layout, hex, cell), hex) > 1) continue;
    const dx = (p.x - x) - layout.width * Math.round((p.x - x) / layout.width), d = Math.hypot(dx, p.y - y);
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
    const line = hexLine(a, nearestCopy(layout, a, b));
    for (let i = 1; i < line.length; i++) {
      // Canonical start, end as its nearest copy: segments that cross the seam stay short.
      const from = wrapHex(layout, line[i - 1]!), to = nearestCopy(layout, from, line[i]!);
      const key = [hexKey(from), hexKey(wrapHex(layout, to))].sort().join('|');
      if (!edges.has(key) || edges.get(key)!.terrain < route.terrain) edges.set(key, { from, to, terrain: route.terrain });
    }
  }
  return [...edges].sort(([a], [b]) => a.localeCompare(b, 'en')).map(([, edge]) => edge);
}
