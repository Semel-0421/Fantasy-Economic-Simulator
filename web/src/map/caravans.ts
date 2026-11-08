// Merchant caravans on the map (caravans view). While a week computes they move from the town they
// left toward where they are going; when the new view arrives they settle at its positions. With
// reduced motion they are drawn still. Only a fixed number are drawn, so a full-scale world stays light.
import { Graphics } from 'pixi.js';
import type { Views } from '../sim/types';

export type Caravan = Views['caravans'][number];
type Point = { x: number; y: number };

/** The most caravans drawn at once. */
export const MAX_CARAVANS = 400;
/** Where a resting caravan sits along its road (0 = the town it left). */
const REST = 0.2;
/** How long the trip toward the destinations takes on screen, in milliseconds (a turn is under 1 s). */
const LEG_MS = 1000;

/** Each drawable caravan's point at `progress` (0..1, clamped) along its road. */
export function caravanPoints(list: Caravan[], points: Map<number, Point>, progress: number): Point[] {
  const t = Math.min(1, Math.max(0, progress));
  const out: Point[] = [];
  for (const c of list) {
    if (out.length >= MAX_CARAVANS) break;
    const a = points.get(c.from);
    const b = points.get(c.to);
    if (!a || !b) continue;
    out.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });
  }
  return out;
}

export interface CaravanLayer {
  graphics: Graphics;
  set(list: Caravan[]): void;
  /** Starts (true) or stops (false) the moving animation; stopping settles them at rest. */
  move(on: boolean): void;
  destroy(): void;
}

export function caravanLayer(points: Map<number, Point>, reducedMotion: () => boolean): CaravanLayer {
  const graphics = new Graphics();
  let list: Caravan[] = [];
  let raf = 0;
  const draw = (progress: number): void => {
    const g = graphics.clear();
    for (const p of caravanPoints(list, points, progress)) g.rect(p.x - 4, p.y - 4, 8, 8);
    g.fill({ color: 0x5a3d1e, alpha: 0.85 }).stroke({ width: 1.5, color: 0xf3e9d2 });
  };
  const stop = (): void => {
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
  };
  return {
    graphics,
    set(next) {
      list = next;
      if (!raf) draw(REST);
    },
    move(on) {
      stop();
      if (!on || reducedMotion()) {
        draw(REST);
        return;
      }
      const t0 = performance.now();
      const frame = (now: number): void => {
        // From where they rest toward their destinations; they wait there for the new week's view.
        const t = Math.min(1, (now - t0) / LEG_MS);
        draw(REST + (1 - REST) * (1 - (1 - t) ** 2));
        raf = t < 1 ? requestAnimationFrame(frame) : 0;
      };
      raf = requestAnimationFrame(frame);
    },
    destroy: stop,
  };
}
