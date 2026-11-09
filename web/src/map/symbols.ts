import { Graphics } from 'pixi.js';
import { INK, GOLD } from './colours';
import { button, el } from '../ui/dom';

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
