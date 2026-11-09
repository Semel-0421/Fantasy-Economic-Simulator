// The cart and the player's warehouses (cargo view).
import type { Views } from '../sim/types';
import { el } from './dom';
import { goodIcon } from '../assets/goods';

export interface CartRow {
  good: number;
  name: string;
  quantity: number;
}

export function cartRows(cart: [number, number][], nameOf: (good: number) => string): CartRow[] {
  return cart.filter(([, q]) => q > 0).map(([good, quantity]) => ({ good, name: nameOf(good), quantity }));
}

export function cargoPanel(nameOf: (good: number) => string, keyOf: (g: number) => string = nameOf): { node: HTMLElement; update(cargo: Views['cargo']): void } {
  const list = el('ul', { id: 'cart', className: 'rows' });
  const extra = el('p', { className: 'note', id: 'cargo-extra' });
  return {
    node: el('section', { id: 'cargo', className: 'card' }, el('h2', { text: 'Cart' }), list, extra),
    update(cargo) {
      const rows = cartRows(cargo.cart as [number, number][], nameOf);
      list.replaceChildren(
        ...(rows.length
          ? rows.map((r) => el('li', {}, el('span', {}, goodIcon(keyOf(r.good), r.name), r.name), el('strong', { text: String(r.quantity) })))
          : [el('li', { className: 'note', text: 'The cart is empty.' })]),
      );
      const stored = cargo.warehouses.reduce((s, w) => s + w[2], 0);
      const parts = [stored > 0 ? `${stored} units in your warehouses` : '', cargo.ship_owned ? 'you own a ship' : ''];
      extra.textContent = parts.filter(Boolean).join('; ');
    },
  };
}
