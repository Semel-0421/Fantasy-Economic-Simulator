// A cylinder of pointy-top hexes stored as flat arrays (index = row * cols + col): the east and
// west edges are joined (a column past the last is the first again), north and south are not.
import { axialToPixel, type Axial } from '../hex';

export interface Grid {
  cols: number;
  rows: number;
  size: number;
  /** Circumference in pixels: x and x + width are the same place. */
  width: number;
  /** Axial coordinates per cell. */
  q: Int32Array;
  r: Int32Array;
  /** Pixel centre per cell. */
  x: Float32Array;
  y: Float32Array;
  /** Up to six neighbour indices per cell (-1 off the grid), in hex.ts `neighbours` order. */
  nb: Int32Array;
  index(hex: Axial): number;
}

/** A grid of `cols` x `rows` hexes whose rows are offset so the field is a rectangle in pixels. */
export function makeGrid(cols: number, rows: number): Grid {
  const size = cols * rows, q = new Int32Array(size), r = new Int32Array(size), x = new Float32Array(size), y = new Float32Array(size);
  for (let row = 0; row < rows; row++) for (let col = 0; col < cols; col++) {
    const i = row * cols + col, hex = { q: col - Math.floor(row / 2), r: row }, p = axialToPixel(hex);
    q[i] = hex.q; r[i] = hex.r; x[i] = p.x; y[i] = p.y;
  }
  const index = ({ q: hq, r: hr }: Axial): number => {
    if (hr < 0 || hr >= rows) return -1;
    const col = ((hq + Math.floor(hr / 2)) % cols + cols) % cols;
    return hr * cols + col;
  };
  const nb = new Int32Array(size * 6);
  const dirs = [[1, 0], [0, 1], [-1, 1], [-1, 0], [0, -1], [1, -1]] as const;
  for (let i = 0; i < size; i++) dirs.forEach(([dq, dr], k) => { nb[i * 6 + k] = index({ q: q[i]! + dq, r: r[i]! + dr }); });
  return { cols, rows, size, width: cols * 28, q, r, x, y, nb, index };
}

/** Grid dimensions for a world that needs `land` land hexes at the given land share, 5:3 in pixels. */
export function gridFor(land: number, landShare: number): { cols: number; rows: number } {
  const total = land / landShare;
  // Pixel aspect 5:3 with 28 px columns and 24 px rows: cols * 28 = (5 / 3) * rows * 24.
  // Small worlds (free mode's default map) still get a sea-girt field with room for the poles.
  const rows = Math.max(24, Math.ceil(Math.sqrt(total / (40 / 28))));
  return { cols: Math.max(40, Math.ceil(total / rows)), rows };
}

/** Shortest east-west offset on a cylinder of circumference `width`. */
export const wrapDx = (dx: number, width: number): number => dx - width * Math.round(dx / width);

/** Distance in pixels between two cells, going round the cylinder if that is shorter. */
export function cellDistance(grid: Grid, a: number, b: number): number {
  return Math.hypot(wrapDx(grid.x[b]! - grid.x[a]!, grid.width), grid.y[b]! - grid.y[a]!);
}
