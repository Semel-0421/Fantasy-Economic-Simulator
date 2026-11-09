import { goodIcon } from '../assets/goods';
import { goodName } from '../data/names';
// The market of the town the player stands in (market view): goods, buy price, trader offer, stock
// and what the cart holds. Choosing a good opens the trade form; Buy or Sell first previews the
// command (cost, or the simulation's refusal) and only Confirm queues it for this week.
import type { CommandResult, PlayerCommand, Views } from '../sim/types';
import { button, el } from './dom';
import { errorBox, rejection } from './errors';

export type Quote = Views['market']['quotes'][number];
export type Side = 'buy' | 'sell';
export interface Estimate {
  units: number;
  unit: number;
  total: number;
}

const FX_ONE = 65536;
/** Market prices are whole coins carried as raw Fx. */
export const coinsOf = (raw: number): number => Math.floor(raw / FX_ONE);

/** `fine_cloth@best` -> `Fine cloth (best)`. */
export function goodLabel(key: string): string {
  const [base = key, grade] = key.split('@');
  const name = goodName(base);
  return grade ? `${name} (${grade})` : name;
}

/**
 * What the order would move at today's prices: a buy is capped by the stock, a sale by the cart.
 * The simulation may fill less still (cart room, coins, the traders' purse) when the week runs.
 */
export function estimate(side: Side, q: Quote, quantity: number, held: number): Estimate {
  const units = Math.max(0, Math.min(quantity, side === 'buy' ? q.stock : held));
  const unit = side === 'buy' ? coinsOf(q.price) : q.offer;
  return { units, unit, total: units * unit };
}

export function previewText(side: Side, label: string, quantity: number, e: Estimate): string {
  const verb = side === 'buy' ? 'Buy' : 'Sell';
  const amount = e.units === quantity ? `${quantity}` : `${e.units} of ${quantity}`;
  const cut = e.units === quantity ? '' : ` (only ${e.units} can be filled now)`;
  return `${verb} ${amount} ${label} at ${e.unit} each (estimated, at today's price): ${e.total} coins${cut}.`;
}

export interface MarketActions {
  preview(command: PlayerCommand): Promise<CommandResult>;
  queue(command: PlayerCommand): Promise<void>;
  ordered(text: string): void;
}

export interface MarketPanel {
  node: HTMLElement;
  update(town: string, quotes: Quote[], cart: Map<number, number>): void;
}

function row(cell: 'td' | 'th', items: (string | Node)[]): HTMLTableRowElement {
  return el('tr', {}, ...items.map((i) => el(cell, {}, i)));
}

function quantityInput(): HTMLInputElement {
  const qty = el('input', { id: 'qty', type: 'number' });
  Object.assign(qty, { min: '1', step: '1', value: '1', inputMode: 'numeric' });
  qty.setAttribute('aria-label', 'Quantity');
  return qty;
}

export function marketPanel(nameOf: (good: number) => string, actions: MarketActions, keyOf: (g: number) => string = nameOf): MarketPanel {
  const title = el('h2', { id: 'market-title', text: 'Market' });
  const body = el('tbody');
  const head = el('thead', {}, row('th', ['Good', 'Buy', 'Sell', 'Stock', 'Held']));
  const table = el('table', { id: 'market-table', className: 'grid' }, head, body);
  const chosen = el('p', { id: 'trade-good', text: 'Choose a good to trade.' });
  const qty = quantityInput();
  const deposit = el('input', { id: 'from-deposit', type: 'checkbox' });
  const preview = el('p', { id: 'trade-preview' });
  const error = errorBox('trade-error');
  const confirm = button('Confirm order', () => void place(), 'confirm');
  confirm.className = 'primary';
  const pending = el('div', { className: 'row', id: 'trade-confirm' }, confirm, button('Cancel', () => reset(), 'cancel'));
  const buy = button('Buy', () => void ask('buy'), 'buy');
  const sell = button('Sell', () => void ask('sell'), 'sell');
  let quotes: Quote[] = [];
  let cart = new Map<number, number>();
  let good = -1;
  let order: { command: PlayerCommand; text: string } | null = null;

  function reset(): void {
    order = null;
    preview.textContent = '';
    pending.hidden = true;
  }

  function setBusy(busy: boolean): void {
    for (const b of [buy, sell, confirm]) b.disabled = busy || good < 0;
  }

  async function ask(side: Side): Promise<void> {
    const q = quotes.find((x) => x.good === good);
    if (!q) return;
    const quantity = Math.floor(Number(qty.value));
    reset();
    const command: PlayerCommand =
      side === 'buy' ? { BuyGoods: { good, quantity, pay_from_deposit: deposit.checked } } : { SellGoods: { good, quantity } };
    setBusy(true);
    try {
      const refused = rejection(await actions.preview(command));
      error.show(refused ?? '');
      if (refused !== null) return;
      const text = previewText(side, nameOf(good), quantity, estimate(side, q, quantity, cart.get(good) ?? 0));
      order = { command, text };
      preview.textContent = text;
      pending.hidden = false;
    } catch (e) {
      error.show(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  async function place(): Promise<void> {
    if (!order) return;
    const { command, text } = order;
    setBusy(true);
    try {
      await actions.queue(command);
      actions.ordered(`Order for this week: ${text}`);
      reset();
    } catch (e) {
      error.show(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  function choose(g: number): void {
    good = g;
    chosen.textContent = nameOf(g);
    error.show('');
    reset();
    setBusy(false);
    for (const tr of body.rows) tr.classList.toggle('chosen', tr.dataset.good === String(g));
  }

  qty.addEventListener('input', reset);
  deposit.addEventListener('change', reset);
  reset();
  setBusy(false);

  const form = el(
    'div',
    { id: 'trade', className: 'trade' },
    chosen,
    el('div', { className: 'row' }, qty, buy, sell),
    el('label', { className: 'check' }, deposit, 'Pay the rest from my bank deposit'),
    error.node,
    preview,
    pending,
  );
  const node = el('section', { id: 'market', className: 'card' }, title, el('div', { className: 'scroll' }, table), form);
  return {
    node,
    update(town, list, held) {
      title.textContent = `Market - ${town}`;
      quotes = list;
      cart = held;
      body.replaceChildren(
        ...list.map((q) => {
          const pick = button(nameOf(q.good), () => choose(q.good));
          pick.className = 'link';
          pick.prepend(goodIcon(keyOf(q.good), nameOf(q.good)));
          const tr = row('td', [pick, String(coinsOf(q.price)), String(q.offer), String(q.stock), String(held.get(q.good) ?? 0)]);
          tr.dataset.good = String(q.good);
          tr.classList.toggle('chosen', q.good === good);
          return tr;
        }),
      );
      if (good >= 0 && !list.some((q) => q.good === good)) {
        good = -1;
        chosen.textContent = 'Choose a good to trade.';
      }
      reset();
      setBusy(false);
    },
  };
}
