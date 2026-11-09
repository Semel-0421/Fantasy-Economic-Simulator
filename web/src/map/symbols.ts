import { Graphics, Sprite, type Container } from 'pixi.js';
import { textures } from '../assets/loader';
import { INK, PAPER, PAPER_DARK, GOLD, SEA, TERRAIN_COLOURS } from './colours';
import { LEVEL, routePairs, type MapData, type Layout } from './layout';
import { button, el } from '../ui/dom';

const TOWNS = ['continent', 'nation', 'region', 'city', 'village'];

export function drawWorld(map: MapData, layout: Layout, world: Container): { sprite: Sprite; level: number }[] {
  const g = new Graphics();
  const sprites: { sprite: Sprite; level: number }[] = [];
  for (const node of map.nodes) {
    const box = layout.boxes.get(node.id);
    if (box) g.rect(box.x, box.y, box.w, box.h).stroke({ width: node.level === LEVEL.continent ? 4 : 2, color: INK, alpha: 0.3 });
  }
  for (const route of routePairs(map)) {
    const a = layout.points.get(route.from)!, b = layout.points.get(route.to)!;
    const distance = Math.hypot(b.x - a.x, b.y - a.y);
    if (route.terrain === 3) {
      for (let d = 0; d < distance; d += 20) {
        const end = Math.min(distance, d + 10);
        g.moveTo(a.x + (b.x - a.x) * d / distance, a.y + (b.y - a.y) * d / distance)
          .lineTo(a.x + (b.x - a.x) * end / distance, a.y + (b.y - a.y) * end / distance);
      }
    } else g.moveTo(a.x, a.y).lineTo(b.x, b.y);
    g.stroke({ width: 2 + Math.min(route.turns, 4), color: TERRAIN_COLOURS[route.terrain] ?? INK, alpha: 0.8 });
  }
  for (const node of map.nodes) {
    const p = layout.points.get(node.id)!;
    const texture = textures.get(`town-${TOWNS[node.level]}`);
    if (texture) {
      const sprite = new Sprite(texture);
      sprite.anchor.set(0.5);
      sprite.position.set(p.x, p.y);
      sprites.push({ sprite, level: node.level });
    }
    if (node.level === LEVEL.village) g.circle(p.x, p.y, 7).fill(PAPER_DARK).stroke({ width: 2, color: INK });
    else if (node.level === LEVEL.city) g.rect(p.x - 12, p.y - 12, 24, 24).fill(PAPER).stroke({ width: 4, color: INK });
    else if (node.level === LEVEL.region) g.poly([p.x, p.y - 15, p.x + 15, p.y, p.x, p.y + 15, p.x - 15, p.y]).fill(PAPER_DARK).stroke({ width: 3, color: SEA });
    else if (node.level === LEVEL.nation) g.poly([p.x - 18, p.y - 18, p.x + 18, p.y - 18, p.x + 18, p.y + 6, p.x, p.y + 22, p.x - 18, p.y + 6]).fill(PAPER).stroke({ width: 4, color: INK });
    else g.poly([p.x - 24, p.y, p.x - 12, p.y - 21, p.x + 12, p.y - 21, p.x + 24, p.y, p.x + 12, p.y + 21, p.x - 12, p.y + 21]).fill(PAPER_DARK).stroke({ width: 4, color: SEA });
  }
  world.addChild(g, ...sprites.map((entry) => entry.sprite));
  return sprites;
}

export function playerMark(g: Graphics, x: number, y: number, scale: number): void {
  const r = 8 / scale;
  g.poly([x, y - r, x + r, y, x, y + r, x - r, y]).fill(GOLD).stroke({ width: 2 / scale, color: INK });
}

export function mapControls(zoom: (factor: number) => void, fit: () => void): HTMLElement {
  const node = el('div', { className: 'map-controls' });
  for (const [label, path, action] of [
    ['Zoom in', 'M12 4V20 M4 12H20', () => zoom(1.5)],
    ['Zoom out', 'M4 12H20', () => zoom(1 / 1.5)],
    ['Fit map', 'M4 9V4H9 M15 4H20V9 M20 15V20H15 M9 20H4V15', fit],
  ] as const) {
    const b = button('', action);
    b.title = label;
    b.setAttribute('aria-label', label);
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    const line = document.createElementNS(svg.namespaceURI, 'path');
    line.setAttribute('d', path);
    line.setAttribute('fill', 'none');
    line.setAttribute('stroke', 'currentColor');
    line.setAttribute('stroke-width', '2');
    svg.append(line);
    b.append(svg);
    node.append(b);
  }
  return node;
}
