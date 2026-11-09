import { describe, expect, it } from 'vitest';
import { calendarDate, dateOf, describeEvent, playTurn, scenarioDate } from '../../src/ui/turn';
import type { TurnReport } from '../../src/sim/types';

const report: TurnReport = { turn: 3, events: [] };
const bridge = { next: async () => report, save: async () => new Uint8Array([1, 2]) };

describe('turn flow', () => {
  it('maps a turn count to year and week', () => {
    expect(dateOf(0)).toEqual({ year: 1, week: 1 });
    expect(dateOf(51)).toEqual({ year: 1, week: 52 });
    expect(dateOf(52)).toEqual({ year: 2, week: 1 });
  });

  it('labels a turn by the calendar, or by the scenario week counted from its start', () => {
    expect(calendarDate(4)).toBe('Year 1, Week 5');
    const week = scenarioDate(4);
    expect(week(4)).toBe('Week 1');
    expect(week(23)).toBe('Week 20');
  });

  it('describes unit and struct events', () => {
    expect(describeEvent('SomethingHappened' as never)).toEqual({ title: 'Something Happened', detail: '' });
    const line = describeEvent({ AuctionClosed: { facility: 1, won_by_player: true, price: 900, turn: 4 } });
    expect(line.title).toBe('Auction Closed');
    expect(line.detail).toContain('price 900');
    expect(line.detail).toContain('won by player true');
  });

  it('resolves event good and place ids for the player log', () => {
    const line = describeEvent({ GoodsTraded: { good: 4, market: 9, requested: 2, filled: 2, price: 30, bought: true, turn: 1 } },
      (field, value) => field === 'good' ? 'Golden wheat (mid)' : field === 'market' ? 'Ashford' : String(value));
    expect(line.detail).toContain('good Golden wheat (mid)');
    expect(line.detail).toContain('market Ashford');
    expect(line.detail).toContain('filled 2');
  });

  it('autosaves after a free-mode turn', async () => {
    const writes: unknown[][] = [];
    const saves = { write: async (...a: unknown[]) => void writes.push(a) };
    const out = await playTurn({ bridge, saves: saves as never, mode: 'free' });
    expect(out.report.turn).toBe(3);
    expect(writes).toEqual([['auto', 'free', new Uint8Array([1, 2]), 3]]);
  });

  it('does not save a scenario', async () => {
    const saves = { write: async () => { throw new Error('should not be called'); } };
    const out = await playTurn({ bridge, saves, mode: 'scenario' });
    expect(out.saveError).toBeUndefined();
  });

  it('keeps the turn when the autosave fails', async () => {
    const saves = { write: async () => { throw new Error('disk full'); } };
    const out = await playTurn({ bridge, saves, mode: 'free' });
    expect(out.report.turn).toBe(3);
    expect(out.saveError).toBe('disk full');
  });
});
