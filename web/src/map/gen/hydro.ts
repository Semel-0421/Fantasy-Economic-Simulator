// Water: sea level by land share, priority-flood drainage (every land cell drains to the sea),
// lakes in filled basins, and rivers where accumulated rainfall is large.
import type { Grid } from './grid';

/** Elevation value below which the given share of cells lies. */
export function quantile(values: Float32Array, share: number): number {
  const sorted = Float32Array.from(values).sort();
  return sorted[Math.min(sorted.length - 1, Math.max(0, Math.floor(share * sorted.length)))]!;
}

/** Binary min-heap of cell indices keyed by a float priority (deterministic ties by index). */
class Heap {
  private items: number[] = [];
  constructor(private key: Float32Array) {}
  get size(): number { return this.items.length; }
  private less(a: number, b: number): boolean { return this.key[a]! < this.key[b]! || this.key[a] === this.key[b] && a < b; }
  push(i: number): void {
    const it = this.items; it.push(i);
    for (let c = it.length - 1; c > 0;) { const p = (c - 1) >> 1; if (!this.less(it[c]!, it[p]!)) break; [it[c], it[p]] = [it[p]!, it[c]!]; c = p; }
  }
  pop(): number {
    const it = this.items, top = it[0]!, last = it.pop()!;
    if (it.length) {
      it[0] = last;
      for (let c = 0; ;) {
        const l = 2 * c + 1, r = l + 1;
        let m = c;
        if (l < it.length && this.less(it[l]!, it[m]!)) m = l;
        if (r < it.length && this.less(it[r]!, it[m]!)) m = r;
        if (m === c) break;
        [it[c], it[m]] = [it[m]!, it[c]!]; c = m;
      }
    }
    return top;
  }
}

export interface Drainage {
  /** Elevation with every sink filled, so water always has a way down. */
  filled: Float32Array;
  /** Next cell downstream (-1 for sea cells). */
  down: Int32Array;
  /** Cells in a filled basin deep enough to hold a lake. */
  lake: Uint8Array;
}

/** Priority-flood from the sea (Barnes et al. style): fills sinks and records flow direction. */
export function drain(grid: Grid, h: Float32Array, sea: number): Drainage {
  const filled = h.slice(), down = new Int32Array(grid.size).fill(-1), done = new Uint8Array(grid.size), lake = new Uint8Array(grid.size);
  const heap = new Heap(filled);
  for (let i = 0; i < grid.size; i++) if (h[i]! < sea) { done[i] = 1; heap.push(i); }
  while (heap.size) {
    const c = heap.pop();
    for (let n = 0; n < 6; n++) {
      const j = grid.nb[c * 6 + n]!;
      if (j < 0 || done[j]) continue;
      done[j] = 1;
      down[j] = c;
      if (filled[j]! <= filled[c]!) filled[j] = filled[c]! + 1e-4;
      heap.push(j);
    }
  }
  for (let i = 0; i < grid.size; i++) if (h[i]! >= sea && filled[i]! - h[i]! > 0.035) lake[i] = 1;
  // A single flooded cell is a pond, not a lake.
  for (let i = 0; i < grid.size; i++) if (lake[i]) {
    let wet = 0;
    for (let n = 0; n < 6; n++) { const j = grid.nb[i * 6 + n]!; if (j >= 0 && lake[j]) wet++; }
    if (!wet) lake[i] = 0;
  }
  return { filled, down, lake };
}

/** Connected groups of land cells (largest first, then by smallest index). */
export function landComponents(grid: Grid, land: Uint8Array): number[][] {
  const seen = new Uint8Array(grid.size), out: number[][] = [];
  for (let i = 0; i < grid.size; i++) {
    if (!land[i] || seen[i]) continue;
    const comp = [i]; seen[i] = 1;
    for (let k = 0; k < comp.length; k++) {
      const c = comp[k]!;
      for (let n = 0; n < 6; n++) { const j = grid.nb[c * 6 + n]!; if (j >= 0 && land[j] && !seen[j]) { seen[j] = 1; comp.push(j); } }
    }
    out.push(comp.sort((a, b) => a - b));
  }
  return out.sort((a, b) => b.length - a.length || a[0]! - b[0]!);
}

/** Rainfall-weighted flow accumulation along `down`; returns the accumulated flow per cell. */
export function accumulate(grid: Grid, drainage: Drainage, rain: Float32Array, land: Uint8Array): Float32Array {
  const order = Array.from({ length: grid.size }, (_, i) => i).filter((i) => land[i]).sort((a, b) => drainage.filled[b]! - drainage.filled[a]! || a - b);
  const flow = new Float32Array(grid.size);
  for (const i of order) {
    flow[i] = flow[i]! + rain[i]!;
    const d = drainage.down[i]!;
    if (d >= 0) flow[d] = flow[d]! + flow[i]!;
  }
  return flow;
}
