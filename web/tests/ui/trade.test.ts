import { describe, expect, it } from 'vitest';
import { coinsOf, estimate, goodLabel, previewText } from '../../src/ui/market';
import { cartRows } from '../../src/ui/cargo';
import { purseRows } from '../../src/ui/purse';
import { rejection, rejectionsIn } from '../../src/ui/errors';

const quote = { good: 4, price: 10 * 65536, stock: 7, offer: 9 };

describe('trading helpers', () => {
  it('reads Fx prices as coins and names goods', () => {
    expect(coinsOf(327680)).toBe(5);
    expect(goodLabel('wheat@mid')).toBe('Wheat (mid)');
    expect(goodLabel('fine_cloth@best')).toBe('Fine cloth (best)');
  });

  it('estimates a buy at the posted price, capped by stock', () => {
    expect(estimate('buy', quote, 3, 0)).toEqual({ units: 3, unit: 10, total: 30 });
    expect(estimate('buy', quote, 50, 0)).toEqual({ units: 7, unit: 10, total: 70 });
  });

  it('estimates a sale at the trader offer, capped by the cart', () => {
    expect(estimate('sell', quote, 5, 2)).toEqual({ units: 2, unit: 9, total: 18 });
  });

  it('words the preview, saying when the order is cut down', () => {
    expect(previewText('buy', 'Wheat (mid)', 3, { units: 3, unit: 10, total: 30 })).toBe('Buy 3 Wheat (mid) at 10 each: 30 coins.');
    expect(previewText('sell', 'Wheat (mid)', 5, { units: 2, unit: 9, total: 18 })).toBe(
      'Sell 2 of 5 Wheat (mid) at 9 each: 18 coins (only 2 can be filled now).',
    );
  });

  it('passes the simulation message through unchanged', () => {
    expect(rejection({ result: 'Error', message: 'Cannot trade while travelling.' })).toBe('Cannot trade while travelling.');
    expect(rejection({ result: 'Ok' })).toBeNull();
    const report = {
      turn: 2,
      events: [{ CommandRejected: { command: null, reason: 'The cart holds none of this good.', turn: 2 } }],
    };
    expect(rejectionsIn(report as never)).toEqual(['The cart holds none of this good.']);
  });

  it('lists the cart and the purse', () => {
    const names = ['Wheat (low)', 'Wheat (mid)'];
    expect(cartRows([[1, 4], [0, 0]], (g) => names[g]!)).toEqual([{ good: 1, name: 'Wheat (mid)', quantity: 4 }]);
    const rows = purseRows({ coins: [100, 5], local_continent: 1, deposits: [0, 20], loans: [] } as never, ['Aurelia', 'Borealis']);
    expect(rows[0]).toEqual({ label: 'Borealis coins (here)', value: 5 });
    expect(rows).toContainEqual({ label: 'Aurelia coins', value: 100 });
    expect(rows).toContainEqual({ label: 'Bank deposits', value: 20 });
  });
});
