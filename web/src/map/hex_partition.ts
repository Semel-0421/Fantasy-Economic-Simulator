import { axialToPixel, hexKey, neighbours, type Axial } from './hex';

/** The cells still free, the neighbour graph over them, and the scratch space for connectivity checks. */
interface Carver {
  graph: number[][];
  points: { x: number; y: number }[];
  active: Uint8Array;
  seen: Int32Array;
  remaining: number;
  epoch: number;
}

/** True when taking the cell out leaves the free cells around it connected. */
function removable(s: Carver, id: number): boolean {
  const { graph, active, seen } = s;
  const adjacent = graph[id]!.filter((n) => active[n]);
  if (adjacent.length <= 1) return s.remaining <= 1 || adjacent.length === 1;
  const target = new Set(adjacent), pending = [adjacent[0]!];
  s.epoch++; seen[adjacent[0]!] = s.epoch;
  let cursor = 0;
  while (cursor < pending.length) {
    const current = pending[cursor++]!;
    target.delete(current);
    if (!target.size) return true;
    for (const n of graph[current]!) if (n !== id && active[n] && seen[n] !== s.epoch) { seen[n] = s.epoch; pending.push(n); }
  }
  return false;
}

/** Free cells on the rim of the remainder, ordered from where the next patch should start. */
function boundaryCells(s: Carver, part: number, size: number, seed: number): number[] {
  const { graph, points, active } = s;
  const angle = (seed % 31) * Math.PI / 15;
  const central = part === 0 && size === 1;
  const centre = points.reduce((p, point) => ({ x: p.x + point.x / points.length, y: p.y + point.y / points.length }), { x: 0, y: 0 });
  const distance = (id: number): number => (points[id]!.x - centre.x) ** 2 + (points[id]!.y - centre.y) ** 2;
  return points.map((_, id) => id).filter((id) => active[id] && (central || graph[id]!.some((n) => !active[n]) || graph[id]!.length < 6))
    .sort((a, b) => central ? distance(a) - distance(b) || a - b : (points[b]!.x - points[a]!.x) * Math.cos(angle) + (points[b]!.y - points[a]!.y) * Math.sin(angle) || a - b);
}

/** Takes up to size free cells, nearest to first, never cutting the remainder apart. */
function growChunk(s: Carver, first: number, size: number): number[] {
  const { graph, points, active } = s;
  const origin = points[first]!, frontier = new Set([first]), chunk: number[] = [];
  const score = (id: number): number => (points[id]!.x - origin.x) ** 2 + (points[id]!.y - origin.y) ** 2;
  while (chunk.length < size) {
    const next = [...frontier].sort((a, b) => score(a) - score(b) || a - b).find((id) => removable(s, id));
    if (next === undefined) break;
    active[next] = 0; s.remaining--; frontier.delete(next); chunk.push(next);
    for (const n of graph[next]!) if (active[n]) frontier.add(n);
  }
  return chunk;
}

/** Tries up to 16 starting cells along the boundary; null when none yields a big enough patch. */
function pickChunk(s: Carver, boundary: number[], part: number, size: number, minimums: number[]): number[] | null {
  const snapshot = s.active.slice(), before = s.remaining;
  const tries = Math.min(16, boundary.length);
  for (let attempt = 0; attempt < tries; attempt++) {
    s.active.set(snapshot); s.remaining = before;
    const first = boundary[Math.floor(attempt * boundary.length / tries)]!;
    if (!removable(s, first)) continue;
    const chunk = growChunk(s, first, size);
    const reserve = minimums.slice(part + 1).reduce((a, b) => a + b, 0);
    if (chunk.length >= minimums[part]! && s.remaining >= reserve) return chunk;
  }
  return null;
}

/** Carve compact connected patches while keeping the unallocated remainder connected. */
export function partitionHexes(cells: Axial[], sizes: number[], seed: number, minimums = sizes): Axial[][] {
  const ids = new Map(cells.map((cell, i) => [hexKey(cell), i]));
  const graph = cells.map((cell) => neighbours(cell).map((n) => ids.get(hexKey(n))).filter((id): id is number => id !== undefined));
  const s: Carver = { graph, points: cells.map(axialToPixel), active: new Uint8Array(cells.length).fill(1), seen: new Int32Array(cells.length), remaining: cells.length, epoch: 0 };
  const out: Axial[][] = [];
  for (const [part, size] of sizes.entries()) {
    if (part === sizes.length - 1) { out.push(cells.filter((_, i) => s.active[i])); break; }
    const before = s.remaining;
    const picked = pickChunk(s, boundaryCells(s, part, size, seed), part, size, minimums);
    if (!picked) throw new Error(`Could not partition hex territory ${seed}: part ${part}, size ${size}, ${cells.length} cells, ${before} remaining`);
    out.push(picked.map((id) => cells[id]!));
  }
  return out;
}
