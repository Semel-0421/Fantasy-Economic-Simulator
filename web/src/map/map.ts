// The world map drawn with PixiJS: region outlines, routes, towns by level, the player's position and
// destination, and the selected town. Drag pans, the wheel and a two-finger pinch zoom, a tap selects
// the nearest town. All input is DOM pointer events on the canvas; the Pixi scene only draws.
import { Application, Container, Graphics, Sprite, Text } from 'pixi.js';
import { textures } from '../assets/loader';
import { labelCandidates, placeLabels, type Label } from './labels';
import { drawWorld, mapControls, playerMark } from './symbols';
import { caravanLayer, type Caravan } from './caravans';
import { fitCamera, panBy, toWorld, zoomAt, type Camera } from './camera';
import { layoutMap, pickTown, type MapData } from './layout';

import { INK, PAPER, PAPER_DARK, SEA, DANGER } from './colours';
/** How far (screen px) a press may move and still count as a tap. */
const TAP_SLOP = 8;

export interface MapScreen {
  setPlayer(node: number, destination: number): void;
  select(node: number): void;
  setTargets(nodes: number[]): void;
  labelBoxes(): readonly Label[];
  renderedLabelBoxes(): readonly Label[];
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
  labels: Map<number, Text>;
}

function drawLabels(map: MapData, layer: Container): Map<number, Text> {
  const labels = new Map<number, Text>();
  for (const node of map.nodes) {
    const text = new Text({ text: node.name, style: {
      fontFamily: 'Atkinson Hyperlegible, system-ui', fontSize: 14, fill: PAPER, stroke: { color: INK, width: 2 },
    } });
    text.visible = false;
    labels.set(node.id, text);
    layer.addChild(text);
  }
  return labels;
}

export async function createMap(host: HTMLElement, map: MapData, onSelect: (node: number) => void): Promise<MapScreen> {
  await document.fonts.ready;
  const layout = layoutMap(map);
  const app = new Application();
  await app.init({ resizeTo: host, background: PAPER_DARK, antialias: true, autoDensity: true, resolution: Math.min(window.devicePixelRatio || 1, 2) });
  const canvas = app.canvas;
  canvas.style.touchAction = 'none';
  canvas.tabIndex = 0;
  canvas.setAttribute('role', 'img');
  canvas.setAttribute('aria-label', 'World map. Plus and minus zoom; zero fits the world. Choose a route below to travel by keyboard.');
  host.prepend(canvas);

  const scene: Scene = { world: new Container(), marks: new Graphics(), labels: new Map() };
  const townSprites = drawWorld(map, layout, scene.world);
  // Labels live in screen space (CSS pixels, never scaled), so their size is constant at every zoom.
  const labelLayer = new Container();
  scene.labels = drawLabels(map, labelLayer);
  const sizes = new Map([...scene.labels.values()].map((text) => [text.text, { w: text.width, h: text.height }]));
  const candidates = labelCandidates(map, layout, (name) => sizes.get(name)!);
  const marker = textures.has('player-marker') ? new Sprite(textures.get('player-marker')) : null;
  if (marker) { marker.anchor.set(0.5); scene.world.addChild(marker); }
  const reduced = (): boolean => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const caravans = caravanLayer(layout.points, reduced);
  scene.world.addChild(caravans.graphics, scene.marks);
  app.stage.addChild(scene.world, labelLayer);

  let base = fitCamera(host.clientWidth || 1, host.clientHeight || 1);
  let cam: Camera = base;
  let touched = false;
  const state = { player: -1, destination: -1, selected: -1, targets: new Set<number>(), neighbours: new Set<number>() };
  let boxes: Label[] = [];
  /** Each placed label's offset from its node's screen position, so pans and zooms carry it along. */
  let offsets = new Map<number, { dx: number; dy: number }>();
  let labelTimer = 0;
  const byId = new Map(candidates.map((label) => [label.id, label]));
  const anchorOf = (id: number): { x: number; y: number } => {
    const c = byId.get(id)!;
    return { x: c.x * cam.scale + cam.x, y: c.y * cam.scale + cam.y };
  };
  const moveLabels = (): void => {
    boxes = boxes.map((box) => {
      const a = anchorOf(box.id), o = offsets.get(box.id)!;
      const moved = { ...box, x: a.x + o.dx, y: a.y + o.dy };
      scene.labels.get(box.id)!.position.set(moved.x, moved.y);
      return moved;
    });
  };
  const updateLabels = (): void => {
    for (const text of scene.labels.values()) text.visible = false;
    boxes = placeLabels(candidates, cam, cam.scale / base.scale, state, host.clientWidth, host.clientHeight);
    offsets = new Map(boxes.map((box) => { const a = anchorOf(box.id); return [box.id, { dx: box.x - a.x, dy: box.y - a.y }]; }));
    for (const box of boxes) {
      const text = scene.labels.get(box.id)!;
      text.visible = true;
      text.position.set(box.x, box.y);
    }
  };

  const apply = (): void => {
    scene.world.position.set(cam.x, cam.y);
    scene.world.scale.set(cam.scale);
    moveLabels();
    for (const { sprite, level } of townSprites) {
      sprite.visible = level < 2 || cam.scale / base.scale >= 4;
      sprite.width = sprite.height = 32 / cam.scale;
    }
    drawMarks();
  };

  const drawMarks = (): void => {
    const g = scene.marks.clear();
    const at = (id: number): { x: number; y: number } | undefined => layout.points.get(id);
    const p = at(state.player);
    const d = state.destination >= 0 ? at(state.destination) : undefined;
    if (p && d) g.moveTo(p.x, p.y).lineTo(d.x, d.y).stroke({ width: 5, color: DANGER, alpha: 0.8 });
    if (d) g.circle(d.x, d.y, 14 / cam.scale).stroke({ width: 2 / cam.scale, color: DANGER });
    const sel = at(state.selected);
    if (sel) g.circle(sel.x, sel.y, 16 / cam.scale).stroke({ width: 2 / cam.scale, color: SEA });
    if (marker) {
      marker.visible = !!p;
      marker.width = marker.height = 16 / cam.scale;
      if (p) marker.position.set(p.x, p.y);
    } else if (p) playerMark(g, p.x, p.y, cam.scale);
  };

  const resize = new ResizeObserver(() => {
    base = fitCamera(host.clientWidth || 1, host.clientHeight || 1);
    if (!touched) cam = base;
    apply();
    updateLabels();
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
    if (pointers.size === 0) { downAt = null; updateLabels(); }
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
      window.clearTimeout(labelTimer);
      labelTimer = window.setTimeout(updateLabels, 120);
    },
    { passive: false },
  );

  const zoom = (factor: number): void => {
    cam = zoomAt(cam, factor, host.clientWidth / 2, host.clientHeight / 2, base.scale);
    touched = true;
    apply(); updateLabels();
  };
  const fit = (): void => { cam = base; touched = false; apply(); updateLabels(); };
  const controls = mapControls(zoom, fit);
  host.append(controls);
  canvas.addEventListener('keydown', (event) => {
    if (event.ctrlKey || event.metaKey || !['+', '=', '-', '0'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === '0') fit(); else zoom(event.key === '-' ? 1 / 1.5 : 1.5);
  });
  apply();
  updateLabels();
  return {
    setPlayer(node, destination) {
      state.player = node;
      state.destination = destination;
      state.neighbours = new Set(map.routes.filter((route) => route.from === node).map((route) => route.to));
      drawMarks();
      updateLabels();
    },
    select(node) {
      state.selected = node;
      drawMarks();
      updateLabels();
    },
    setTargets(nodes) { state.targets = new Set(nodes); updateLabels(); },
    labelBoxes() { return boxes; },
    renderedLabelBoxes() {
      const rect = canvas.getBoundingClientRect();
      return boxes.map((box) => {
        const bounds = scene.labels.get(box.id)!.getBounds(); // stage space = CSS pixels of the canvas
        return { ...box, x: bounds.x * rect.width / app.screen.width, y: bounds.y * rect.height / app.screen.height,
          w: bounds.width * rect.width / app.screen.width, h: bounds.height * rect.height / app.screen.height };
      });
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
      window.clearTimeout(labelTimer);
      controls.remove();
      app.destroy(true, { children: true });
    },
  };
}
