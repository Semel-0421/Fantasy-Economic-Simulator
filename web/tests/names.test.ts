import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { goodName, placeName, displayText } from '../src/data/names';
import { goodLabel } from '../src/ui/market';

const keys = (file: string): string[] => readFileSync(`public/data/fullscale/${file}.csv`, 'utf8').trim().split(/\r?\n/).slice(1).map((line) => line.split(',')[0]!);

describe('display names', () => {
  it('uses the curated scenario names and keeps grades', () => {
    expect(placeName('k0n0r0c0v1')).toBe('Millbrook');
    expect(placeName('k0n0r0c1')).toBe('Thornhold');
    expect(placeName('k0n0r1c4')).toBe('Ravensgate');
    expect(goodName('g17_5')).toBe('Copper fittings');
    expect(goodLabel('g1_0@high')).toBe('Golden wheat (high)');
  });
  it('names all 1,580 places uniquely within their level and all 1,000 goods uniquely', () => {
    const places = keys('map_nodes'), goods = keys('goods');
    expect(places).toHaveLength(1580);
    expect(goods).toHaveLength(1000);
    expect(new Set(places.map(placeName)).size).toBe(places.length);
    for (const level of [1, 2, 3, 4, 5]) {
      const group = places.filter((key) => (key.match(/[knrcv]/g) ?? []).length === level);
      expect(new Set(group.map(placeName)).size).toBe(group.length);
    }
    expect(new Set(goods.map(goodName)).size).toBe(goods.length);
    expect(places.map(placeName).some((name) => /\d|_/.test(name))).toBe(false);
    expect(goods.filter((key) => !['g1_0', 'g6_0', 'g17_5', 'g19_0'].includes(key)).map(goodName).every((name) => /^(Highland|Lowland|Coastal|River|Northern|Southern|Eastern|Western|Hill|Marsh|Valley|Old-town) [a-z ]+$/.test(name))).toBe(true);
    expect(places.filter((key) => /r\d+$/.test(key) && key !== 'k0n0r1').map(placeName).every((name) => / (Vale|Fen|Downs|Reach|Moor|Hollow|March)$/.test(name))).toBe(true);
  });
  it('excludes the Planner banned substrings from generated names', () => {
    const banned = /fag|nig|cum|tit|rap|nazi|kkk|shit|fuck|dick|cock|slut|whor|ass/i;
    const curated = new Set(['k0n0r0c4']); // Saltmarsh is explicitly curated.
    expect(keys('map_nodes').filter((key) => !curated.has(key)).map(placeName).filter((name) => banned.test(name))).toEqual([]);
    expect(keys('goods').map(goodName).filter((name) => banned.test(name))).toEqual([]);
  });
  it('uses the CSV category for generated goods nouns', () => {
    const nouns = [
      'turnips|cabbages|onions|leeks|carrots|beets', 'wheat|barley|oats|rye|millet|spelt',
      'apples|pears|plums|cherries|figs|quinces', 'herring|cod|eels|trout|salmon|smoked fish',
      'mutton|pork|beef|sausages|salted ham|venison', 'cheese|butter|cream|curds|goat cheese|eggs',
      'tools|axes|sickles|hammers|saws|ploughs', 'oak timber|pine planks|ash poles|beams|shingles|charcoal',
      'granite|slate|limestone|marble|bricks|flagstones', 'iron ore|copper ore|tin ore|lead ore|silver ore|coal',
      'iron bars|copper ingots|tin ingots|bronze bars|steel rods|lead sheets', 'linen|wool cloth|canvas|felt|yarn|cotton',
      'hides|leather|saddles|boots|belts|furs', 'pottery|jugs|bowls|tiles|glassware|lamps',
      'ale|cider|mead|wine|perry|spirits', 'pepper|saffron|cloves|salt|mustard|herbs',
      'madder|woad|indigo|ochre|dye cakes|ink', 'copper fittings|hinges|nails|buckles|locks|chains',
      'chairs|chests|tables|barrels|carts|wagons', 'sunstone|amber|silk|pearls|perfume|jewellery',
    ];
    for (const row of readFileSync('public/data/fullscale/goods.csv', 'utf8').trim().split(/\r?\n/).slice(1)) {
      const [key, category] = row.split(',');
      const index = Number(category!.replace('cat', ''));
      expect(key!.startsWith(`g${index}_`)).toBe(true);
      if (['g1_0', 'g6_0', 'g17_5', 'g19_0'].includes(key!)) continue;
      expect(goodName(key!).replace(/^[^ ]+ /, '')).toMatch(new RegExp(`^(${nouns[index]})$`));
    }
  });
  it('replaces whole keys in prose, including grade suffixes, without touching larger words', () => {
    expect(displayText('Buy g19_0 in k0n1; g1_0@high. xk0n1 g19_0_extra.')).toBe('Buy Sunstone in Halvesh; Golden wheat (high). xk0n1 g19_0_extra.');
  });
  it('covers every literal mentioned by the three scenarios', () => {
    for (const file of ['s1_first_venture', 's2_shares_and_bonds', 's3_licences_abroad']) {
      const text = readFileSync(`../data/scenarios/${file}.ron`, 'utf8');
      expect(displayText(text)).not.toMatch(/\bk\d+(?:n\d+)?|\bg\d+_\d+\b/);
    }
  });
  it('keeps a stable twenty-name sample independent of call order', () => {
    const sample = [...keys('map_nodes').slice(0, 10), ...keys('goods').slice(0, 10)];
    const names = sample.map((key) => key.startsWith('g') ? goodName(key) : placeName(key));
    expect([...sample].reverse().map((key) => key.startsWith('g') ? goodName(key) : placeName(key)).reverse()).toEqual(names);
    expect(names).toMatchSnapshot();
  });
});
