import { t, language } from '../i18n';
// The world map drawn with PixiJS: region outlines, routes, towns by level, the player's position and
// destination, and the selected town. Drag pans, the wheel and a two-finger pinch zoom, a tap selects
// the nearest town. All input is DOM pointer events on the canvas; the Pixi scene only draws.
import { Application, Container, Graphics, Sprite, Text } from 'pixi.js';
import { textures } from '../assets/loader';
import { labelCandidates, placeLabels, type Label, type LabelState } from './labels';
import { mapControls, playerMark } from './symbols';
import { caravanLayer, type Caravan, type CaravanLayer } from './caravans';
import { fitCamera, panBy, toWorld, zoomAt, type Camera } from './camera';
import type { MapData } from './layout';
import { pickHexTown, type HexLayout } from './hex_layout';
import { layoutInWorker } from './gen/layout_async';
import { drawHexWorld, drawEmblems } from './hex_draw';

import { INK, PAPER, SEA, GOLD, DANGER } from './colours';
/** How far (screen px) a press may move and still count as a tap. */
const TAP_SLOP = 8;

export interface MapScreen {
  setPlayer(node: number, destination: number): void;
  select(node: number): void;
  setTargets(nodes: number[]): void;
  labelBoxes(): readonly Label[];
  renderedLabelBoxes(): readonly Label[];
  /** Frames rendered so far (the map renders on demand). */
  renderCount(): number;
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
      fontFamily: language.code === 'en' ? 'Atkinson Hyperlegible, system-ui' : language.fontStack, fontSize: 14, fill: PAPER, stroke: { color: INK, width: 2 },
    } });
    text.visible = false;
    labels.set(node.id, text);
    layer.addChild(text);
  }
  return labels;
}

/** The GPU's largest texture side (WebGL); a safe 4096 where it cannot be read. */
function maxTextureSize(app: Application): number {
  const gl = (app.renderer as unknown as { gl?: WebGLRenderingContext }).gl;
  return gl ? Number(gl.getParameter(gl.MAX_TEXTURE_SIZE)) || 4096 : 4096;
}

/** Everything the map's drawing, input and public API share. */
interface View {
  host: HTMLElement;
  map: MapData;
  layout: HexLayout;
  app: Application;
  canvas: HTMLCanvasElement;
  scene: Scene;
  grid: Container;
  townMarks: Graphics;
  townSprites: Map<number, Sprite>;
  marker: Sprite | null;
  caravans: CaravanLayer;
  candidates: Label[];
  byId: Map<number, Label>;
  bounds: { x: number; y: number; w: number; h: number };
  cam: Camera;
  base: Camera;
  touched: boolean;
  state: LabelState & { destination: number };
  boxes: Label[];
  /** Each placed label's offset from its node's screen position, so pans and zooms carry it along. */
  offsets: Map<number, { dx: number; dy: number }>;
  labelTimer: number;
  frame: number;
  /** Number of frames rendered (tests check that an idle map renders none). */
  rendered: number;
}

function requestRender(v: View): void {
  if (v.frame) return;
  v.frame = requestAnimationFrame(() => { v.frame = 0; v.rendered++; v.app.render(); });
}

/** World x at the centre of the view. */
const centreX = (v: View): number => (v.host.clientWidth / 2 - v.cam.x) / v.cam.scale;
/** A world x moved by whole turns to the copy nearest the view centre. */
const nearX = (v: View, x: number): number => x + v.layout.width * Math.round((centreX(v) - x) / v.layout.width);

function anchorOf(v: View, id: number): { x: number; y: number } {
  const c = v.byId.get(id)!;
  return { x: nearX(v, c.x) * v.cam.scale + v.cam.x, y: c.y * v.cam.scale + v.cam.y };
}

function moveLabels(v: View): void {
  v.boxes = v.boxes.map((box) => {
    const a = anchorOf(v, box.id), o = v.offsets.get(box.id)!;
    const moved = { ...box, x: a.x + o.dx, y: a.y + o.dy };
    v.scene.labels.get(box.id)!.position.set(moved.x, moved.y);
    return moved;
  });
}

function updateLabels(v: View): void {
  for (const text of v.scene.labels.values()) text.visible = false;
  v.boxes = placeLabels(v.candidates, v.cam, v.cam.scale / v.base.scale, v.state, v.host.clientWidth, v.host.clientHeight, v.layout.width);
  v.offsets = new Map(v.boxes.map((box) => { const a = anchorOf(v, box.id); return [box.id, { dx: box.x - a.x, dy: box.y - a.y }]; }));
  drawEmblems(v.townMarks, v.map, v.layout, new Set(v.boxes.map((box) => box.id)), v.cam.scale, v.townSprites, centreX(v));
  requestRender(v);
  for (const box of v.boxes) {
    const text = v.scene.labels.get(box.id)!;
    text.visible = true;
    text.position.set(box.x, box.y);
  }
}

/** Keeps the view centre inside the middle copy of the world, so the three copies always cover it. */
function wrapCamera(v: View): void {
  const W = v.layout.width, turns = Math.floor(centreX(v) / W);
  if (turns) v.cam = { ...v.cam, x: v.cam.x + turns * W * v.cam.scale };
}

function apply(v: View): void {
  wrapCamera(v);
  v.grid.visible = v.cam.scale * 28 >= 10;
  v.scene.world.position.set(v.cam.x, v.cam.y);
  v.scene.world.scale.set(v.cam.scale);
  moveLabels(v);
  drawEmblems(v.townMarks, v.map, v.layout, new Set(v.boxes.map((box) => box.id)), v.cam.scale, v.townSprites, centreX(v));
  drawMarks(v);
  requestRender(v);
}

function drawMarks(v: View): void {
  const { state, cam, marker } = v, W = v.layout.width;
  const g = v.scene.marks.clear();
  const at = (id: number): { x: number; y: number } | undefined => { const p = v.layout.points.get(id); return p && { x: nearX(v, p.x), y: p.y }; };
  for (const target of state.targets) {
    const point = at(target);
    if (point) {
      g.circle(point.x, point.y, 8 / cam.scale).stroke({ width: 3.5 / cam.scale, color: INK });
      g.circle(point.x, point.y, 8 / cam.scale).stroke({ width: 1.5 / cam.scale, color: PAPER });
    }
  }
  const p = at(state.player);
  const d = state.destination >= 0 ? at(state.destination) : undefined;
  if (p && d) g.moveTo(p.x, p.y).lineTo(p.x + (d.x - p.x) - W * Math.round((d.x - p.x) / W), d.y).stroke({ width: 5, color: DANGER, alpha: 0.8 });
  if (d) g.circle(d.x, d.y, 14 / cam.scale).stroke({ width: 2 / cam.scale, color: DANGER });
  const sel = at(state.selected);
  if (sel) {
    g.circle(sel.x, sel.y, 16 / cam.scale).stroke({ width: 4 / cam.scale, color: INK });
    g.circle(sel.x, sel.y, 16 / cam.scale).stroke({ width: 2 / cam.scale, color: GOLD });
  }
  if (marker) {
    marker.visible = !!p;
    marker.width = marker.height = 16 / cam.scale;
    if (p) marker.position.set(p.x, p.y);
  } else if (p) playerMark(g, p.x, p.y, cam.scale);
  requestRender(v);
}

/** The Pixi application, rendering only when something changes, with its canvas inside the host. */
async function startApp(host: HTMLElement): Promise<Application> {
  const app = new Application();
  // No antialiasing (pixel art, cheaper on weak GPUs) and no free-running ticker: the map renders
  // only when something on it changes (render on demand).
  await app.init({ resizeTo: host, background: SEA, antialias: false, autoStart: false, autoDensity: true, resolution: Math.min(window.devicePixelRatio || 1, 2) });
  app.ticker.stop();
  const canvas = app.canvas;
  canvas.style.touchAction = 'none';
  canvas.tabIndex = 0;
  canvas.setAttribute('role', 'img');
  canvas.setAttribute('aria-label', t('map.description'));
  host.prepend(canvas);
  return app;
}

function buildView(host: HTMLElement, map: MapData, layout: HexLayout, app: Application): View {
  const scene: Scene = { world: new Container(), marks: new Graphics(), labels: new Map() };
  const { grid, emblems, townSprites } = drawHexWorld(map, layout, scene.world, app.renderer, maxTextureSize(app));
  const townMarks = new Graphics();
  emblems.addChild(townMarks);
  // Labels live in screen space (CSS pixels, never scaled), so their size is constant at every zoom.
  const labelLayer = new Container();
  scene.labels = drawLabels(map, labelLayer);
  const sizes = new Map([...scene.labels.values()].map((text) => [text.text, { w: text.width, h: text.height }]));
  const candidates = labelCandidates(map, layout, (name) => sizes.get(name)!);
  const marker = textures.has('player-marker') ? new Sprite(textures.get('player-marker')) : null;
  if (marker) { marker.anchor.set(0.5); scene.world.addChild(marker); }
  const reduced = (): boolean => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const caravans = caravanLayer(layout.points, reduced, () => requestRender(view), layout.width);
  scene.world.addChild(caravans.graphics, scene.marks);
  app.stage.addChild(scene.world, labelLayer);
  const bounds = { x: layout.landBounds.x - 28, y: layout.landBounds.y - 32, w: layout.landBounds.w + 56, h: layout.landBounds.h + 64 };
  const base = fitCamera(host.clientWidth || 1, host.clientHeight || 1, bounds);
  const view: View = {
    host, map, layout, app, canvas: app.canvas, scene, grid, townMarks, townSprites, marker, caravans, candidates,
    byId: new Map(candidates.map((label) => [label.id, label])), bounds, cam: base, base, touched: false,
    state: { player: -1, destination: -1, selected: -1, targets: new Set<number>(), neighbours: new Set<number>() },
    boxes: [], offsets: new Map(), labelTimer: 0, frame: 0, rendered: 0,
  };
  return view;
}

const local = (v: View, e: PointerEvent): { x: number; y: number } => {
  const r = v.canvas.getBoundingClientRect();
  return { x: e.clientX - r.left, y: e.clientY - r.top };
};

/** One pointer pans (or taps), two pinch. */
function pinch(v: View, old: { x: number; y: number }, other: { x: number; y: number }, p: { x: number; y: number }): void {
  const before = Math.hypot(old.x - other.x, old.y - other.y) || 1;
  const after = Math.hypot(p.x - other.x, p.y - other.y) || 1;
  v.cam = zoomAt(v.cam, after / before, (p.x + other.x) / 2, (p.y + other.y) / 2, v.base.scale);
}

function bindPointerInput(v: View, onSelect: (node: number) => void): void {
  const { canvas } = v;
  const pointers = new Map<number, { x: number; y: number }>();
  let downAt: { x: number; y: number } | null = null;
  let moved = false;
  canvas.addEventListener('pointerdown', (e) => {
    canvas.setPointerCapture?.(e.pointerId);
    const p = local(v, e);
    pointers.set(e.pointerId, p);
    if (pointers.size === 1) {
      downAt = p;
      moved = false;
    } else moved = true;
  });
  canvas.addEventListener('pointermove', (e) => {
    const old = pointers.get(e.pointerId);
    if (!old) return;
    const p = local(v, e);
    if (pointers.size === 1) {
      if (downAt && Math.hypot(p.x - downAt.x, p.y - downAt.y) > TAP_SLOP) moved = true;
      if (moved) v.cam = panBy(v.cam, p.x - old.x, p.y - old.y);
    } else if (pointers.size === 2) {
      const other = [...pointers.entries()].find(([id]) => id !== e.pointerId)?.[1];
      if (other) pinch(v, old, other, p);
    }
    pointers.set(e.pointerId, p);
    v.touched = true;
    apply(v);
  });
  const release = (e: PointerEvent): void => {
    if (!pointers.has(e.pointerId)) return;
    pointers.delete(e.pointerId);
    if (pointers.size === 0 && !moved && downAt && e.type === 'pointerup') {
      const w = toWorld(v.cam, downAt.x, downAt.y);
      const id = pickHexTown(v.map, v.layout, w.x, w.y);
      if (id >= 0) onSelect(id);
    }
    if (pointers.size === 0) { downAt = null; updateLabels(v); }
  };
  canvas.addEventListener('pointerup', release);
  canvas.addEventListener('pointercancel', release);
  canvas.addEventListener(
    'wheel',
    (e) => {
      e.preventDefault();
      const p = local(v, e as unknown as PointerEvent);
      v.cam = zoomAt(v.cam, Math.exp(-e.deltaY * 0.0015), p.x, p.y, v.base.scale);
      v.touched = true;
      apply(v);
      window.clearTimeout(v.labelTimer);
      v.labelTimer = window.setTimeout(() => updateLabels(v), 120);
    },
    { passive: false },
  );
}

/** Zoom and fit buttons plus the keyboard shortcuts; returns the controls element. */
function bindControls(v: View): HTMLElement {
  const zoom = (factor: number): void => {
    v.cam = zoomAt(v.cam, factor, v.host.clientWidth / 2, v.host.clientHeight / 2, v.base.scale);
    v.touched = true;
    apply(v); updateLabels(v);
  };
  const fit = (): void => { v.cam = v.base; v.touched = false; apply(v); updateLabels(v); };
  const controls = mapControls(zoom, fit);
  v.host.append(controls);
  v.canvas.addEventListener('keydown', (event) => {
    if (event.ctrlKey || event.metaKey || !['+', '=', '-', '0'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === '0') fit(); else zoom(event.key === '-' ? 1 / 1.5 : 1.5);
  });
  return controls;
}

function screenApi(v: View, resize: ResizeObserver, controls: HTMLElement): MapScreen {
  const { map, state } = v;
  return {
    setPlayer(node, destination) {
      state.player = node;
      state.destination = destination;
      state.neighbours = new Set(map.routes.filter((route) => route.from === node).map((route) => route.to));
      drawMarks(v);
      updateLabels(v);
    },
    select(node) {
      state.selected = node;
      drawMarks(v);
      updateLabels(v);
    },
    setTargets(nodes) { state.targets = new Set(nodes); drawMarks(v); updateLabels(v); },
    labelBoxes() { return v.boxes; },
    renderCount() { return v.rendered; },
    renderedLabelBoxes() {
      const rect = v.canvas.getBoundingClientRect(), screen = v.app.screen;
      return v.boxes.map((box) => {
        const bounds = v.scene.labels.get(box.id)!.getBounds(); // stage space = CSS pixels of the canvas
        return { ...box, x: bounds.x * rect.width / screen.width, y: bounds.y * rect.height / screen.height,
          w: bounds.width * rect.width / screen.width, h: bounds.height * rect.height / screen.height };
      });
    },
    setCaravans(list) {
      v.caravans.set(list);
    },
    moveCaravans(on) {
      v.caravans.move(on);
    },
    screenOf(node) {
      const p = v.layout.points.get(node);
      return p ? { x: nearX(v, p.x) * v.cam.scale + v.cam.x, y: p.y * v.cam.scale + v.cam.y } : null;
    },
    destroy() {
      if (v.frame) cancelAnimationFrame(v.frame);
      v.caravans.destroy();
      resize.disconnect();
      window.clearTimeout(v.labelTimer);
      controls.remove();
      v.app.destroy(true, { children: true });
    },
  };
}

export async function createMap(host: HTMLElement, map: MapData, onSelect: (node: number) => void, seed = 'world'): Promise<MapScreen> {
  await document.fonts.ready;
  // The world is generated in a Web Worker so the page never stalls while it is built.
  const layout = await layoutInWorker(map, seed);
  const app = await startApp(host);
  const view = buildView(host, map, layout, app);
  const resize = new ResizeObserver(() => {
    view.base = fitCamera(host.clientWidth || 1, host.clientHeight || 1, view.bounds);
    if (!view.touched) view.cam = view.base;
    apply(view);
    updateLabels(view);
  });
  resize.observe(host);
  bindPointerInput(view, onSelect);
  const controls = bindControls(view);
  apply(view);
  updateLabels(view);
  requestRender(view);
  return screenApi(view, resize, controls);
}
