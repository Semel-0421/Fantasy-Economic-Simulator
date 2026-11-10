import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { expect, it } from 'vitest';
import { fontCodepoints, missingGlyphs } from '../scripts/font-coverage.mjs';
import { LANGUAGES } from '../src/i18n/languages';
import { loadLanguage } from '../src/i18n/loaders';
import { en } from '../src/i18n/en';
import { englishNames } from '../src/data/name-data/en';
import { createNames } from '../src/data/name-generator';
import { FONT_PROFILES } from '../src/i18n/font-profiles';

const subsets = ['latin', 'latin-ext', 'cyrillic'];
const pixel = Object.fromEntries(subsets.map(subset => [subset, fontCodepoints(readFileSync(`node_modules/@fontsource/pixelify-sans/files/pixelify-sans-${subset}-400-normal.woff`))]));
const galmuri = fontCodepoints(readFileSync('src/assets/fonts/Galmuri11.woff2'));

it('reads actual WOFF/WOFF2 Unicode cmaps and reports whole-language coverage', async () => {
  expect(pixel.latin.has('A'.codePointAt(0))).toBe(true);
  expect(pixel.latin.has('한'.codePointAt(0))).toBe(false);
  expect(galmuri.has('한'.codePointAt(0))).toBe(true);
  expect(() => fontCodepoints(Buffer.from('not a font'))).toThrow();
  const report = {};
  for (const language of LANGUAGES) {
    const bundle = language.code === 'en' ? { catalogue: en, scenario: {}, names: englishNames } : await loadLanguage(language.code);
    const names = createNames(bundle.names);
    const corpus = [...Object.values(bundle.catalogue), ...Object.values(bundle.scenario), ...names.places.values(), ...names.goods.values(), ...bundle.names.qualifiers, ...bundle.names.landforms, ...bundle.names.starts, ...bundle.names.middles, ...bundle.names.ends].join('\n');
    const pixelPoints = new Set(Object.values(pixel).flatMap(set => [...set]));
    const missingPixel = missingGlyphs(corpus, pixelPoints), missingGalmuri = missingGlyphs(corpus, galmuri);
    const choice = !missingPixel.length ? { family: 'pixelify', subsets: subsets.filter(subset => [...corpus].some(char => pixel[subset].has(char.codePointAt(0)) && (subset === 'latin' || !pixel.latin.has(char.codePointAt(0))))) } : ['ko', 'ja'].includes(language.code) && !missingGalmuri.length ? { family: 'galmuri', subsets: [] } : { family: 'system', subsets: [] };
    report[language.code] = { ...choice, missingPixel, missingGalmuri };
    expect(FONT_PROFILES[language.code], language.code).toBe(choice.family);
    if (choice.family === 'pixelify') expect(missingPixel).toEqual([]);
    if (choice.family === 'galmuri') expect(missingGalmuri).toEqual([]);
    console.info(`${language.code}: ${choice.family}; Pixelify missing ${missingPixel.length}; Galmuri missing ${missingGalmuri.length}`);
  }
  mkdirSync('test-results', { recursive: true });
  writeFileSync('test-results/font-coverage.json', JSON.stringify(report, null, 2));
  for (const file of ['public/licenses/pixelify-sans.txt', 'public/licenses/galmuri.txt']) expect(readFileSync(file, 'utf8')).toMatch(/SIL OPEN FONT LICENSE/i);
});
