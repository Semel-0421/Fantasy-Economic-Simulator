// Draws the generated hex world. The world wraps east-west, so the static layers (terrain, borders,
// roads, rivers, place dots; and the hex grid) are baked into textures exactly one circumference wide
// and shown as three copies side by side; whatever crosses the seam is drawn on both sides first.
import { Container, Graphics, Rectangle, Sprite, type Renderer, type Texture } from 'pixi.js';
import { textures } from '../assets/loader';
import { INK, PAPER, PAPER_DARK, WOOD, NATION_COLOURS, TERRAIN_COLOURS, HEX_TERRAIN_COLOURS, WATER } from './colours';
import { axialToPixel, hexCorners, hexKey, neighbours, type Axial } from './hex';
import { hexRouteEdges, wrapHex, type HexLayout, type HexTile } from './hex_layout';
import type { MapData } from './layout';

const LEVELS = ['continent', 'nation', 'region', 'city', 'village'];
/** Anything within this many pixels of the seam is drawn on the far side too. */
const SEAM = 48;

export interface HexWorld {
  /** Three copies (-width, 0, +width) of the baked terrain. */
  terrain: Container;
  /** Three copies of the baked hex outlines (shown only when hexes are large enough). */
  grid: Container;
  emblems: Container;
  townSprites: Map<number, Sprite>;
  /** World pixels of the baked strip in y. */
  top: number;
  height: number;
}

/** Copies of x (in [0, width)) that may be visible near the seam: itself, and x +/- width close to it. */
const copies = (x: number, width: number): number[] => [0, ...(x < SEAM ? [width] : []), ...(x > width - SEAM ? [-width] : [])];

function bake(renderer: Renderer, source: Container, frame: Rectangle, resolution: number, width: number): Container {
  const texture: Texture = renderer.generateTexture({ target: source, frame, resolution, antialias: false });
  const tiles = new Container();
  for (const dx of [-width, 0, width]) {
    const sprite = new Sprite(texture);
    sprite.position.set(frame.x + dx, frame.y);
    tiles.addChild(sprite);
  }
  return tiles;
}

/** The layers the terrain bake draws into, and what drawing a tile needs to know about the layout. */
interface Paint {
  layout: HexLayout;
  width: number;
  ground: Graphics;
  edges: Graphics;
  roads: Graphics;
  dots: Graphics;
  tints: Graphics;
  grid: Graphics;
  terrain: Container;
  nations: Map<number, number>;
  at: (hex: Axial) => HexTile | undefined;
}

function addTerrainSprite(terrain: Container, texture: Texture, x: number, y: number): void {
  const sprite = new Sprite(texture); sprite.anchor.set(0.5); sprite.position.set(x, y); sprite.width = 28; sprite.height = 32;
  terrain.addChild(sprite);
}

/** Nation and region borders of one tile copy; a nation edge is drawn once, by the lower-numbered side. */
function drawBorders(p: Paint, tile: HexTile, base: { x: number; y: number }[], dx: number): void {
  // Vertex pairs in clockwise order correspond to these axial neighbour directions.
  const adjacent = neighbours(tile), neighbourOrder = [5, 0, 1, 2, 3, 4];
  for (let edge = 0; edge < 6; edge++) {
    const other = p.at(adjacent[neighbourOrder[edge]!]!);
    if (tile.nation < 0 || other?.nation === tile.nation && other.region === tile.region) continue;
    const a = base[edge]!, b = base[(edge + 1) % 6]!;
    if (other?.nation !== tile.nation) {
      if (other && other.nation >= 0 && other.nation < tile.nation) continue;
      p.edges.moveTo(a.x + dx, a.y).lineTo(b.x + dx, b.y).stroke({ color: INK, width: 4.5 });
      p.edges.moveTo(a.x + dx, a.y).lineTo(b.x + dx, b.y).stroke({ color: p.nations.get(tile.nation)!, width: 2.5 });
    } else if (tile.region >= 0) {
      for (const t of [0.2, 0.6]) p.edges.circle(a.x + dx + (b.x - a.x) * t, a.y + (b.y - a.y) * t, 0.8).fill(WOOD);
    }
  }
}

function drawTileCopy(p: Paint, tile: HexTile, centre: { x: number; y: number }, base: { x: number; y: number }[], coastal: boolean, dx: number): void {
  const poly = base.flatMap((pt) => [pt.x + dx, pt.y]);
  p.ground.poly(poly).fill(coastal ? WATER : HEX_TERRAIN_COLOURS[tile.terrain]);
  p.grid.poly(poly).stroke({ width: 0.7, color: INK, alpha: 0.18 });
  if (tile.nation >= 0) p.tints.poly(poly).fill({ color: p.nations.get(tile.nation)!, alpha: 0.12 });
  const texture = textures.get(`terrain-${tile.terrain}`);
  if (texture) addTerrainSprite(p.terrain, texture, centre.x + dx, centre.y);
  if (tile.road && textures.has('terrain-road-overlay')) addTerrainSprite(p.terrain, textures.get('terrain-road-overlay')!, centre.x + dx, centre.y);
  drawBorders(p, tile, base, dx);
}

/** Draws every tile on each visible copy and returns the vertical extent of the world. */
function drawTiles(p: Paint): { top: number; bottom: number } {
  let top = Infinity, bottom = -Infinity;
  for (const tile of p.layout.hexes.values()) {
    const centre = axialToPixel(tile), base = hexCorners(tile);
    top = Math.min(top, centre.y - 16); bottom = Math.max(bottom, centre.y + 16);
    const coastal = tile.terrain === 'water' && neighbours(tile).some((n) => { const other = p.at(n); return !!other && other.terrain !== 'water'; });
    for (const dx of copies(centre.x, p.width)) drawTileCopy(p, tile, centre, base, coastal, dx);
  }
  return { top, bottom };
}

/** Rivers run from each river hex to the hex it drains into (generated hydrology, gen/hydro.ts). */
function drawRivers(p: Paint): void {
  const W = p.width;
  for (const { from, to } of p.layout.rivers) {
    const start = axialToPixel(from), raw = axialToPixel(to), q = { x: start.x + ((raw.x - start.x) - W * Math.round((raw.x - start.x) / W)), y: raw.y };
    for (const dx of copies(start.x, W)) p.roads.moveTo(start.x + dx, start.y).lineTo(q.x + dx, q.y).stroke({ color: WATER, width: 2.2, alpha: 0.9, cap: 'round' });
  }
}

function drawRoutes(map: MapData, p: Paint): void {
  for (const edge of hexRouteEdges(map, p.layout)) {
    const start = axialToPixel(edge.from), q = axialToPixel(edge.to);
    const end = edge.terrain === 3 ? { x: (start.x + q.x) / 2, y: (start.y + q.y) / 2 } : q;
    for (const dx of copies(start.x, p.width)) {
      p.roads.moveTo(start.x + dx, start.y).lineTo(end.x + dx, end.y)
        .stroke({ color: edge.terrain === 3 ? PAPER : TERRAIN_COLOURS[edge.terrain] ?? WOOD, width: edge.terrain === 2 ? 2.5 : 0.8, alpha: 0.55 });
    }
  }
}

function drawPlaces(map: MapData, p: Paint, emblems: Container, townSprites: Map<number, Sprite>): void {
  for (const node of map.nodes) {
    const pt = p.layout.points.get(node.id);
    if (!pt) continue;
    // Villages are small and soft so they do not speckle the zoomed-out map; towns stay crisp.
    for (const dx of copies(pt.x, p.width)) {
      if (node.level === 4) p.dots.circle(pt.x + dx, pt.y, 2).fill({ color: PAPER, alpha: 0.75 }).stroke({ width: 1, color: INK, alpha: 0.6 });
      else p.dots.circle(pt.x + dx, pt.y, node.level === 3 ? 3.5 : 4).fill(PAPER).stroke({ width: 2, color: INK });
    }
    const texture = textures.get(`town-${LEVELS[node.level]}`);
    if (texture) {
      const sprite = new Sprite(texture); sprite.anchor.set(0.5); sprite.position.set(pt.x, pt.y); sprite.visible = false;
      townSprites.set(node.id, sprite); emblems.addChild(sprite);
    }
  }
}

export function drawHexWorld(map: MapData, layout: HexLayout, world: Container, renderer: Renderer, maxTexture = 4096): HexWorld {
  const W = layout.width, terrain = new Container(), ground = new Graphics(), edges = new Graphics(), roads = new Graphics(), dots = new Graphics(), tints = new Graphics(), grid = new Graphics();
  const emblems = new Container(), townSprites = new Map<number, Sprite>();
  const nations = new Map(map.nodes.filter((node) => node.level === 1).sort((a, b) => a.id - b.id).map((node, i) => [node.id, NATION_COLOURS[i % NATION_COLOURS.length]!]));
  const at = (hex: Axial): HexTile | undefined => layout.hexes.get(hexKey(wrapHex(layout, hex)));
  const paint: Paint = { layout, width: W, ground, edges, roads, dots, tints, grid, terrain, nations, at };
  terrain.addChild(ground);
  const { top, bottom } = drawTiles(paint);
  drawRivers(paint);
  drawRoutes(map, paint);
  drawPlaces(map, paint, emblems, townSprites);
  terrain.addChild(tints, roads, edges, dots);
  // Twice the resolution keeps zoomed-in terrain sharp, where the GPU's texture limit allows it.
  const frame = new Rectangle(0, top, W, bottom - top);
  const resolution = Math.max(W, frame.height) * 2 <= maxTexture ? 2 : 1;
  const terrainTiles = bake(renderer, terrain, frame, resolution, W), gridTiles = bake(renderer, grid, frame, resolution, W);
  terrain.destroy({ children: true });
  grid.destroy();
  world.addChild(terrainTiles, gridTiles, emblems);
  return { terrain: terrainTiles, grid: gridTiles, emblems, townSprites, top, height: frame.height };
}

/** Only labelled places get large symbols, drawn at the copy of the world nearest `centreX`. */
export function drawEmblems(graphics: Graphics, map: MapData, layout: HexLayout, visible: Set<number>, scale: number, sprites: Map<number, Sprite>, centreX: number): void {
  graphics.clear();
  const near = (x: number): number => x + layout.width * Math.round((centreX - x) / layout.width);
  for (const [id, sprite] of sprites) {
    sprite.visible = visible.has(id); sprite.width = sprite.height = 24 / scale;
    const p = layout.points.get(id);
    if (p) sprite.position.set(near(p.x), p.y);
  }
  for (const id of visible) {
    const node = map.nodes[id];
    if (!node || node.id !== id || sprites.has(id)) continue;
    const p = layout.points.get(id)!, x = near(p.x);
    const radius = (node.level === 4 ? 3 : 5) / scale;
    if (node.level >= 3) graphics.rect(x - radius, p.y - radius, 2 * radius, 2 * radius).fill(node.level === 4 ? PAPER_DARK : PAPER).stroke({ color: INK, width: 1 / scale });
    else graphics.circle(x, p.y, radius).fill(PAPER_DARK).stroke({ color: NATION_COLOURS[node.id % 8]!, width: 2 / scale });
  }
}
