// The teaching scenarios (spec 5): their data files (data/scenarios, shipped as public/data/scenarios)
// and the full-scale world they are played on (public/data/fullscale).
export interface ScenarioEntry {
  /** 1-based number shown in the menu. */
  n: number;
  title: string;
  file: string;
  pitch: string;
  targetProfit: number;
}

export const SCENARIOS: readonly ScenarioEntry[] = [
  { n: 1, title: 'First Venture', file: 's1_first_venture.ron', pitch: 'Learn to trade: buy cheap, sell dear, deliver a contract.', targetProfit: 1000 },
  { n: 2, title: 'Shares and Bonds', file: 's2_shares_and_bonds.ron', pitch: 'Grow coins through company shares and bonds.', targetProfit: 2500 },
  { n: 3, title: 'Licences and Trade Abroad', file: 's3_licences_abroad.ron', pitch: 'Licences and trade between nations.', targetProfit: 1000 },
];

/** The scenario after `n`, or null after the last. */
export function nextScenario(n: number): number | null {
  return SCENARIOS.some((s) => s.n === n + 1) ? n + 1 : null;
}

/** Existing scenario node literals identify teaching destinations without changing the Worker API. */
export function scenarioNodeKeys(text: string): string[] {
  return [...new Set([...text.matchAll(/"(k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?)"/g)].map((match) => match[1]!))];
}

/** The folder of the full-scale world the scenarios run on. */
export function scenarioDataUrl(): string {
  return new URL('data/fullscale/', document.baseURI).href;
}

/** Fetches scenario `n`'s file (RON text). */
export async function loadScenarioText(n: number): Promise<string> {
  const entry = SCENARIOS.find((s) => s.n === n);
  if (!entry) throw new Error(`There is no scenario ${n}.`);
  const res = await fetch(new URL(`data/scenarios/${entry.file}`, document.baseURI));
  if (!res.ok) throw new Error(`Could not load ${entry.file}: HTTP ${res.status}`);
  return res.text();
}
