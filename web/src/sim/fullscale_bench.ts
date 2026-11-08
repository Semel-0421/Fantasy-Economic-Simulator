export interface BenchSimulation {
  step(): void;
  turn(): number;
  world_hash(): string;
}

export interface FullscaleBenchResult {
  turnsMs: number[];
  averageMs: number;
  worstMs: number;
  worstTurn: number;
  hashAfter11: string;
  expectedHash11: string;
  hashMatches: boolean;
}

export const FULLSCALE_TURNS = 54;

export function runFullscaleBench(
  sim: BenchSimulation,
  expectedHash11: string,
  step: () => void = () => sim.step(),
  onTurn?: (turn: number, ms: number) => void,
): FullscaleBenchResult {
  const turnsMs: number[] = [];
  let hashAfter11 = '';
  for (let turn = 0; turn < FULLSCALE_TURNS; turn++) {
    const started = performance.now();
    step();
    const ms = performance.now() - started;
    turnsMs.push(ms);
    if (sim.turn() === 11) hashAfter11 = sim.world_hash();
    onTurn?.(turn, ms);
  }
  const worstMs = Math.max(...turnsMs);
  return {
    turnsMs,
    averageMs: turnsMs.reduce((sum, ms) => sum + ms, 0) / FULLSCALE_TURNS,
    worstMs,
    worstTurn: turnsMs.indexOf(worstMs),
    hashAfter11,
    expectedHash11,
    hashMatches: hashAfter11 === expectedHash11,
  };
}
