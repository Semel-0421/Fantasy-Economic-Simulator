// Worker side: loads the wasm and the full-scale CSVs, builds the world and plays turns 0-53.
import init, { WasmSim } from './pkg/fes_wasm.js';
import { runFullscaleBench, type FullscaleBenchResult } from '../src/sim/fullscale_bench';

export interface BenchResult extends FullscaleBenchResult {
  buildMs: number;
  /** Wasm linear memory high-water mark, including allocator slack (it cannot shrink). */
  peakWasmBytes: number;
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
  const profiled = 'step_profiled' in sim;
  const systems: [string, number][][] = [];
  const expectedHash11 = turnsJson.turns[11].world_hash;
  const result = runFullscaleBench(
    sim,
    expectedHash11,
    () => {
      if (profiled) {
        const times = (sim as unknown as { step_profiled(): [string, number][] }).step_profiled();
        systems.push(times.filter(([, ms]) => ms >= 1).sort((a, b) => b[1] - a[1]));
      } else {
        sim.step();
      }
    },
    (turn, ms) => post({ kind: 'progress', turn, ms }),
  );
  return {
    buildMs,
    peakWasmBytes: wasm.memory.buffer.byteLength,
    ...result,
    worstTurnSystems: profiled ? systems[result.worstTurn] : undefined,
  };
}

run().then(
  (result) => post({ kind: 'done', result }),
  (e: unknown) => post({ kind: 'error', message: String(e) }),
);
