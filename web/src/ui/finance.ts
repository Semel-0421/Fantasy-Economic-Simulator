// Panels for the scenario 2 and 3 tools: the exchange (shares listed at the bank the player stands
// in), the nation's bonds, and licences. Each order is previewed first; a refusal shows the
// simulation's own message, and only Confirm queues it for this week.
import type { CommandResult, PlayerCommand, Views } from '../sim/types';
import { button, el } from './dom';
import { errorBox, rejection } from './errors';
import { goodLabel } from './market';
import { licenceChoices } from './licence_goods';
import type { DateLabel } from './turn';

export type Listing = Views['exchange'][number];
export type ShareSide = 'buy' | 'sell';

export interface OrderActions {
  preview(command: PlayerCommand): Promise<CommandResult>;
  queue(command: PlayerCommand): Promise<void>;
  ordered(text: string): void;
}

const fmt = (n: number): string => n.toLocaleString('en-US');
const TRADE_INTERNATIONAL = -1;
const TRADE_INTERCONTINENTAL = -2;

export function shareText(side: ShareSide, row: Listing, count: number, town: string): string {
  const verb = side === 'buy' ? 'Buy' : 'Sell';
  return `${verb} ${count} shares of ${goodLabel(row.name)} in ${town} at ${row.price} each (today's price): ${fmt(count * row.price)} coins.`;
}

/** Bonds sell in whole hundreds of coins. */
export const roundBond = (coins: number): number => Math.max(0, Math.floor(coins / 100) * 100);

export const bondText = (amount: number): string => `Buy ${fmt(amount)} coins of this nation's bonds at face.`;

export function licenceName(baseGood: number, goodName: (g: number) => string): string {
  if (baseGood === TRADE_INTERNATIONAL) return 'International trade licence';
  if (baseGood === TRADE_INTERCONTINENTAL) return 'Intercontinental trade licence';
  return `Licence to sell ${goodName(baseGood)}`;
}

interface OrderForm {
  nodes: HTMLElement[];
  ask(command: PlayerCommand, text: string): Promise<void>;
  reset(): void;
}

/** Preview, then Confirm or Cancel: the part every order form shares. `prefix` names its ids. */
function orderForm(prefix: string, actions: OrderActions): OrderForm {
  const preview = el('p', { id: `${prefix}-preview` });
  const error = errorBox(`${prefix}-error`);
  let order: { command: PlayerCommand; text: string } | null = null;
  const confirm = button('Confirm order', () => void place(), `${prefix}-confirm`);
  confirm.className = 'primary';
  const pending = el('div', { className: 'row' }, confirm, button('Cancel', () => reset(), `${prefix}-cancel`));
  const show = (e: unknown): void => error.show(e instanceof Error ? e.message : String(e));
  function reset(): void {
    order = null;
    preview.textContent = '';
    pending.hidden = true;
  }
  async function place(): Promise<void> {
    if (!order) return;
    const { command, text } = order;
    confirm.disabled = true;
    try {
      await actions.queue(command);
      actions.ordered(`Order for this week: ${text}`);
      reset();
    } catch (e) {
      show(e);
    } finally {
      confirm.disabled = false;
    }
  }
  reset();
  return {
    nodes: [error.node, preview, pending],
    reset,
    async ask(command, text) {
      reset();
      try {
        const refused = rejection(await actions.preview(command));
        error.show(refused ?? '');
        if (refused !== null) return;
        order = { command, text };
        preview.textContent = text;
        pending.hidden = false;
      } catch (e) {
        show(e);
      }
    },
  };
}

function numberInput(id: string, label: string, value: string): HTMLInputElement {
  const input = el('input', { id, type: 'number' });
  Object.assign(input, { min: '1', step: '1', value, inputMode: 'numeric' });
  input.setAttribute('aria-label', label);
  return input;
}

export interface ExchangePanel {
  node: HTMLElement;
  update(list: Listing[]): void;
}

export function exchangePanel(townOf: (node: number) => string, actions: OrderActions): ExchangePanel {
  const body = el('tbody');
  const head = el('thead', {}, el('tr', {}, ...['Company', 'Price', 'Held', 'For sale'].map((h) => el('th', { text: h }))));
  const empty = el('p', { className: 'note', text: 'Shares trade at a bank of their nation.' });
  const chosen = el('p', { id: 'share-chosen', text: 'Choose a company.' });
  const count = numberInput('share-count', 'Shares', '1');
  const form = orderForm('share', actions);
  let list: Listing[] = [];
  let picked = -1;
  const ask = (side: ShareSide): void => {
    const row = list.find((r) => r.facility === picked);
    const n = Math.floor(Number(count.value));
    if (!row) return;
    const command: PlayerCommand = side === 'buy' ? { BuyShare: { facility: picked, count: n } } : { SellShare: { facility: picked, count: n } };
    void form.ask(command, shareText(side, row, n, townOf(row.node)));
  };
  const choose = (row: Listing): void => {
    picked = row.facility;
    chosen.textContent = `${goodLabel(row.name)} in ${townOf(row.node)} (company ${row.facility})`;
    form.reset();
  };
  count.addEventListener('input', () => form.reset());
  const node = el(
    'section',
    { id: 'exchange', className: 'card' },
    el('h2', { text: 'Exchange' }),
    empty,
    el('div', { className: 'scroll' }, el('table', { id: 'exchange-table', className: 'grid' }, head, body)),
    chosen,
    el('div', { className: 'row' }, count, button('Buy', () => ask('buy'), 'share-buy'), button('Sell', () => ask('sell'), 'share-sell')),
    ...form.nodes,
  );
  return {
    node,
    update(next) {
      list = next;
      empty.hidden = next.length > 0;
      body.replaceChildren(
        ...next.map((r) => {
          const pick = button(`${goodLabel(r.name)}, ${townOf(r.node)}`, () => choose(r), `share-${r.facility}`);
          pick.className = 'link';
          const tr = el('tr', {}, el('td', {}, pick), ...[r.price, r.held, r.room].map((v) => el('td', { text: fmt(v) })));
          tr.dataset.facility = String(r.facility);
          return tr;
        }),
      );
      if (!next.some((r) => r.facility === picked)) {
        picked = -1;
        chosen.textContent = 'Choose a company.';
      }
      form.reset();
    },
  };
}

export interface BondsPanel {
  node: HTMLElement;
  update(purse: Views['purse']): void;
}

/** The nation's bonds at the bank the player stands in (issuer -1); held bonds at face. */
export function bondsPanel(actions: OrderActions): BondsPanel {
  const held = el('p', { id: 'bonds-held' });
  const amount = numberInput('bond-amount', 'Coins of bonds', '100');
  amount.step = '100';
  const form = orderForm('bond', actions);
  let coins = 0;
  const ask = (): void => {
    const n = Math.floor(Number(amount.value));
    void form.ask({ BuyBond: { issuer: -1, amount: n } }, bondText(n));
  };
  const all = button(
    'All coins',
    () => {
      amount.value = String(roundBond(coins));
      form.reset();
    },
    'bond-all',
  );
  amount.addEventListener('input', () => form.reset());
  const node = el(
    'section',
    { id: 'bonds', className: 'card' },
    el('h2', { text: 'Bonds' }),
    held,
    el('div', { className: 'row' }, amount, all, button("Buy the nation's bonds", ask, 'bond-buy')),
    ...form.nodes,
  );
  return {
    node,
    update(purse) {
      coins = purse.coins[purse.local_continent] ?? 0;
      const face = purse.bonds.reduce((s, [, a]) => s + a, 0);
      held.textContent = face > 0 ? `Bonds held: ${fmt(face)} coins at face.` : 'No bonds held.';
      form.reset();
    },
  };
}

export interface LicencePanel {
  node: HTMLElement;
  update(held: Views['licences']): void;
}

export function heldLicenceText(held: Views['licences'][number], baseName: (base: number) => string, nationName: (nation: number) => string,
  dateLabel: DateLabel = (turn) => `Week ${turn + 1}`): string {
  const date = dateLabel(held.expires_turn);
  const expiry = date.startsWith('Week ') ? date.toLowerCase() : `week ${held.expires_turn + 1} (${date})`;
  return `${licenceName(held.base_good, baseName)} · ${nationName(held.nation)} · expires ${expiry}`;
}

/** Trade licences and licences to sell a good, bought in the nation the player stands in. */
export function licencePanel(goods: Views['goods'], goodName: (g: number) => string, actions: OrderActions,
  source: { csv: string; scenario: string; nation: (nation: number) => string; dateLabel: DateLabel }): LicencePanel {
  const list = el('ul', { id: 'licences-held', className: 'rows' });
  const select = el('select', { id: 'licence-good' });
  select.setAttribute('aria-label', 'Good');
  const form = orderForm('licence', actions);
  const all = licenceChoices(goods, source.csv, source.scenario, true);
  const baseNames = new Map(all.map((good) => [good.base, good.name]));
  const baseName = (base: number): string => baseNames.get(base) ?? `Good ${base}`;
  const showAll = el('input', { type: 'checkbox', id: 'licence-show-all' });
  const trade = (kind: number) => (): void =>
    void form.ask({ BuyTradeLicense: { kind } }, `Buy an ${licenceName(kind, goodName).toLowerCase()} for a year.`);
  const forGood = (): void => {
    if (!select.value) return;
    const good = Number(select.value);
    void form.ask({ BuyLicense: { good } }, `Buy a licence to sell ${select.selectedOptions[0]!.text} in this nation for a year.`);
  };
  const buy = button('Buy licence', forGood, 'licence-buy');
  const populate = (): void => {
    const previous = select.value;
    const choices = showAll.checked ? all : licenceChoices(goods, source.csv, source.scenario, false);
    select.replaceChildren(...choices.map((good) => new Option(good.name, String(good.id))));
    if (choices.some((good) => String(good.id) === previous)) select.value = previous;
    buy.disabled = choices.length === 0;
    form.reset();
  };
  showAll.addEventListener('change', populate);
  populate();
  select.addEventListener('change', () => form.reset());
  const node = el(
    'section',
    { id: 'licences', className: 'card' },
    el('h2', { text: 'Licences' }),
    list,
    el('p', { className: 'note', text: 'International: trade between nations. Intercontinental: trade between continents.' }),
    el(
      'div',
      { className: 'row' },
      button('International trade licence', trade(TRADE_INTERNATIONAL), 'licence-trade'),
      button('Intercontinental trade licence', trade(TRADE_INTERCONTINENTAL), 'licence-trade-far'),
    ),
    el('label', { className: 'check' }, showAll, 'Show all goods'),
    el('div', { className: 'row' }, select, buy),
    ...form.nodes,
  );
  return {
    node,
    update(held) {
      list.replaceChildren(
        ...(held.length ? held.map((l) => el('li', { text: heldLicenceText(l, baseName, source.nation, source.dateLabel) })) : [el('li', { text: 'No licences held.' })]),
      );
      form.reset();
    },
  };
}
