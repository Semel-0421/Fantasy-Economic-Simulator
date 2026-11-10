import { language } from './index';
import type { LanguageCode } from './languages';
import { scenarioCatalogues } from './scenario/catalogues';

const escape = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const templates = Object.fromEntries(Object.entries(scenarioCatalogues).map(([code, catalogue]) => [code,
  Object.entries(catalogue).filter(([source]) => /\{\w+\}/.test(source)).map(([source, translated]) => {
    const names: string[] = [];
    const pattern = source.split(/(\{\w+\})/).map(part => {
      if (!/^\{\w+\}$/.test(part)) return escape(part);
      names.push(part.slice(1, -1));
      return '([^\\n]+?)';
    }).join('');
    return { pattern: new RegExp(`^${pattern}$`), names, translated };
  }),
]));

/** Translate before displayText replaces world keys. The engine has already filled contract tokens. */
export function translateScenario(text: string, code: LanguageCode = language.code): string {
  if (code === 'en') return text;
  const catalogue = scenarioCatalogues[code];
  if (Object.hasOwn(catalogue, text)) return catalogue[text]!;
  for (const template of templates[code] ?? []) {
    const match = template.pattern.exec(text);
    if (!match) continue;
    const values = Object.fromEntries(template.names.map((name, index) => [name, match[index + 1]!]));
    return template.translated.replace(/\{(\w+)\}/g, (token, name: string) => values[name] ?? token);
  }
  return text;
}
