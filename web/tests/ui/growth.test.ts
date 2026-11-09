// The growth presentation's logic (what counts as a gain, the counted value over time) and the
// caravans' positions on the map while a week computes.
import { describe, expect, it } from 'vitest';
import { countedValue, gainsOf, growthDuration } from '../../src/ui/growth';
import { caravanPoints, MAX_CARAVANS } from '../../src/map/caravans';
import type { TurnReport } from '../../src/sim/types';

const report = (events: unknown[]): TurnReport => ({ turn: 9, events } as TurnReport);
const name = (g: number): string => `Good ${g}`;

describe('growth presentation', () => {
  it('scales logarithmically with relative gain and stays within 800–2000ms', () => {
    const durations = [0, 1, 10, 100, 1000, 10000, 1000000].map((gain) => growthDuration(gain, 500));
    expect(durations).toEqual([...durations].sort((a, b) => a - b));
    for (const duration of durations) expect(duration).toBeGreaterThanOrEqual(800);
    for (const duration of durations) expect(duration).toBeLessThanOrEqual(2000);
    expect(growthDuration(0, 500)).toBe(800);
    expect(growthDuration(10000, 1)).toBe(2000);
    expect(growthDuration(100, 100)).toBeGreaterThan(growthDuration(100, 1000));
    expect(Number.isFinite(growthDuration(100, 0))).toBe(true);
  });
  it('counts sales, payouts and delivered contracts, not purchases', () => {
    const r = report([
      { GoodsTraded: { market: 1, good: 3, bought: false, requested: 30, filled: 30, price: 12, turn: 9 } },
      { GoodsTraded: { market: 1, good: 4, bought: true, requested: 5, filled: 5, price: 9, turn: 9 } },
      { GoodsTraded: { market: 1, good: 5, bought: false, requested: 5, filled: 0, price: 9, turn: 9 } },
      { ScenarioDialog: { event: 'dividend', text: 'The workshop pays a dividend.', coins: 490, continent: 0, turn: 9 } },
      { ScenarioDialog: { event: 'rumour', text: 'Rumour.', coins: 0, continent: 0, turn: 9 } },
      'Other',
    ]);
    expect(gainsOf(r, name)).toEqual([
      { label: 'Sold 30 Good 3', coins: 360 },
      { label: 'The workshop pays a dividend.', coins: 490 },
    ]);
  });

  it('counts up from the old coins to the new with an ease-out, ending exactly', () => {
    const duration = growthDuration(500, 100);
    expect(countedValue(100, 600, 0, duration)).toBe(100);
    expect(countedValue(100, 600, duration, duration)).toBe(600);
    expect(countedValue(100, 600, duration * 10, duration)).toBe(600);
    const half = countedValue(100, 600, duration / 2, duration);
    expect(half).toBeGreaterThan(350); // ease-out: past the midpoint at half time
    expect(Number.isInteger(half)).toBe(true);
  });
});

describe('caravans on the map', () => {
  const points = new Map([
    [1, { x: 0, y: 0 }],
    [2, { x: 100, y: 0 }],
  ]);
  it('places each caravan between the town it left and where it goes', () => {
    const list = [
      { from: 1, to: 2, arrival_turn: 3 },
      { from: 2, to: 7, arrival_turn: 3 }, // unknown town: not drawn
    ];
    expect(caravanPoints(list, points, 0)).toEqual([{ x: 0, y: 0 }]);
    expect(caravanPoints(list, points, 0.5)).toEqual([{ x: 50, y: 0 }]);
    expect(caravanPoints(list, points, 2)).toEqual([{ x: 100, y: 0 }]);
  });
  it('draws at most a fixed number', () => {
    const many = Array.from({ length: MAX_CARAVANS + 50 }, () => ({ from: 1, to: 2, arrival_turn: 1 }));
    expect(caravanPoints(many, points, 0)).toHaveLength(MAX_CARAVANS);
  });
});
