// Exact production contract: public/assets/README.md.
import manifest from './manifest.json';
export const MANIFEST: Record<string, { path: string; size: string }> = manifest;


/** Grades keep their own names in the UI, but share their base art. */
export function goodAsset(key: string): string {
  const base = key.replace(/@(low|mid|high|best)$/, '');
  const special: Record<string, string> = { g1_0: 'good-wheat', g6_0: 'good-iron_tools', g19_0: 'good-regional-specialty' };
  if (special[base]) return special[base];
  if (/^g(?:[0-9]|1[0-9])_(?:[0-9]|[1-4][0-9])$/.test(base)) return 'good-trade-crate';
  return MANIFEST[`good-${base}`] ? `good-${base}` : 'good-trade-crate';
}
