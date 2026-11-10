import { en } from './en';
import { catalogues } from './catalogues';
import { languageOf } from './languages';
import type { MessageId, Params } from './types';

function readLanguage(): string | null {
  try { return localStorage.getItem('fes-lang'); } catch { return null; }
}

export const language = languageOf(readLanguage());
const catalogue = catalogues[language.code];
const numbers = new Intl.NumberFormat(language.locale, { numberingSystem: 'latn' });
const ungrouped = new Intl.NumberFormat(language.locale, { numberingSystem: 'latn', useGrouping: false, maximumFractionDigits: 20 });

export const formatNumber = (value: number): string => numbers.format(value);

export function t(id: MessageId, params: Params = {}): string {
  return (catalogue[id] ?? en[id]).replace(/\{(\w+)\}/g, (token: string, key: string) => {
    const value = params[key];
    return value === undefined ? token : typeof value === 'number' ? ungrouped.format(value) : value;
  });
}

export function applyLanguage(root: { lang: string; dir: string } = document.documentElement): void {
  root.lang = language.code;
  root.dir = language.dir;
}

export function chooseLanguage(code: string): void {
  try { localStorage.setItem('fes-lang', languageOf(code).code); } catch { /* Storage may be blocked. */ }
  location.reload();
}
