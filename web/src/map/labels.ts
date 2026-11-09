import { LEVEL, type Box, type Layout, type MapData } from './layout';
import type { Camera } from './camera';

export interface Label extends Box { id: number; level: number }
export interface LabelState { player: number; selected: number; targets: Set<number>; neighbours: Set<number> }

export function labelCandidates(map: MapData, layout: Layout, measure: (name: string) => { w: number; h: number }): Label[] {
  return map.nodes.map((node) => {
    const point = layout.points.get(node.id)!;
    const box = layout.boxes.get(node.id);
    return { id: node.id, level: node.level, x: point.x, y: box ? box.y : point.y, ...measure(node.name) };
  });
}
export const overlaps = (a: Box, b: Box): boolean => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;

/** Greedy placement is in CSS pixels: font size and the collision margin never shrink with zoom. */
export function placeLabels(labels: Label[], camera: Camera, zoom: number, state: LabelState, width: number, height: number): Label[] {
  const priority = (label: Label): number => label.id === state.player ? 0 : label.id === state.selected ? 1
    : state.targets.has(label.id) ? 2 : state.neighbours.has(label.id) ? 3 : 4 + label.level;
  const eligible = labels.filter((label) => label.level <= LEVEL.nation || priority(label) < 4 || (label.level === LEVEL.region ? zoom >= 2 : zoom >= 4));
  eligible.sort((a, b) => priority(a) - priority(b) || a.id - b.id);
  const placed: Label[] = [];
  for (const label of eligible) {
    const cx = label.x * camera.scale + camera.x, cy = label.y * camera.scale + camera.y;
    if (cx < 0 || cx > width || cy < 0 || cy > height) continue;
    const x = Math.max(2, Math.min(width - label.w - 2, cx - label.w / 2));
    const y = Math.max(2, Math.min(height - label.h - 2, cy + (label.level >= LEVEL.city ? 12 : 4)));
    const box = { ...label, x, y };
    if (placed.some((other) => overlaps(box, other))) continue;
    placed.push(box);
    if (zoom < 2 && placed.length >= 40) break;
  }
  return placed;
}
