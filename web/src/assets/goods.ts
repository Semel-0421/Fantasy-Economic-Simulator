import { MANIFEST, goodAsset } from './manifest';
import { textures } from './loader';
import { el } from '../ui/dom';

/** A 24px chip preserves the good's text identity while art is being produced. */
export function goodIcon(key: string, name: string): HTMLElement {
  const id = goodAsset(key);
  if (textures.has(id)) {
    const img = el('img', { className: 'good-icon' });
    img.src = `assets/${MANIFEST[id]!.path}`;
    img.alt = '';
    img.width = img.height = 24;
    return img;
  }
  const chip = el('span', { className: 'good-icon good-chip' });
  chip.dataset.initial = name.charAt(0).toUpperCase();
  chip.dataset.category = /grain|wheat|barley|^g1_/.test(key) ? 'food' : /iron|copper|tools|^g6_/.test(key) ? 'metal' : 'trade';
  chip.setAttribute('aria-hidden', 'true');
  return chip;
}
