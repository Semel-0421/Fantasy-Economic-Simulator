// The scenario results screen: the statement with net profit as the score, the objectives, and
// Next scenario / Main menu (scenarios never save, so nothing else is offered).
import type { Views } from '../sim/types';
import { button, el } from './dom';

type Statement = NonNullable<Views['results']>;
const fmt = (n: number): string => n.toLocaleString('en-US');

export interface StatementRow {
  label: string;
  value: string;
  kind: 'plain' | 'cost' | 'total';
}

/** The statement's lines in reading order; costs are shown negative. */
export function statementRows(s: Statement): StatementRow[] {
  const cost = (label: string, n: number): StatementRow => ({ label, value: fmt(-n), kind: 'cost' });
  const rows: StatementRow[] = [
    { label: 'Starting coins', value: fmt(s.start_coins), kind: 'plain' },
    { label: 'Revenue', value: fmt(s.revenue), kind: 'plain' },
    cost('Purchases', s.costs.purchases),
    cost('Travel', s.costs.travel),
    cost('Guards and insurance', s.costs.protection),
    cost('Cargo lost', s.costs.losses),
    // The engine's residual line: licences, interest, and price gains on holdings (a negative cost).
    cost('Licences, interest and other (gains count as negative)', s.costs.other),
    { label: 'Ending coins', value: fmt(s.end_coins), kind: 'plain' },
  ];
  if (s.holdings !== 0) rows.push({ label: 'Shares and bonds held', value: fmt(s.holdings), kind: 'plain' });
  rows.push({ label: 'Net profit (your score)', value: fmt(s.net_profit), kind: 'total' });
  return rows;
}

export interface ResultsActions {
  /** Absent after the last scenario. */
  next?: () => void;
  menu(): void;
}

export function renderResults(root: HTMLElement, title: string, s: Statement, objectives: Views['objectives'], actions: ResultsActions): void {
  const table = el('table', { className: 'grid statement', id: 'statement' });
  for (const r of statementRows(s)) {
    table.append(el('tr', { className: r.kind }, el('th', { text: r.label }), el('td', { text: r.value })));
  }
  const goals = el(
    'ul',
    { className: 'rows', id: 'results-objectives' },
    ...objectives.map((o) => el('li', {}, el('span', { text: o.text }), el('span', { text: o.done ? `Done in week ${o.done_week}` : 'Not done' }))),
  );
  const buttons = el('div', { className: 'row' });
  if (actions.next) {
    const next = button('Next scenario', actions.next, 'next-scenario');
    next.classList.add('primary');
    buttons.append(next);
  }
  buttons.append(button('Main menu', actions.menu, 'results-menu'));
  root.replaceChildren(
    el(
      'main',
      { className: 'menu results', id: 'results' },
      el('h1', { text: `${title}: results` }),
      el('p', { id: 'score', className: 'score', text: `Net profit ${fmt(s.net_profit)} coins in ${s.weeks} weeks` }),
      table,
      el('h2', { text: 'Objectives' }),
      goals,
      buttons,
    ),
  );
}
