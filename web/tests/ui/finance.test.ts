// Words of the exchange, bonds and licence panels.
import { describe, expect, it } from 'vitest';
import { bondText, licenceName, roundBond, shareText } from '../../src/ui/finance';

describe('finance panels', () => {
  it('words a share order at today\'s price', () => {
    const row = { facility: 130, name: 'tool_workshop', node: 4, price: 85, held: 0, room: 49 };
    expect(shareText('buy', row, 49, 'Tc')).toBe("Buy 49 shares of Tool workshop in Tc at 85 each (today's price): 4,165 coins.");
    expect(shareText('sell', row, 2, 'Tc')).toBe("Sell 2 shares of Tool workshop in Tc at 85 each (today's price): 170 coins.");
  });
  it('rounds a bond purchase down to whole hundreds', () => {
    expect(roundBond(10057)).toBe(10000);
    expect(roundBond(99)).toBe(0);
    expect(bondText(3000)).toBe("Buy 3,000 coins of this nation's bonds at face.");
  });
  it('names goods licences and the two trade licences', () => {
    const good = (g: number): string => `Good ${g}`;
    expect(licenceName(-1, good)).toBe('International trade licence');
    expect(licenceName(-2, good)).toBe('Intercontinental trade licence');
    expect(licenceName(4, good)).toBe('Licence to sell Good 4');
  });
});
