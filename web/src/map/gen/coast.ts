// Small seeded bays/promontories break grid-aligned coasts while keeping land connected.
import type { Grid } from './grid';
import type { Rng } from './rng';

interface Coast {
  grid: Grid;
  height: Float32Array;
  owner: Int32Array;
  touched: Uint8Array;
}

const land = (c: Coast, i: number, b: number): boolean => i >= 0 && c.height[i]! >= 0 && c.owner[i] === b;

function facesSea(c: Coast, i: number, b: number, face: number): boolean {
  if (!land(c, i, b)) return false;
  const j = c.grid.nb[i * 6 + face]!;
  return j >= 0 && c.height[j]! < 0;
}

function removable(c: Coast, i: number, b: number): boolean {
  const ring = Array.from({ length: 6 }, (_, k) => land(c, c.grid.nb[i * 6 + k]!, b));
  // A single connected fan of neighbours stays joined when its centre is removed.
  return ring.filter(Boolean).length >= 3 && ring.filter((v, k) => v !== ring[(k + 1) % 6]).length === 2;
}

/** The next ring of unseen neighbours around `ring`, or null when one of them would bridge to another landmass. */
function nextRing(c: Coast, ring: number[], seen: Set<number>, b: number): number[] | null {
  const { grid, height, owner } = c;
  const next: number[] = [];
  for (const cell of ring) for (let k = 0; k < 6; k++) {
    const j = grid.nb[cell * 6 + k]!;
    if (j < 0 || seen.has(j)) continue;
    // Do not bridge another mainland or an unassigned offshore island.
    if (height[j]! >= 0 && owner[j] !== b) return null;
    seen.add(j); next.push(j);
  }
  return next;
}

/** True when growing the sea cell `i` would bridge to another mainland or an unassigned offshore island. */
function bridgesOtherLand(c: Coast, i: number, b: number): boolean {
  let ring: number[] | null = [i]; const seen = new Set(ring);
  for (let d = 0; d < 3; d++) {
    ring = nextRing(c, ring!, seen, b);
    if (!ring) return true;
  }
  return false;
}

function canAdd(c: Coast, i: number, b: number): boolean {
  if (i < 0 || c.touched[i] || c.height[i]! >= 0) return false;
  const row = Math.floor(i / c.grid.cols);
  if (row < 2 || row >= c.grid.rows - 2) return false;
  return !bridgesOtherLand(c, i, b);
}

/** Tries to carve a bay or grow a promontory along a straight run of coast; true when it changed the map. */
function reshapeRun(c: Coast, path: number[], b: number, face: number, random: Rng): boolean {
  const start = random.int(3);
  for (let k = 0; k < 3; k++) {
    const cell = path[2 + (start + k) % 3]!;
    if (!c.touched[cell] && removable(c, cell, b)) {
      c.height[cell] = -0.01; c.owner[cell] = -1; c.touched[cell] = 1;
      return true;
    }
    const sea = c.grid.nb[cell * 6 + face]!;
    if (canAdd(c, sea, b)) { c.height[sea] = 0.06; c.owner[sea] = b; c.touched[sea] = 1; return true; }
  }
  return false;
}

/** Cells from `i` stepping in one direction while they keep facing the sea, at most 7. */
function coastRun(c: Coast, i: number, b: number, face: number, step: number): number[] {
  const path = [i]; let j = c.grid.nb[i * 6 + step]!;
  while (path.length < 7 && facesSea(c, j, b, face)) { path.push(j); j = c.grid.nb[j * 6 + step]!; }
  return path;
}

function roughenCell(c: Coast, i: number, b: number, random: Rng): boolean {
  let changed = false;
  for (let face = 0; face < 6; face++) {
    if (!facesSea(c, i, b, face)) continue;
    for (let step = 0; step < 6; step++) {
      const path = coastRun(c, i, b, face, step);
      if (path.length < 7) continue;
      if (reshapeRun(c, path, b, face, random)) changed = true;
    }
  }
  return changed;
}

export function roughenCoasts(grid: Grid, height: Float32Array, owner: Int32Array, random: Rng): void {
  const coast: Coast = { grid, height, owner, touched: new Uint8Array(grid.size) };
  for (let pass = 0; pass < 3; pass++) {
    let changed = false;
    for (let i = 0; i < grid.size; i++) {
      const b = owner[i]!;
      if (b < 0 || !land(coast, i, b)) continue;
      if (roughenCell(coast, i, b, random)) changed = true;
    }
    if (!changed) break;
  }
}
