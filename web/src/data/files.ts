// The game data files the engine reads (`{name: text}`), served from `data/` next to index.html.
export const REQUIRED_FILES = [
  'config.csv',
  'events.csv',
  'facilities.csv',
  'goods.csv',
  'map_nodes.csv',
  'map_routes.csv',
  'market_profiles.csv',
  'recipes.csv',
] as const;

/** Not every data set ships these; a 404 skips the file. */
export const OPTIONAL_FILES = ['specialties.csv'] as const;

export type GameFiles = Record<string, string>;

/** Fetches the data files under `baseUrl` (a folder URL ending in `/`); `optional` may be absent. */
export async function loadGameFiles(baseUrl: string, optional: readonly string[] = OPTIONAL_FILES): Promise<GameFiles> {
  const files: GameFiles = {};
  const get = async (name: string, optional: boolean): Promise<void> => {
    const res = await fetch(new URL(name, baseUrl));
    if (res.status === 404 && optional) return;
    if (!res.ok) throw new Error(`Could not load ${name}: HTTP ${res.status}`);
    files[name] = await res.text();
  };
  await Promise.all([
    ...REQUIRED_FILES.map((n) => get(n, false)),
    ...optional.map((n) => get(n, true)),
  ]);
  return files;
}

/** The data folder of the page that is running (relative, so any host folder works). */
export function defaultDataUrl(): string {
  return new URL('data/', document.baseURI).href;
}
