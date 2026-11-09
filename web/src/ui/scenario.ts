// The scenario HUD panel (objectives, the deadline, offered contracts) and the scenario parts of a
// turn report (news dialogs, the end).
import type { TurnReport, Views } from '../sim/types';
import type { DialogText } from './dialog';
import { button, el } from './dom';
import { displayText } from '../data/names';

type Contract = Views['scenario_contracts'][number];
type Objective = Views['objectives'][number];
const fmt = (n: number): string => n.toLocaleString('en-US');

/** The news dialogs a turn brought, in order. */
export function scenarioDialogs(report: TurnReport): DialogText[] {
  const out: DialogText[] = [];
  for (const e of report.events) {
    if (typeof e === 'object' && 'ScenarioDialog' in e) {
      const d = e.ScenarioDialog;
      out.push({ title: 'News', text: displayText(d.coins > 0 ? `${d.text}\n\nYou receive ${fmt(d.coins)} coins.` : d.text) });
    }
  }
  return out;
}

/** The ScenarioEnded event of a turn, or null. */
/** A drawn check box (decorative; the row's text says done or to do). */
function objectiveMark(done: boolean): HTMLElement {
  const mark = el('span', { className: `objective-mark${done ? ' checked' : ''}` });
  mark.setAttribute('aria-hidden', 'true');
  return mark;
}

export function scenarioEnd(report: TurnReport): { net_profit: number; objectives_done: number; turn: number } | null {
  for (const e of report.events) if (typeof e === 'object' && 'ScenarioEnded' in e) return e.ScenarioEnded;
  return null;
}

/** "Week 3 of 20 - 18 weeks left" after `played` weeks. */
export function deadlineText(played: number, weeks: number): string {
  const left = weeks - played;
  return `Week ${played + 1} of ${weeks} - ${left <= 1 ? 'last week' : `${left} weeks left`}`;
}

export function contractLine(c: Contract, goodName: (g: number) => string, nodeName: (n: number) => string): string {
  return `${c.quantity} ${goodName(c.good)} to ${nodeName(c.node)} by week ${c.deadline_week} - ${fmt(c.reward)} coins`;
}

function contractState(c: Contract): string {
  if (c.delivered) return 'Delivered';
  if (c.expired) return 'Expired';
  return c.accepted ? 'Accepted' : '';
}

export interface ScenarioPanel {
  node: HTMLElement;
  update(played: number, objectives: Objective[], contracts: Contract[]): void;
}

export function scenarioPanel(
  title: string,
  weeks: number,
  names: { good(g: number): string; node(n: number): string },
  accept: (id: string) => void,
): ScenarioPanel {
  const deadline = el('p', { id: 'deadline' });
  const goals = el('ul', { id: 'objectives', className: 'rows' });
  const offers = el('ul', { id: 'scenario-contracts', className: 'rows' });
  const node = el(
    'section',
    { className: 'card scenario-card' },
    el('h2', { text: title }),
    deadline,
    el('h3', { text: 'Objectives' }),
    goals,
    offers,
  );
  return {
    node,
    update(played, objectives, contracts) {
      deadline.textContent = deadlineText(Math.min(played, weeks - 1), weeks);
      goals.replaceChildren(
        ...objectives.map((o) =>
          el(
            'li',
            { className: o.done ? 'done' : '' },
            objectiveMark(o.done),
            el('span', { className: 'objective-text', text: o.text }),
            el('span', { text: o.done ? `Done, week ${o.done_week}` : 'To do' }),
          ),
        ),
      );
      offers.replaceChildren(
        ...contracts.map((c) => {
          const row = el('li', { className: 'contract' }, el('span', { text: contractLine(c, names.good, names.node), title: c.text }));
          const state = contractState(c);
          row.append(state ? el('span', { text: state }) : button('Accept', () => accept(c.id), `accept-${c.id}`));
          return row;
        }),
      );
    },
  };
}
