import { afterEach, describe, expect, it, vi } from 'vitest';
import { guideDone, markGuideDone, resetGuide, GUIDE_STEPS } from '../src/ui/guide';

afterEach(() => vi.unstubAllGlobals());
describe('first-run guide', () => {
  it('shows once, records completion/skip, and can be requested again', () => {
    const values = new Map<string, string>();
    vi.stubGlobal('localStorage', { getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value), removeItem: (key: string) => values.delete(key) });
    expect(guideDone()).toBe(false);
    markGuideDone();
    expect(values.get('fes-guide-s1-done')).toBe('1');
    expect(guideDone()).toBe(true);
    resetGuide();
    expect(guideDone()).toBe(false);
    expect(GUIDE_STEPS.map((step) => step.title)).toEqual(['Your purse', 'The market', 'Choose where to sell', 'End your week']);
  });
  it('blocked storage leaves the guide available without throwing', () => {
    const blocked = (): never => { throw new Error('Storage blocked'); };
    vi.stubGlobal('localStorage', { getItem: blocked, setItem: blocked, removeItem: blocked });
    expect(guideDone()).toBe(false);
    expect(() => markGuideDone()).not.toThrow();
    expect(() => resetGuide()).not.toThrow();
  });
});
