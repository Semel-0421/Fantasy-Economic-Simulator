// Small seeded bays/promontories break grid-aligned coasts while keeping land connected.
import type { Grid } from './grid';
import type { Rng } from './rng';

export function roughenCoasts(grid: Grid, height: Float32Array, owner: Int32Array, random: Rng): void {
  const touched = new Uint8Array(grid.size);
  const land = (i: number, b: number): boolean => i >= 0 && height[i]! >= 0 && owner[i] === b;
  const facesSea = (i: number, b: number, face: number): boolean => {
    if (!land(i, b)) return false;
    const j = grid.nb[i * 6 + face]!;
    return j >= 0 && height[j]! < 0;
  };
  const removable = (i: number, b: number): boolean => {
    const ring = Array.from({ length: 6 }, (_, k) => land(grid.nb[i * 6 + k]!, b));
    // A single connected fan of neighbours stays joined when its centre is removed.
    return ring.filter(Boolean).length >= 3 && ring.filter((v, k) => v !== ring[(k + 1) % 6]).length === 2;
  };
  const canAdd = (i: number, b: number): boolean => {
    if (i < 0 || touched[i] || height[i]! >= 0) return false;
    const row = Math.floor(i / grid.cols);
    if (row < 2 || row >= grid.rows - 2) return false;
    let ring = [i]; const seen = new Set(ring);
    for (let d = 0; d < 3; d++) {
      const next: number[] = [];
      for (const c of ring) for (let k = 0; k < 6; k++) {
        const j = grid.nb[c * 6 + k]!;
        if (j < 0 || seen.has(j)) continue;
        // Do not bridge another mainland or an unassigned offshore island.
        if (height[j]! >= 0 && owner[j] !== b) return false;
        seen.add(j); next.push(j);
      }
      ring = next;
    }
    return true;
  };
  for (let pass = 0; pass < 3; pass++) {
    let changed = false;
    for (let i = 0; i < grid.size; i++) {
      const b = owner[i]!;
      if (b < 0 || !land(i, b)) continue;
      for (let face = 0; face < 6; face++) if (facesSea(i, b, face)) for (let step = 0; step < 6; step++) {
        const path = [i]; let j = grid.nb[i * 6 + step]!;
        while (path.length < 7 && facesSea(j, b, face)) { path.push(j); j = grid.nb[j * 6 + step]!; }
        if (path.length < 7) continue;
        const start = random.int(3);
        for (let k = 0; k < 3; k++) {
          const c = path[2 + (start + k) % 3]!;
          if (!touched[c] && removable(c, b)) {
            height[c] = -0.01; owner[c] = -1; touched[c] = 1; changed = true; break;
          }
          const sea = grid.nb[c * 6 + face]!;
          if (canAdd(sea, b)) { height[sea] = 0.06; owner[sea] = b; touched[sea] = 1; changed = true; break; }
        }
      }
    }
    if (!changed) break;
  }
}
