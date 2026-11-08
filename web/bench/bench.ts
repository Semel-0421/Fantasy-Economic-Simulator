// Page side: starts the Worker, prints its progress and the result; the result is also left on
// `window.benchResult` for the Playwright runner.
import type { BenchResult, BenchMessage } from './worker';

const out = document.getElementById('out') as HTMLPreElement;
const worker = new Worker(new URL('./worker.ts', import.meta.url), { type: 'module' });

function format(r: BenchResult): string {
  const s = (ms: number): string => `${(ms / 1000).toFixed(3)} s`;
  return [
    `build world: ${s(r.buildMs)}`,
    `turns 0-53: average ${s(r.averageMs)}, worst ${s(r.worstMs)} (turn ${r.worstTurn})`,
    `turn 52 (new year): ${s(r.turnsMs[52])}`,
    `world hash after turn 11: ${r.hashAfter11} (C# ${r.expectedHash11}) ${r.hashMatches ? 'MATCH' : 'MISMATCH'}`,
    `user agent: ${navigator.userAgent}`,
  ].join('\n');
}

worker.onmessage = (e: MessageEvent<BenchMessage>): void => {
  const msg = e.data;
  if (msg.kind === 'progress') {
    out.textContent = `turn ${msg.turn}: ${(msg.ms / 1000).toFixed(3)} s`;
  } else if (msg.kind === 'error') {
    out.textContent = `error: ${msg.message}`;
    (window as unknown as { benchResult: unknown }).benchResult = { error: msg.message };
  } else {
    out.textContent = format(msg.result);
    (window as unknown as { benchResult: unknown }).benchResult = msg.result;
  }
};
