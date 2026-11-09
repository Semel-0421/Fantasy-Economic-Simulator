import { axialToPixel, hexKey, neighbours, type Axial } from './hex';

/** Carve compact connected patches while keeping the unallocated remainder connected. */
export function partitionHexes(cells: Axial[], sizes: number[], seed: number, minimums = sizes): Axial[][] {
  const ids = new Map(cells.map((cell, i) => [hexKey(cell), i]));
  const graph = cells.map((cell) => neighbours(cell).map((n) => ids.get(hexKey(n))).filter((id): id is number => id !== undefined));
  const points = cells.map(axialToPixel), active = new Uint8Array(cells.length).fill(1), seen = new Int32Array(cells.length);
  let remaining = cells.length, epoch = 0;
  const removable = (id: number): boolean => {
    const adjacent = graph[id]!.filter((n) => active[n]);
    if (adjacent.length <= 1) return remaining <= 1 || adjacent.length === 1;
    const target = new Set(adjacent), pending = [adjacent[0]!];
    epoch++; seen[adjacent[0]!] = epoch;
    let cursor = 0;
    while (cursor < pending.length) {
      const current = pending[cursor++]!;
      target.delete(current);
      if (!target.size) return true;
      for (const n of graph[current]!) if (n !== id && active[n] && seen[n] !== epoch) { seen[n] = epoch; pending.push(n); }
    }
    return false;
  };
  const out: Axial[][] = [];
  for (const [part, size] of sizes.entries()) {
    if (part === sizes.length - 1) { out.push(cells.filter((_, i) => active[i])); break; }
    const angle = (seed % 31) * Math.PI / 15;
    const central = part === 0 && size === 1;
    const centre = points.reduce((p, point) => ({ x: p.x + point.x / points.length, y: p.y + point.y / points.length }), { x: 0, y: 0 });
    const distance = (id: number): number => (points[id]!.x - centre.x) ** 2 + (points[id]!.y - centre.y) ** 2;
    const boundary = cells.map((_, id) => id).filter((id) => active[id] && (central || graph[id]!.some((n) => !active[n]) || graph[id]!.length < 6))
      .sort((a, b) => central ? distance(a) - distance(b) || a - b : (points[b]!.x - points[a]!.x) * Math.cos(angle) + (points[b]!.y - points[a]!.y) * Math.sin(angle) || a - b);
    const snapshot = active.slice(), before = remaining;
    let picked: number[] | null = null;
    for (let attempt = 0; attempt < Math.min(16, boundary.length) && !picked; attempt++) {
      active.set(snapshot); remaining = before;
      const first = boundary[Math.floor(attempt * boundary.length / Math.min(16, boundary.length))]!;
      if (!removable(first)) continue;
      const origin = points[first]!, frontier = new Set([first]), chunk: number[] = [];
      const score = (id: number): number => (points[id]!.x - origin.x) ** 2 + (points[id]!.y - origin.y) ** 2;
      while (chunk.length < size) {
        const next = [...frontier].sort((a, b) => score(a) - score(b) || a - b).find(removable);
        if (next === undefined) break;
        active[next] = 0; remaining--; frontier.delete(next); chunk.push(next);
        for (const n of graph[next]!) if (active[n]) frontier.add(n);
      }
      const reserve = minimums.slice(part + 1).reduce((a, b) => a + b, 0);
      if (chunk.length >= minimums[part]! && remaining >= reserve) picked = chunk;
    }
    if (!picked) throw new Error(`Could not partition hex territory ${seed}: part ${part}, size ${size}, ${cells.length} cells, ${before} remaining`);
    out.push(picked.map((id) => cells[id]!));
  }
  return out;
}
