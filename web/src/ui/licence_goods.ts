import type { Views } from '../sim/types';
import { goodLabel } from './market';

export interface LicenceGood { id: number; base: number; key: string; name: string }

/** Reuse the already-loaded CSV/RON. Commands retain a real graded id; options show only the base. */
export function licenceChoices(goods: Views['goods'], csv: string, scenario: string, all: boolean): LicenceGood[] {
  const lines = csv.split(/\r?\n/).filter((line) => line.trim() && !line.trimStart().startsWith('#'));
  const header = lines.shift()?.split(',') ?? [];
  const keyColumn = header.indexOf('key'), requiredColumn = header.indexOf('requires_license');
  const required = new Set(lines.filter((line) => line.split(',')[requiredColumn]?.trim() === '1').map((line) => line.split(',')[keyColumn]!.trim()));
  const licensed = /licensed:\s*\[([\s\S]*?)\]/.exec(scenario)?.[1] ?? '';
  const priority = new Set([...licensed.matchAll(/good:\s*"([^"]+)"/g)].map((match) => match[1]!));
  const bases = new Map<string, LicenceGood>();
  for (const good of goods) {
    const key = good.key.split('@')[0]!;
    if (!bases.has(key) || good.key.endsWith('@mid')) bases.set(key, { id: good.id, base: Math.floor(good.id / 4), key, name: goodLabel(key) });
  }
  const rank = (key: string): number => priority.has(key) ? 0 : required.has(key) ? 1 : 2;
  return [...bases.values()].filter((good) => all || rank(good.key) < 2)
    .sort((a, b) => rank(a.key) - rank(b.key) || a.name.localeCompare(b.name, 'en'));
}
