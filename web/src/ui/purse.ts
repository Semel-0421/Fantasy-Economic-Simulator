import { t as i18nText, formatNumber } from '../i18n';
// Coins per continent (the local coin first), bank deposits and loans (purse view).
import type { Views } from '../sim/types';
import { el } from './dom';

export interface PurseRow {
  label: string;
  value: number;
}

type PurseParts = Pick<Views['purse'], 'coins' | 'local_continent' | 'deposits' | 'loans'>;

export function purseRows(purse: PurseParts, continents: string[]): PurseRow[] {
  const name = (i: number): string => continents[i] ?? i18nText("purse.continent", { arg1: i });
  const here = purse.local_continent;
  const rows: PurseRow[] = [{ label: i18nText("purse.coins-here", { arg1: name(here) }), value: purse.coins[here] ?? 0 }];
  purse.coins.forEach((c, i) => {
    if (i !== here) rows.push({ label: i18nText("purse.coins", { arg1: name(i) }), value: c });
  });
  const deposits = purse.deposits.reduce((s, d) => s + d, 0);
  if (deposits !== 0) rows.push({ label: i18nText("purse.bank-deposits"), value: deposits });
  const owed = purse.loans.reduce((s, l) => s + l.balance, 0);
  if (owed !== 0) rows.push({ label: i18nText("purse.loans-owed"), value: owed });
  return rows;
}

export function pursePanel(continents: string[]): { node: HTMLElement; update(purse: Views['purse']): void } {
  const list = el('ul', { id: 'purse-rows', className: 'rows' });
  return {
    node: el('section', { id: 'purse', className: 'card' }, el('h2', { text: i18nText("purse.purse") }), list),
    update(purse) {
      list.replaceChildren(
        ...purseRows(purse, continents).map((r) =>
          el('li', {}, el('span', { text: r.label }), el('strong', { text: formatNumber(r.value) })),
        ),
      );
    },
  };
}
