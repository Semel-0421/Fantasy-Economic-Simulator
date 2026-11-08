// The end of a week: the results screen opens after the last week even when the growth
// presentation or a dialog fails.
import { describe, expect, it } from 'vitest';
import { finishWeek } from '../../src/ui/week_end';

describe('finishWeek', () => {
  const run = async (fail: 'present' | 'dialogs' | null, ended: boolean) => {
    const log: string[] = [];
    await finishWeek({
      ended,
      present: async () => {
        log.push('present');
        if (fail === 'present') throw new Error('boom');
      },
      dialogs: async () => {
        log.push('dialogs');
        if (fail === 'dialogs') throw new Error('boom');
      },
      results: async () => {
        log.push('results');
      },
      onError: (e) => log.push(`error ${(e as Error).message}`),
    });
    return log;
  };

  it('shows the results after the last week even when the presentation fails', async () => {
    expect(await run('present', true)).toEqual(['present', 'error boom', 'results']);
    expect(await run('dialogs', true)).toEqual(['present', 'dialogs', 'error boom', 'results']);
  });

  it('runs everything in order and shows results only at the end', async () => {
    expect(await run(null, true)).toEqual(['present', 'dialogs', 'results']);
    expect(await run(null, false)).toEqual(['present', 'dialogs']);
  });
});
