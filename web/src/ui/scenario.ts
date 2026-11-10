import { t as i18nText, formatNumber } from '../i18n';
// The scenario HUD panel (objectives, the deadline, offered contracts) and the scenario parts of a
// turn report (news dialogs, the end).
import type { TurnReport, Views } from '../sim/types';
import type { DialogText } from './dialog';
import { button, el } from './dom';
import { displayText } from '../data/names';
import { translateScenario } from '../i18n/scenario';

type Contract = Views['scenario_contracts'][number];
type Objective = Views['objectives'][number];
const fmt = formatNumber;

/** The news dialogs a turn brought, in order. */
export function scenarioDialogs(report: TurnReport): DialogText[] {
  const out: DialogText[] = [];
  for (const e of report.events) {
    if (typeof e === 'object' && 'ScenarioDialog' in e) {
      const d = e.ScenarioDialog;
      const text = translateScenario(d.text);
      out.push({ title: i18nText("scenario.news"), text: displayText(d.coins > 0 ? i18nText("scenario.you-receive-coins", { arg1: text, arg2: fmt(d.coins) }) : text) });
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
  return i18nText("scenario.week-of", { arg1: played + 1, arg2: weeks, arg3: left <= 1 ? i18nText("scenario.last-week") : i18nText("scenario.weeks-left", { arg1: left }) });
}

export function contractLine(c: Contract, goodName: (g: number) => string, nodeName: (n: number) => string): string {
  return i18nText("scenario.to-by-week-coins", { arg1: c.quantity, arg2: goodName(c.good), arg3: nodeName(c.node), arg4: c.deadline_week, arg5: fmt(c.reward) });
}

function contractState(c: Contract): string {
  if (c.delivered) return i18nText("scenario.delivered");
  if (c.expired) return i18nText("scenario.expired");
  return c.accepted ? i18nText("scenario.accepted") : '';
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
    el('h3', { text: i18nText("scenario.objectives") }),
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
            el('span', { text: o.done ? i18nText("scenario.done-week", { arg1: o.done_week }) : i18nText("scenario.to-do") }),
          ),
        ),
      );
      offers.replaceChildren(
        ...contracts.map((c) => {
          const row = el('li', { className: 'contract' }, el('span', { text: contractLine(c, names.good, names.node), title: c.text }));
          const state = contractState(c);
          row.append(state ? el('span', { text: state }) : button(i18nText("scenario.accept"), () => accept(c.id), `accept-${c.id}`));
          return row;
        }),
      );
    },
  };
}
