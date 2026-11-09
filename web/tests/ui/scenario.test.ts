// Pure logic of the scenario screens: dialogs from a report, the deadline line, the results rows.
import { describe, expect, it } from 'vitest';
import { contractLine, deadlineText, scenarioDialogs, scenarioEnd } from '../../src/ui/scenario';
import { statementRows } from '../../src/ui/results';
import { nextScenario, SCENARIOS } from '../../src/data/scenarios';
import type { TurnReport, Views } from '../../src/sim/types';

const statement: NonNullable<Views['results']> = {
  revenue: 2000,
  costs: { purchases: 700, travel: 50, protection: 64, losses: 0, other: 127 },
  net_profit: 1059,
  start_coins: 500,
  end_coins: 1559,
  holdings: 0,
  weeks: 20,
};

describe('scenario screens', () => {
  it('lists three scenarios in order and knows which comes next', () => {
    expect(SCENARIOS.map((s) => s.file)).toEqual(['s1_first_venture.ron', 's2_shares_and_bonds.ron', 's3_licences_abroad.ron']);
    expect(nextScenario(1)).toBe(2);
    expect(nextScenario(3)).toBeNull();
  });

  it('finds the dialogs and the end of a scenario in a turn report', () => {
    const report: TurnReport = {
      turn: 7,
      events: [
        { ScenarioDialog: { event: 'offer', text: 'A workshop needs tools.', coins: 0, continent: 0, turn: 7 } },
        { ScenarioDialog: { event: 'dividend', text: 'Dividend paid.', coins: 490, continent: 0, turn: 7 } },
        { ScenarioEnded: { net_profit: 1059, objectives_done: 3, turn: 7 } },
      ],
    };
    expect(scenarioDialogs(report)).toEqual([
      { title: 'News', text: 'A workshop needs tools.' },
      { title: 'News', text: 'Dividend paid.\n\nYou receive 490 coins.' },
    ]);
    expect(scenarioEnd(report)).toEqual({ net_profit: 1059, objectives_done: 3, turn: 7 });
    expect(scenarioEnd({ turn: 1, events: [] })).toBeNull();
  });

  it('shows the week and the weeks left', () => {
    expect(deadlineText(0, 20)).toBe('Week 1 of 20 - 20 weeks left');
    expect(deadlineText(19, 20)).toBe('Week 20 of 20 - last week');
  });

  it('describes a contract with its deadline', () => {
    const c = { id: 'tools', good: 6, quantity: 18, node: 9, reward: 1200, deadline_week: 12, text: '', accepted: false, delivered: false, expired: false };
    expect(contractLine(c, (g) => `good ${g}`, (n) => `node ${n}`)).toBe('18 good 6 to node 9 by week 12 - 1,200 coins');
  });

  it('lists the statement with the mixed line labelled, holdings and net profit as the score', () => {
    const rows = statementRows({ ...statement, holdings: 300 });
    expect(rows.map((r) => r.label)).toEqual([
      'Starting coins',
      'Revenue',
      'Purchases',
      'Travel',
      'Guards and insurance',
      'Cargo lost',
      'Licences, interest and other costs (gains shown as negative)',
      'Ending coins',
      'Shares and bonds held',
      'Net profit (your score)',
    ]);
    expect(rows.at(-1)?.value).toBe('1,059');
    expect(rows.find((r) => r.label === 'Purchases')?.value).toBe('-700');
    expect(statementRows(statement).some((r) => r.label === 'Shares and bonds held')).toBe(false);
  });
});
