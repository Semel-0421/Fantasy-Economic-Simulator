import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { licenceChoices } from '../src/ui/licence_goods';
import { heldLicenceText } from '../src/ui/finance';
import type { Views } from '../src/sim/types';

const csv = readFileSync('../engine/fixtures/fullscale/goods.csv', 'utf8');
const keys = csv.trim().split(/\r?\n/).slice(1).map((line) => line.split(',')[0]!);
const goods: Views['goods'] = keys.flatMap((key, base) => ['low', 'mid', 'high', 'best'].map((grade, index) => ({ id: base * 4 + index, key: `${key}@${grade}`, base_price: 10 })));
const scenario = readFileSync('../data/scenarios/s3_licences_abroad.ron', 'utf8');

describe('licence goods', () => {
  it('offers at most 50 base goods initially and puts scenario 3 goods first', () => {
    const choices = licenceChoices(goods, csv, scenario, false);
    expect(choices.length).toBeLessThanOrEqual(50);
    expect(choices[0]!.key).toBe('g19_0');
    expect(choices[0]!.id).toBe(goods.find((good) => good.key === 'g19_0@mid')!.id);
    const all = licenceChoices(goods, csv, scenario, true);
    expect(all).toHaveLength(1000);
    expect(new Set(all.map((choice) => choice.key)).size).toBe(1000);
    expect(all.every((choice) => !choice.name.includes('@') && !choice.name.includes('(mid)'))).toBe(true);
  });
  it('shows required CSV goods before the remaining sorted base goods', () => {
    const metadata = 'key,requires_license\ng0_0,1\ng1_0,0';
    expect(licenceChoices(goods, metadata, '', false).map((choice) => choice.key)).toEqual(['g0_0']);
    const all = licenceChoices(goods, metadata, '', true);
    expect(all[0]!.key).toBe('g0_0');
    expect(all.slice(1).map((choice) => choice.name)).toEqual(all.slice(1).map((choice) => choice.name).sort((a, b) => a.localeCompare(b, 'en')));
  });
  it('renders held base-good identity, nation and expiry, plus trade scope', () => {
    const name = (base: number): string => base === 950 ? 'G19 0' : `Base ${base}`;
    expect(heldLicenceText({ base_good: 950, nation: 7, expires_turn: 55 }, name, () => 'East nation', (turn) => `Week ${turn - 3}`))
      .toBe('Licence to sell G19 0 · East nation · expires week 52');
    expect(heldLicenceText({ base_good: -1, nation: 7, expires_turn: 55 }, name, () => 'East nation'))
      .toContain('International trade licence');
  });
});
