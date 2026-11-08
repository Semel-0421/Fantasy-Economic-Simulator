// Worker side: loads the wasm and the full-scale CSVs, builds the world and plays turns 0-53.
import init, { WasmSim } from './pkg/fes_wasm.js';

export interface BenchResult {
  buildMs: number;
  turnsMs: number[];
  averageMs: number;
  worstMs: number;
  worstTurn: number;
  hashAfter11: string;
  expectedHash11: string;
  hashMatches: boolean;
}

export type BenchMessage =
  | { kind: 'progress'; turn: number; ms: number }
  | { kind: 'done'; result: BenchResult }
  | { kind: 'error'; message: string };

const FILES = [
  'config.csv',
  'events.csv',
  'facilities.csv',
  'goods.csv',
  'map_nodes.csv',
  'map_routes.csv',
  'market_profiles.csv',
  'recipes.csv',
];
const TURNS = 54;

const post = (m: BenchMessage): void => postMessage(m);

async function text(name: string): Promise<string> {
  const res = await fetch(new URL(`/${name}`, self.location.origin));
  if (!res.ok) throw new Error(`${name}: HTTP ${res.status}`);
  return res.text();
}

async function run(): Promise<BenchResult> {
  await init();
  const files: Record<string, string> = {};
  for (const name of FILES) files[name] = await text(name);
  const turnsJson = JSON.parse(await text('turns.json')) as {
    turns: { turn: number; world_hash: string }[];
  };
  const t0 = performance.now();
  const sim = new WasmSim(files, 1n);
  const buildMs = performance.now() - t0;
  const turnsMs: number[] = [];
  let hashAfter11 = '';
  for (let turn = 0; turn < TURNS; turn++) {
    const start = performance.now();
    sim.step();
    const ms = performance.now() - start;
    turnsMs.push(ms);
    if (sim.turn() === 11) hashAfter11 = sim.world_hash();
    post({ kind: 'progress', turn, ms });
  }
  const worstMs = Math.max(...turnsMs);
  const expectedHash11 = turnsJson.turns[11].world_hash;
  return {
    buildMs,
    turnsMs,
    averageMs: turnsMs.reduce((a, b) => a + b, 0) / TURNS,
    worstMs,
    worstTurn: turnsMs.indexOf(worstMs),
    hashAfter11,
    expectedHash11,
    hashMatches: hashAfter11 === expectedHash11,
  };
}

run().then(
  (result) => post({ kind: 'done', result }),
  (e: unknown) => post({ kind: 'error', message: String(e) }),
);
