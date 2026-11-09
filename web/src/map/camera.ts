// Pan and zoom as plain values: screen = world * scale + (x, y).
import { WORLD_H, WORLD_W, type Box } from './layout';

export interface Camera {
  x: number;
  y: number;
  scale: number;
}

const MIN_ZOOM = 0.5;
const MAX_ZOOM = 12;

/** The camera that shows the whole world centred in a w x h view. */
export function fitCamera(w: number, h: number, bounds: Box = { x: 0, y: 0, w: WORLD_W, h: WORLD_H }): Camera {
  const scale = Math.min(w / bounds.w, h / bounds.h);
  return { scale, x: (w - bounds.w * scale) / 2 - bounds.x * scale, y: (h - bounds.h * scale) / 2 - bounds.y * scale };
}

export function toWorld(cam: Camera, sx: number, sy: number): { x: number; y: number } {
  return { x: (sx - cam.x) / cam.scale, y: (sy - cam.y) / cam.scale };
}

export function panBy(cam: Camera, dx: number, dy: number): Camera {
  return { ...cam, x: cam.x + dx, y: cam.y + dy };
}

/**
 * Zooms by `factor` keeping the world point under (sx, sy) fixed. The scale is clamped relative to
 * `base` (the fitted scale): from half of it to twelve times it.
 */
export function zoomAt(cam: Camera, factor: number, sx: number, sy: number, base = cam.scale): Camera {
  const scale = Math.min(base * MAX_ZOOM, Math.max(base * MIN_ZOOM, cam.scale * factor));
  const w = toWorld(cam, sx, sy);
  return { scale, x: sx - w.x * scale, y: sy - w.y * scale };
}
