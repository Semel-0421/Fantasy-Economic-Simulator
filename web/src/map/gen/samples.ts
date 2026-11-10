// Development page (samples.html, never in the release build): renders any map seed through the
// production map renderer plus a debug panel (plates, elevation, rainfall, rivers and biomes) and
// returns statistics. Driven by scripts/map-samples.mjs.
import '@fontsource/atkinson-hyperlegible/latin-400.css';
import '../../ui/fonts.css';
import { SimBridge } from '../../sim/bridge';
import { placeName } from '../../data/names';
import { createMap, type MapScreen } from '../map';
import { continentNeeds, layoutHexMap, routeConsistency } from '../hex_layout';
import type { MapData } from '../layout';
import { generateWorld, type GenWorld } from './world';
import type { Biome } from './climate';
import { HEX_TERRAIN_COLOURS, INK, PAPER, WATER } from '../colours';

const css = (n: number): string => `rgb(${n >> 16 & 255}, ${n >> 8 & 255}, ${n & 255})`;
const COLOURS: Record<Biome, string> = Object.fromEntries(Object.entries(HEX_TERRAIN_COLOURS).map(([k, v]) => [k, css(v)])) as Record<Biome, string>;

function panel(ctx: CanvasRenderingContext2D, w: GenWorld, ox: number, oy: number, title: string, colour: (i: number) => string, overlay?: (cx: (i: number) => number, cy: (i: number) => number) => void): void {
  const pw = 800, ph = 500, sx = (pw - 10) / (w.grid.cols * 28 + 14), sy = (ph - 30) / (w.grid.rows * 24 + 8), s = Math.min(sx, sy);
  const cx = (i: number): number => ox + 5 + (w.grid.x[i]! + 14) * s, cy = (i: number): number => oy + 25 + (w.grid.y[i]! + 16) * s;
  for (let i = 0; i < w.grid.size; i++) { ctx.fillStyle = colour(i); ctx.fillRect(cx(i) - 14 * s, cy(i) - 12 * s, 28 * s + 0.5, 24 * s + 0.5); }
  overlay?.(cx, cy);
  ctx.fillStyle = css(INK); ctx.fillRect(ox, oy, 800, 24);
  ctx.fillStyle = css(PAPER); ctx.font = '16px system-ui'; ctx.fillText(title, ox + 8, oy + 18);
}

function drawDebug(w: GenWorld): void {
  const ctx = (document.getElementById('debug') as HTMLCanvasElement).getContext('2d')!;
  ctx.fillStyle = css(INK); ctx.fillRect(0, 0, 1600, 1000);
  const hue = (p: number): string => `hsl(${(p * 137) % 360} 55% ${w.land[0] === undefined ? 50 : 45}%)`;
  panel(ctx, w, 0, 0, 'Tectonic plates (darker = ocean)', (i) => w.land[i] ? hue(w.plates.plate[i]!) : `hsl(${(w.plates.plate[i]! * 137) % 360} 35% 22%)`);
  let lo = Infinity, hi = -Infinity;
  for (const v of w.height) { lo = Math.min(lo, v); hi = Math.max(hi, v); }
  panel(ctx, w, 800, 0, 'Elevation', (i) => {
    const v = w.height[i]!;
    if (v < w.sea) { const t = (v - lo) / (w.sea - lo || 1); return `rgb(${20 + 30 * t}, ${50 + 60 * t}, ${90 + 80 * t})`; }
    const t = (v - w.sea) / (hi - w.sea || 1); return `rgb(${110 + 130 * t}, ${150 + 60 * t - 80 * t * t}, ${90 + 100 * t})`;
  });
  let rmax = 0;
  for (let i = 0; i < w.grid.size; i++) if (w.land[i]) rmax = Math.max(rmax, w.climate.rain[i]!);
  panel(ctx, w, 0, 500, 'Rainfall on land (blue = wet)', (i) => {
    if (!w.land[i]) return css(HEX_TERRAIN_COLOURS.water);
    const t = Math.min(1, w.climate.rain[i]! / (rmax || 1)); return `rgb(${230 - 190 * t}, ${215 - 120 * t}, ${170 + 60 * t})`;
  });
  panel(ctx, w, 800, 500, 'Biomes, rivers and lakes', (i) => w.drainage.lake[i] ? css(WATER) : COLOURS[w.biome[i]!], (cx, cy) => {
    ctx.strokeStyle = css(WATER); ctx.lineWidth = 1.6;
    for (let i = 0; i < w.grid.size; i++) {
      const d = w.drainage.down[i]!;
      if (w.river[i] && d >= 0) { ctx.beginPath(); ctx.moveTo(cx(i), cy(i)); ctx.lineTo(cx(d), cy(d)); ctx.stroke(); }
    }
  });
}

let map: MapData | null = null;
let screen: MapScreen | null = null;

async function renderSeed(seed: string): Promise<Record<string, unknown>> {
  if (!map) throw new Error('World not loaded');
  screen?.destroy(); screen = null;
  const host = document.getElementById('map')!;
  host.replaceChildren();
  const t1 = performance.now();
  const layout = layoutHexMap(map, seed);
  const layoutMs = performance.now() - t1;
  const t0 = performance.now();
  const world = generateWorld(layout.worldSeed, continentNeeds(map));
  const genMs = performance.now() - t0;
  drawDebug(world);
  screen = await createMap(host, map, () => {}, seed);
  await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
  const land = world.land.reduce((a, b) => a + b, 0), biome: Record<string, number> = {};
  world.biome.forEach((b, i) => { if (world.land[i]) biome[b] = (biome[b] ?? 0) + 1; });
  for (const k of Object.keys(biome)) biome[k] = Math.round(1000 * biome[k]! / land) / 10;
  return {
    seed, worldSeed: layout.worldSeed, layout: world.layout, attempt: world.attempt, grid: `${world.grid.cols}x${world.grid.rows}`, landShare: Math.round(1000 * land / world.grid.size) / 10,
    continents: world.continents.map((c) => c.length), islands: land - world.continents.reduce((a, c) => a + c.length, 0),
    biomePercent: biome, riverHexes: world.river.reduce((a, b) => a + b, 0), lakeHexes: world.drainage.lake.reduce((a, b) => a + b, 0),
    routeConsistency: Math.round(1000 * routeConsistency(map, layout)) / 10, generateMs: Math.round(genMs), layoutMs: Math.round(layoutMs),
  };
}

async function main(): Promise<void> {
  const game = await SimBridge.scenario(1);
  map = await game.view('map');
  for (const node of map.nodes) if (/^k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?$/.test(node.key)) node.name = placeName(node.key);
  game.dispose();
  Object.assign(window, { renderSeed });
  document.getElementById('status')!.textContent = 'ready';
}
void main();
