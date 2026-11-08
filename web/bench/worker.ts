// Worker side: loads the wasm and the full-scale CSVs, builds the world and plays turns 0-53.
import init, { WasmSim } from './pkg/fes_wasm.js';

export interface BenchResult {
  buildMs: number;
  /** Wasm linear memory high-water mark, including allocator slack (it cannot shrink). */
  peakWasmBytes: number;
  turnsMs: number[];
  averageMs: number;
  worstMs: number;
  worstTurn: number;
  hashAfter11: string;
  expectedHash11: string;
  hashMatches: boolean;
  /** Per-system ms for the worst turn, when the wasm was built with PROFILE=1. */
  worstTurnSystems?: [string, number][];
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
  const wasm = await init();
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
  const profiled = 'step_profiled' in sim;
  const systems: [string, number][][] = [];
  for (let turn = 0; turn < TURNS; turn++) {
    const start = performance.now();
    if (profiled) {
      const times = (sim as unknown as { step_profiled(): [string, number][] }).step_profiled();
      systems.push(times.filter(([, ms]) => ms >= 1).sort((a, b) => b[1] - a[1]));
    } else sim.step();
    const ms = performance.now() - start;
    turnsMs.push(ms);
    if (sim.turn() === 11) hashAfter11 = sim.world_hash();
    post({ kind: 'progress', turn, ms });
  }
  const worstMs = Math.max(...turnsMs);
  const expectedHash11 = turnsJson.turns[11].world_hash;
  return {
    buildMs,
    peakWasmBytes: wasm.memory.buffer.byteLength,
    turnsMs,
    averageMs: turnsMs.reduce((a, b) => a + b, 0) / TURNS,
    worstMs,
    worstTurn: turnsMs.indexOf(worstMs),
    hashAfter11,
    expectedHash11,
    hashMatches: hashAfter11 === expectedHash11,
    worstTurnSystems: profiled ? systems[turnsMs.indexOf(worstMs)] : undefined,
  };
}

run().then(
  (result) => post({ kind: 'done', result }),
  (e: unknown) => post({ kind: 'error', message: String(e) }),
);
