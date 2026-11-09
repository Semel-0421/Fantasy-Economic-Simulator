import { Container, Graphics, Sprite } from 'pixi.js';
import { textures } from '../assets/loader';
import { INK, PAPER, PAPER_DARK, WOOD, NATION_COLOURS, TERRAIN_COLOURS, HEX_TERRAIN_COLOURS, WATER } from './colours';
import { axialToPixel, hexCorners, hexKey, neighbours } from './hex';
import { hexRouteEdges, type HexLayout } from './hex_layout';
import type { MapData } from './layout';

const LEVELS = ['continent', 'nation', 'region', 'city', 'village'];

export function drawHexWorld(map: MapData, layout: HexLayout, world: Container): { terrain: Container; grid: Graphics; emblems: Container; townSprites: Map<number, Sprite> } {
  const terrain = new Container(), ground = new Graphics(), edges = new Graphics(), roads = new Graphics(), dots = new Graphics(), tints = new Graphics(), grid = new Graphics();
  const emblems = new Container(), townSprites = new Map<number, Sprite>();
  const nations = new Map(map.nodes.filter((node) => node.level === 1).sort((a, b) => a.id - b.id).map((node, i) => [node.id, NATION_COLOURS[i % NATION_COLOURS.length]!]));
  terrain.addChild(ground);
  for (const tile of layout.hexes.values()) {
    const centre = axialToPixel(tile), corners = hexCorners(tile);
    const coastal = tile.terrain === 'water' && (tile.continent >= 0 || neighbours(tile).some((n) => (layout.hexes.get(hexKey(n))?.continent ?? -1) >= 0));
    ground.poly(corners.flatMap((p) => [p.x, p.y])).fill(coastal ? WATER : HEX_TERRAIN_COLOURS[tile.terrain]);
    grid.poly(corners.flatMap((p) => [p.x, p.y])).stroke({ width: 0.7, color: INK, alpha: 0.18 });
    if (tile.nation >= 0) tints.poly(corners.flatMap((p) => [p.x, p.y])).fill({ color: nations.get(tile.nation)!, alpha: 0.12 });
    const texture = textures.get(`terrain-${tile.terrain}`);
    if (texture) {
      const sprite = new Sprite(texture); sprite.anchor.set(0.5); sprite.position.set(centre.x, centre.y); sprite.width = 28; sprite.height = 32;
      terrain.addChild(sprite);
    }
    // Vertex pairs in clockwise order correspond to these axial neighbour directions.
    const adjacent = neighbours(tile);
    const neighbourOrder = [5, 0, 1, 2, 3, 4];
    for (let edge = 0; edge < 6; edge++) {
      const other = layout.hexes.get(hexKey(adjacent[neighbourOrder[edge]!]!));
      if (tile.nation < 0 || other?.nation === tile.nation && other.region === tile.region) continue;
      const a = corners[edge]!, b = corners[(edge + 1) % 6]!;
      if (other?.nation !== tile.nation) {
        if (other && other.nation >= 0 && other.nation < tile.nation) continue;
        edges.moveTo(a.x, a.y).lineTo(b.x, b.y).stroke({ color: INK, width: 4.5 });
        edges.moveTo(a.x, a.y).lineTo(b.x, b.y).stroke({ color: nations.get(tile.nation)!, width: 2.5 });
      } else if (tile.region >= 0) {
        for (const t of [0.2, 0.6]) edges.circle(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, 0.8).fill(WOOD);
      }
    }
    if (tile.road && textures.has('terrain-road-overlay')) {
      const sprite = new Sprite(textures.get('terrain-road-overlay')); sprite.anchor.set(0.5); sprite.position.set(centre.x, centre.y); sprite.width = 28; sprite.height = 32;
      terrain.addChild(sprite);
    }
  }
  for (const edge of hexRouteEdges(map, layout)) {
    const p = axialToPixel(edge.from), q = axialToPixel(edge.to);
    roads.moveTo(p.x, p.y).lineTo(edge.terrain === 3 ? (p.x + q.x) / 2 : q.x, edge.terrain === 3 ? (p.y + q.y) / 2 : q.y)
      .stroke({ color: edge.terrain === 3 ? PAPER : TERRAIN_COLOURS[edge.terrain] ?? WOOD, width: edge.terrain === 2 ? 2.5 : 0.8, alpha: 0.55 });
  }
  for (const node of map.nodes) {
    const p = layout.points.get(node.id);
    if (!p) continue;
    dots.circle(p.x, p.y, node.level >= 3 ? 3 : 4).fill(PAPER).stroke({ width: 2, color: INK });
    const texture = textures.get(`town-${LEVELS[node.level]}`);
    if (texture) {
      const sprite = new Sprite(texture); sprite.anchor.set(0.5); sprite.position.set(p.x, p.y); sprite.visible = false;
      townSprites.set(node.id, sprite); emblems.addChild(sprite);
    }
  }
  terrain.addChild(tints, roads, edges, dots);
  terrain.cacheAsTexture({ resolution: 1, antialias: false });
  world.addChild(terrain, grid, emblems);
  return { terrain, grid, emblems, townSprites };
}

/** Only labelled places get large symbols; the small cached dots stay readable at fitted zoom. */
export function drawEmblems(graphics: Graphics, map: MapData, layout: HexLayout, visible: Set<number>, scale: number, sprites: Map<number, Sprite>): void {
  graphics.clear();
  for (const [id, sprite] of sprites) { sprite.visible = visible.has(id); sprite.width = sprite.height = 24 / scale; }
  for (const node of map.nodes) {
    if (!visible.has(node.id) || sprites.has(node.id)) continue;
    const p = layout.points.get(node.id)!;
    const radius = (node.level === 4 ? 3 : 5) / scale;
    if (node.level >= 3) graphics.rect(p.x - radius, p.y - radius, 2 * radius, 2 * radius).fill(node.level === 4 ? PAPER_DARK : PAPER).stroke({ color: INK, width: 1 / scale });
    else graphics.circle(p.x, p.y, radius).fill(PAPER_DARK).stroke({ color: NATION_COLOURS[node.id % 8]!, width: 2 / scale });
  }
}
