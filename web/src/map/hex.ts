import type { Point } from './layout';

export interface Axial { q: number; r: number }
export const HEX_W = 28, HEX_H = 32;
export const hexKey = (hex: Axial): string => `${hex.q},${hex.r}`;
export const axialToPixel = ({ q, r }: Axial): Point => ({ x: HEX_W * (q + r / 2), y: 24 * r });
export const hexDistance = (a: Axial, b: Axial): number => Math.max(Math.abs(a.q - b.q), Math.abs(a.r - b.r), Math.abs(a.q + a.r - b.q - b.r));

function round(q: number, r: number): Axial {
  let x = Math.round(q), z = Math.round(r);
  const y = Math.round(-q - r), dx = Math.abs(x - q), dz = Math.abs(z - r), dy = Math.abs(y + q + r);
  if (dx > dy && dx > dz) x = -y - z;
  else if (dz > dy) z = -x - y;
  return { q: x || 0, r: z || 0 };
}
export const pixelToAxial = (x: number, y: number): Axial => round(x / HEX_W - y / 48, y / 24);
export const neighbours = ({ q, r }: Axial): Axial[] => [[1, 0], [0, 1], [-1, 1], [-1, 0], [0, -1], [1, -1]].map(([dq, dr]) => ({ q: q + dq!, r: r + dr! }));
export function hexLine(a: Axial, b: Axial): Axial[] {
  const distance = hexDistance(a, b);
  return Array.from({ length: distance + 1 }, (_, i) => {
    const t = distance ? i / distance : 0;
    return round(a.q + (b.q - a.q) * t + 1e-7, a.r + (b.r - a.r) * t + 1e-7);
  });
}
export const hexCorners = (hex: Axial): Point[] => {
  const p = axialToPixel(hex);
  return [[0, -16], [14, -8], [14, 8], [0, 16], [-14, 8], [-14, -8]].map(([x, y]) => ({ x: p.x + x!, y: p.y + y! }));
};
