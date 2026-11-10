import { t as i18nText, formatNumber, language } from '../i18n';
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

const fmt = formatNumber;
const TRADE_INTERNATIONAL = -1;
const TRADE_INTERCONTINENTAL = -2;

export function shareText(side: ShareSide, row: Listing, count: number, town: string): string {
  const verb = side === 'buy' ? i18nText("finance.buy") : i18nText("finance.sell");
  return i18nText("finance.shares-of-in-at-each-today-s-price-coins", { arg1: verb, arg2: count, arg3: goodLabel(row.name), arg4: town, arg5: row.price, arg6: fmt(count * row.price) });
}

/** Bonds sell in whole hundreds of coins. */
export const roundBond = (coins: number): number => Math.max(0, Math.floor(coins / 100) * 100);

export const bondText = (amount: number): string => i18nText("finance.buy-coins-of-this-nation-s-bonds-at-face", { arg1: fmt(amount) });

export function licenceName(baseGood: number, goodName: (g: number) => string): string {
  if (baseGood === TRADE_INTERNATIONAL) return i18nText("finance.international-trade-licence");
  if (baseGood === TRADE_INTERCONTINENTAL) return i18nText("finance.intercontinental-trade-licence");
  return i18nText("finance.licence-to-sell", { arg1: goodName(baseGood) });
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
  const confirm = button(i18nText("finance.confirm-order"), () => void place(), `${prefix}-confirm`);
  confirm.className = 'primary';
  const pending = el('div', { className: 'row' }, confirm, button(i18nText("finance.cancel"), () => reset(), `${prefix}-cancel`));
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
      actions.ordered(i18nText("finance.order-for-this-week", { arg1: text }));
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
  const head = el('thead', {}, el('tr', {}, ...[i18nText("finance.company"), i18nText("finance.price"), i18nText("finance.held"), i18nText("finance.for-sale")].map((h) => el('th', { text: h }))));
  const empty = el('p', { className: 'note', text: i18nText("finance.shares-trade-at-a-bank-of-their-nation") });
  const chosen = el('p', { id: 'share-chosen', text: i18nText("finance.choose-a-company") });
  const count = numberInput('share-count', i18nText("finance.shares"), '1');
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
    chosen.textContent = i18nText("finance.in-company", { arg1: goodLabel(row.name), arg2: townOf(row.node), arg3: row.facility });
    form.reset();
  };
  count.addEventListener('input', () => form.reset());
  const node = el(
    'section',
    { id: 'exchange', className: 'card' },
    el('h2', { text: i18nText("finance.exchange") }),
    empty,
    el('div', { className: 'scroll' }, el('table', { id: 'exchange-table', className: 'grid' }, head, body)),
    chosen,
    el('div', { className: 'row' }, count, button(i18nText("finance.buy"), () => ask('buy'), 'share-buy'), button(i18nText("finance.sell"), () => ask('sell'), 'share-sell')),
    ...form.nodes,
  );
  return {
    node,
    update(next) {
      list = next;
      empty.hidden = next.length > 0;
      body.replaceChildren(
        ...next.map((r) => {
          const pick = button(i18nText('finance.company-choice', { good: goodLabel(r.name), town: townOf(r.node) }), () => choose(r), `share-${r.facility}`);
          pick.className = 'link';
          const tr = el('tr', {}, el('td', {}, pick), ...[r.price, r.held, r.room].map((v) => el('td', { text: fmt(v) })));
          tr.dataset.facility = String(r.facility);
          return tr;
        }),
      );
      if (!next.some((r) => r.facility === picked)) {
        picked = -1;
        chosen.textContent = i18nText("finance.choose-a-company");
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
  const amount = numberInput('bond-amount', i18nText("finance.bond-amount-coins"), '100');
  amount.step = '100';
  const form = orderForm('bond', actions);
  let coins = 0;
  const ask = (): void => {
    const n = Math.floor(Number(amount.value));
    void form.ask({ BuyBond: { issuer: -1, amount: n } }, bondText(n));
  };
  const all = button(
    i18nText("finance.all-coins"),
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
    el('h2', { text: i18nText("finance.bonds") }),
    held,
    el('div', { className: 'row' }, amount, all, button(i18nText("finance.buy-the-nation-s-bonds"), ask, 'bond-buy')),
    ...form.nodes,
  );
  return {
    node,
    update(purse) {
      coins = purse.coins[purse.local_continent] ?? 0;
      const face = purse.bonds.reduce((s, [, a]) => s + a, 0);
      held.textContent = face > 0 ? i18nText("finance.bonds-held-coins-at-face", { arg1: fmt(face) }) : i18nText("finance.no-bonds-held");
      form.reset();
    },
  };
}

export interface LicencePanel {
  node: HTMLElement;
  update(held: Views['licences']): void;
}

export function heldLicenceText(held: Views['licences'][number], baseName: (base: number) => string, nationName: (nation: number) => string,
  dateLabel?: DateLabel): string {
  const week = dateLabel ? dateLabel.weekOf?.(held.expires_turn) : held.expires_turn + 1;
  const date = week === undefined ? dateLabel!(held.expires_turn) : i18nText('finance.week', { arg1: week });
  const expiry = week === undefined ? i18nText("finance.week-3", { arg1: held.expires_turn + 1, arg2: date }) : language.code === 'en' ? date.toLowerCase() : date;
  return i18nText("finance.expires", { arg1: licenceName(held.base_good, baseName), arg2: nationName(held.nation), arg3: expiry });
}

/** Trade licences and licences to sell a good, bought in the nation the player stands in. */
export function licencePanel(goods: Views['goods'], goodName: (g: number) => string, actions: OrderActions,
  source: { csv: string; scenario: string; nation: (nation: number) => string; dateLabel: DateLabel }): LicencePanel {
  const list = el('ul', { id: 'licences-held', className: 'rows' });
  const select = el('select', { id: 'licence-good' });
  select.setAttribute('aria-label', i18nText("finance.good"));
  const form = orderForm('licence', actions);
  const all = licenceChoices(goods, source.csv, source.scenario, true);
  const baseNames = new Map(all.map((good) => [good.base, good.name]));
  const baseName = (base: number): string => baseNames.get(base) ?? i18nText("finance.good-2", { arg1: base });
  const showAll = el('input', { type: 'checkbox', id: 'licence-show-all' });
  const trade = (kind: number) => (): void =>
    void form.ask({ BuyTradeLicense: { kind } }, i18nText("finance.buy-an-for-a-year", { arg1: language.code === 'en' ? licenceName(kind, goodName).toLowerCase() : licenceName(kind, goodName) }));
  const forGood = (): void => {
    if (!select.value) return;
    const good = Number(select.value);
    void form.ask({ BuyLicense: { good } }, i18nText("finance.buy-a-licence-to-sell-in-this-nation-for-a-year", { arg1: select.selectedOptions[0]!.text }));
  };
  const buy = button(i18nText("finance.buy-licence"), forGood, 'licence-buy');
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
    el('h2', { text: i18nText("finance.licences") }),
    list,
    el('p', { className: 'note', text: i18nText("finance.international-trade-between-nations-intercontinental") }),
    el(
      'div',
      { className: 'row' },
      button(i18nText("finance.international-trade-licence"), trade(TRADE_INTERNATIONAL), 'licence-trade'),
      button(i18nText("finance.intercontinental-trade-licence"), trade(TRADE_INTERCONTINENTAL), 'licence-trade-far'),
    ),
    el('label', { className: 'check' }, showAll, i18nText("finance.show-all-goods")),
    el('div', { className: 'row' }, select, buy),
    ...form.nodes,
  );
  return {
    node,
    update(held) {
      list.replaceChildren(
        ...(held.length ? held.map((l) => el('li', { text: heldLicenceText(l, baseName, source.nation, source.dateLabel) })) : [el('li', { text: i18nText("finance.no-licences-held") })]),
      );
      form.reset();
    },
  };
}
