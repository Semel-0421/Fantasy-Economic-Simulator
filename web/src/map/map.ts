// The world map drawn with PixiJS: region outlines, routes, towns by level, the player's position and
// destination, and the selected town. Drag pans, the wheel and a two-finger pinch zoom, a tap selects
// the nearest town. All input is DOM pointer events on the canvas; the Pixi scene only draws.
import { Application, Container, Graphics, Text } from 'pixi.js';
import { caravanLayer, type Caravan } from './caravans';
import { fitCamera, panBy, toWorld, zoomAt, type Camera } from './camera';
import { LEVEL, isTown, layoutMap, pickTown, routePairs, type Layout, type MapData } from './layout';

import { INK, PAPER, PAPER_DARK, GOLD, SEA, DANGER, TERRAIN_COLOURS } from './colours';
/** How far (screen px) a press may move and still count as a tap. */
const TAP_SLOP = 8;

export interface MapScreen {
  setPlayer(node: number, destination: number): void;
  select(node: number): void;
  /** The merchants on the road (caravans view); they rest near the town they left. */
  setCaravans(list: Caravan[]): void;
  /** While a week computes (true) the caravans move toward their destinations. */
  moveCaravans(on: boolean): void;
  /** A town's position in CSS pixels relative to the map element (for tests and tooltips). */
  screenOf(node: number): { x: number; y: number } | null;
  destroy(): void;
}

interface Scene {
  world: Container;
  marks: Graphics;
  labels: Text[];
}

function drawBase(map: MapData, layout: Layout, world: Container): Text[] {
  const g = new Graphics();
  for (const n of map.nodes) {
    const b = layout.boxes.get(n.id);
    if (!b) continue;
    const width = n.level === LEVEL.continent ? 4 : n.level === LEVEL.nation ? 2.5 : 1.2;
    g.roundRect(b.x, b.y, b.w, b.h, 12).stroke({ width, color: INK, alpha: 0.25 + 0.15 * (2 - n.level) });
  }
  for (const r of routePairs(map)) {
    const a = layout.points.get(r.from);
    const b = layout.points.get(r.to);
    if (!a || !b) continue;
    g.moveTo(a.x, a.y).lineTo(b.x, b.y).stroke({ width: 2 + Math.min(r.turns, 4), color: TERRAIN_COLOURS[r.terrain] ?? INK, alpha: 0.7 });
  }
  const labels: Text[] = [];
  for (const n of map.nodes) {
    const p = layout.points.get(n.id);
    if (!p) continue;
    if (isTown(n)) {
      const city = n.level === LEVEL.city;
      g.circle(p.x, p.y, city ? 14 : 8).fill(city ? PAPER : PAPER_DARK).stroke({ width: city ? 4 : 2.5, color: INK });
    }
    const box = layout.boxes.get(n.id);
    const text = new Text({
      text: n.name,
      style: { fontFamily: 'Atkinson Hyperlegible, system-ui', fontSize: isTown(n) ? (n.level === LEVEL.city ? 15 : 12) : 16, fill: INK },
    });
    text.anchor.set(0.5, isTown(n) ? -0.9 : 0);
    text.position.set(box ? box.x + box.w / 2 : p.x, box ? box.y + 4 : p.y);
    text.alpha = isTown(n) ? 1 : 0.6;
    labels.push(text);
  }
  world.addChild(g, ...labels);
  return labels;
}

export async function createMap(host: HTMLElement, map: MapData, onSelect: (node: number) => void): Promise<MapScreen> {
  const layout = layoutMap(map);
  const app = new Application();
  await app.init({ resizeTo: host, background: PAPER_DARK, antialias: true, autoDensity: true, resolution: Math.min(window.devicePixelRatio || 1, 2) });
  const canvas = app.canvas;
  canvas.style.touchAction = 'none';
  canvas.setAttribute('aria-hidden', 'true');
  host.prepend(canvas);

  const scene: Scene = { world: new Container(), marks: new Graphics(), labels: [] };
  scene.labels = drawBase(map, layout, scene.world);
  const reduced = (): boolean => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const caravans = caravanLayer(layout.points, reduced);
  scene.world.addChild(caravans.graphics, scene.marks);
  app.stage.addChild(scene.world);

  let base = fitCamera(host.clientWidth || 1, host.clientHeight || 1);
  let cam: Camera = base;
  let touched = false;
  const state = { player: -1, destination: -1, selected: -1 };

  const apply = (): void => {
    scene.world.position.set(cam.x, cam.y);
    scene.world.scale.set(cam.scale);
    // Labels keep a readable size whatever the zoom.
    const s = Math.min(1 / cam.scale, 4);
    for (const t of scene.labels) t.scale.set(s);
  };

  const drawMarks = (): void => {
    const g = scene.marks.clear();
    const at = (id: number): { x: number; y: number } | undefined => layout.points.get(id);
    const p = at(state.player);
    const d = state.destination >= 0 ? at(state.destination) : undefined;
    if (p && d) g.moveTo(p.x, p.y).lineTo(d.x, d.y).stroke({ width: 5, color: DANGER, alpha: 0.8 });
    if (d) g.circle(d.x, d.y, 22).stroke({ width: 4, color: DANGER });
    const sel = at(state.selected);
    if (sel) g.circle(sel.x, sel.y, 28).stroke({ width: 4, color: SEA });
    if (p) g.circle(p.x, p.y, 11).fill(GOLD).stroke({ width: 3, color: INK });
  };

  const resize = new ResizeObserver(() => {
    base = fitCamera(host.clientWidth || 1, host.clientHeight || 1);
    if (!touched) cam = base;
    apply();
  });
  resize.observe(host);

  // Pointer input: one pointer pans (or taps), two pinch.
  const pointers = new Map<number, { x: number; y: number }>();
  let downAt: { x: number; y: number } | null = null;
  let moved = false;
  const local = (e: PointerEvent): { x: number; y: number } => {
    const r = canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };
  canvas.addEventListener('pointerdown', (e) => {
    canvas.setPointerCapture?.(e.pointerId);
    const p = local(e);
    pointers.set(e.pointerId, p);
    if (pointers.size === 1) {
      downAt = p;
      moved = false;
    } else moved = true;
  });
  canvas.addEventListener('pointermove', (e) => {
    const old = pointers.get(e.pointerId);
    if (!old) return;
    const p = local(e);
    if (pointers.size === 1) {
      if (downAt && Math.hypot(p.x - downAt.x, p.y - downAt.y) > TAP_SLOP) moved = true;
      if (moved) cam = panBy(cam, p.x - old.x, p.y - old.y);
    } else if (pointers.size === 2) {
      const other = [...pointers.entries()].find(([id]) => id !== e.pointerId)?.[1];
      if (other) {
        const before = Math.hypot(old.x - other.x, old.y - other.y) || 1;
        const after = Math.hypot(p.x - other.x, p.y - other.y) || 1;
        cam = zoomAt(cam, after / before, (p.x + other.x) / 2, (p.y + other.y) / 2, base.scale);
      }
    }
    pointers.set(e.pointerId, p);
    touched = true;
    apply();
  });
  const release = (e: PointerEvent): void => {
    if (!pointers.has(e.pointerId)) return;
    pointers.delete(e.pointerId);
    if (pointers.size === 0 && !moved && downAt && e.type === 'pointerup') {
      const w = toWorld(cam, downAt.x, downAt.y);
      const id = pickTown(map, layout.points, w.x, w.y, 30 / cam.scale);
      if (id >= 0) onSelect(id);
    }
    if (pointers.size === 0) downAt = null;
  };
  canvas.addEventListener('pointerup', release);
  canvas.addEventListener('pointercancel', release);
  canvas.addEventListener(
    'wheel',
    (e) => {
      e.preventDefault();
      const p = local(e as unknown as PointerEvent);
      cam = zoomAt(cam, Math.exp(-e.deltaY * 0.0015), p.x, p.y, base.scale);
      touched = true;
      apply();
    },
    { passive: false },
  );

  apply();
  drawMarks();
  return {
    setPlayer(node, destination) {
      state.player = node;
      state.destination = destination;
      drawMarks();
    },
    select(node) {
      state.selected = node;
      drawMarks();
    },
    setCaravans(list) {
      caravans.set(list);
    },
    moveCaravans(on) {
      caravans.move(on);
    },
    screenOf(node) {
      const p = layout.points.get(node);
      return p ? { x: p.x * cam.scale + cam.x, y: p.y * cam.scale + cam.y } : null;
    },
    destroy() {
      caravans.destroy();
      resize.disconnect();
      app.destroy(true, { children: true });
    },
  };
}
