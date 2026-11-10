import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { SCENARIOS } from '../src/data/scenarios';
import { scenarioCatalogues } from '../src/i18n/scenario/catalogues';
import { translateScenario } from '../src/i18n/scenario';
import { createScenarioTranslator } from '../src/i18n/scenario-translator';
const korean = createScenarioTranslator(scenarioCatalogues.ko);

const quoted = '"(?:\\\\.|[^"\\\\])*"';
function displayedStrings(source: string): string[] {
  const withoutComments = source.replace(new RegExp(`${quoted}|//[^\\n]*`, 'g'), token => token.startsWith('//') ? '' : token);
  const pattern = new RegExp(`(?:\\b(?:title|intro|text)\\s*:\\s*|\\bDialog\\(\\s*)(${quoted})`, 'g');
  return [...withoutComments.matchAll(pattern)].map(match => JSON.parse(match[1]!.replace(/\\'/g, "'")) as string);
}
const sources = [...new Set([...SCENARIOS.flatMap(s => [s.title, s.pitch]), ...SCENARIOS.flatMap(s => displayedStrings(readFileSync(`../data/scenarios/${s.file}`, 'utf8')))])];
const tokens = (text: string, pattern: RegExp): string[] => [...text.matchAll(pattern)].map(m => m[0]).sort();

describe('scenario translations', () => {
  it('parses all displayed RON fields and detects escaped text correctly', () => {
    expect(displayedStrings('// Dialog("ignored")\n(title: "Title", id: "hidden", intro: "Line\\nnext", text: "Offer", effects: [Dialog("News")])'))
      .toEqual(['Title', 'Line\nnext', 'Offer', 'News']);
    expect(sources.length).toBeGreaterThan(25);
  });
  for (const [code, catalogue] of Object.entries(scenarioCatalogues)) {
    it(`${code} covers every RON/menu string with matching placeholders and world keys`, () => {
      for (const source of sources) {
        expect(Object.hasOwn(catalogue, source), `${code}: ${source}`).toBe(true);
        const translated = catalogue[source]!;
        expect(translated.trim(), `${code}: ${source}`).not.toBe('');
        expect(tokens(translated, /\{\w+\}/g), source).toEqual(tokens(source, /\{\w+\}/g));
        expect(tokens(translated, /\b(?:k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?|g\d+_\d+)\b/g), source)
          .toEqual(tokens(source, /\b(?:k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?|g\d+_\d+)\b/g));
      }
    });
  }
  it('keeps English/unknown text unchanged and translates before display-name substitution', () => {
    expect(translateScenario('First Venture')).toBe('First Venture');
    expect(korean('A runtime error')).toBe('A runtime error');
    expect(korean('First Venture')).toBe('첫 장사');
    const offer = sources.find(s => s.includes('{qty}'))!;
    const values: Record<string, string> = { qty: '18', good: 'g6_0', dest: 'k0n0r0c1', deadline: '12', reward: '1500' };
    const fill = (s: string): string => s.replace(/\{(\w+)\}/g, (_, key: string) => values[key]!);
    expect(korean(fill(offer))).toBe(fill(scenarioCatalogues.ko[offer]!));
    expect(korean('Delivery complete. Promised 1500 coins; you received 0 coins.')).toContain('0');
  });
});
