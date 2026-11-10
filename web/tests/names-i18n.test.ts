import { describe, expect, it } from 'vitest';
import { LANGUAGES } from '../src/i18n/languages';
import { loadLanguage } from '../src/i18n/loaders';
import { createNames } from '../src/data/name-generator';
import { englishNames } from '../src/data/name-data/en';
import snapshot from './fixtures/names-en.json';

describe('localized name generation', () => {
  it('keeps every shipped English place and good byte-identical', () => {
    const names = createNames(englishNames);
    expect(Object.fromEntries(names.places)).toEqual(snapshot.places);
    expect(Object.fromEntries(names.goods)).toEqual(snapshot.goods);
  });
  for (const language of LANGUAGES.filter(language => language.code !== 'en')) {
    it(`${language.code} generates unique nonempty names and excludes its banned output`, async () => {
      const bundle = await loadLanguage(language.code);
      const data = bundle.names;
      const names = createNames(data);
      const places = [...names.places.values()], goods = [...names.goods.values()];
      expect(places).toHaveLength(1580); expect(goods).toHaveLength(1000);
      expect(new Set(places).size).toBe(places.length);
      expect(new Set(goods).size).toBe(goods.length);
      for (const name of [...places, ...goods]) expect(name.trim()).not.toBe('');
      const curated = new Set([...Object.values(data.curatedPlaces), ...Object.values(data.curatedGoods)]);
      expect([...places, ...goods].filter(name => !curated.has(name) && data.banned.test(name))).toEqual([]);
      expect(Object.fromEntries(createNames(data).places)).toEqual(Object.fromEntries(names.places));
      expect(names.placeName('k0n0r0c0')).toBe(data.curatedPlaces.k0n0r0c0);
      expect(names.goodName('g1_0')).toBe(data.curatedGoods.g1_0);
    });
  }
});
