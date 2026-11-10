import { t as i18nText, formatNumber } from '../i18n';
// The scenario results screen: the statement with net profit as the score, the objectives, and
// Next scenario / Main menu (scenarios never save, so nothing else is offered).
import type { Views } from '../sim/types';
import { button, el } from './dom';
import { play } from '../audio/sound';

type Statement = NonNullable<Views['results']>;
const fmt = formatNumber;

export interface StatementRow {
  label: string;
  value: string;
  kind: 'plain' | 'cost' | 'total';
}

/** The statement's lines in reading order; costs are shown negative. */
export function statementRows(s: Statement): StatementRow[] {
  const cost = (label: string, n: number): StatementRow => ({ label, value: fmt(-n), kind: 'cost' });
  const rows: StatementRow[] = [
    { label: i18nText("results.starting-coins"), value: fmt(s.start_coins), kind: 'plain' },
    { label: i18nText("results.revenue"), value: fmt(s.revenue), kind: 'plain' },
    cost(i18nText("results.purchases"), s.costs.purchases),
    cost(i18nText("results.travel"), s.costs.travel),
    cost(i18nText("results.guards-and-insurance"), s.costs.protection),
    cost(i18nText("results.cargo-lost"), s.costs.losses),
    // The engine's residual line: licences, interest, and price gains on holdings (a negative cost).
    cost(i18nText("results.licences-interest-and-other-costs-gains-shown-as-neg"), s.costs.other),
    { label: i18nText("results.ending-coins"), value: fmt(s.end_coins), kind: 'plain' },
  ];
  if (s.holdings !== 0) rows.push({ label: i18nText("results.shares-and-bonds-held"), value: fmt(s.holdings), kind: 'plain' });
  rows.push({ label: i18nText("results.net-profit-your-score"), value: fmt(s.net_profit), kind: 'total' });
  return rows;
}

export interface ResultsActions {
  /** Absent after the last scenario. */
  next?: () => void;
  menu(): void;
}

export function renderResults(root: HTMLElement, title: string, s: Statement, objectives: Views['objectives'], actions: ResultsActions, target = 0): void {
  play('results');
  const table = el('table', { className: 'grid statement', id: 'statement' });
  table.setAttribute('aria-label', i18nText("results.financial-statement"));
  for (const r of statementRows(s)) {
    const heading = el('th', { text: r.label });
    heading.scope = 'row';
    table.append(el('tr', { className: r.kind }, heading, el('td', { text: r.value })));
  }
  const goals = el(
    'ul',
    { className: 'rows', id: 'results-objectives' },
    ...objectives.map((o) => {
      const mark = el('span', { className: `objective-mark${o.done ? ' checked' : ''}` });
      mark.setAttribute('aria-hidden', 'true');
      return el('li', {}, mark, el('span', { text: o.text }), el('span', { text: o.done ? i18nText("results.done-in-week", { arg1: o.done_week }) : i18nText("results.not-done") }));
    }),
  );
  const buttons = el('div', { className: 'row' });
  if (actions.next) {
    const next = button(i18nText("results.next-scenario"), actions.next, 'next-scenario');
    next.classList.add('primary');
    buttons.append(next);
  }
  buttons.append(button(i18nText("results.main-menu"), actions.menu, 'results-menu'));
  root.replaceChildren(
    el(
      'main',
      { className: 'menu results', id: 'results' },
      el('h1', { text: i18nText("results.results", { arg1: title }) }),
      el('p', { id: 'score', className: 'score', text: i18nText("results.net-profit-coins-in-weeks", { arg1: fmt(s.net_profit), arg2: s.weeks }) }),
      el('p', { id: 'results-target', text: i18nText("results.target-you", { arg1: fmt(target), arg2: fmt(s.net_profit), arg3: target > 0 ? i18nText("results.x", { arg1: (s.net_profit / target).toFixed(1) }) : '' }) }),
      table,
      el('h2', { text: i18nText("results.objectives") }),
      goals,
      buttons,
    ),
  );
}
