// Turn flow (spec 5.2a): the player acts, presses Next week, the Worker moves the NPCs, and the
// report's events come back. Pure logic here; the DOM lives in hud.ts and main.ts.
import type { SaveMode, SaveStore } from '../save/store';
import type { SimEventRecord, TurnReport } from '../sim/types';

export const WEEKS_PER_YEAR = 52;

/** The calendar position of a turn count: turn 0 is Year 1, Week 1. */
export function dateOf(turn: number): { year: number; week: number } {
  return { year: Math.floor(turn / WEEKS_PER_YEAR) + 1, week: (turn % WEEKS_PER_YEAR) + 1 };
}

export interface LogLine {
  title: string;
  detail: string;
}

const humanise = (name: string): string => name.replace(/([a-z0-9])([A-Z])/g, '$1 $2');

/** A readable line for one engine event: its name, and its fields as "key value" pairs. */
export function describeEvent(event: SimEventRecord): LogLine {
  if (typeof event === 'string') return { title: humanise(event), detail: '' };
  const [name, body] = Object.entries(event)[0] ?? ['Unknown', null];
  const detail =
    body !== null && typeof body === 'object'
      ? Object.entries(body as Record<string, unknown>)
          .filter(([, v]) => typeof v === 'number' || typeof v === 'string' || typeof v === 'boolean')
          .map(([k, v]) => `${k.replace(/_/g, ' ')} ${String(v)}`)
          .join(', ')
      : '';
  return { title: humanise(name), detail };
}

/** What a turn needs from the Worker handle and the save store. */
export interface TurnDeps {
  bridge: { next(): Promise<TurnReport>; save(): Promise<Uint8Array> };
  saves: Pick<SaveStore, 'write'> | null;
  mode: SaveMode;
}

export interface TurnOutcome {
  report: TurnReport;
  /** Set when the autosave failed; the turn itself still happened. */
  saveError?: string;
}

/** Plays one week, then autosaves in free mode. Scenarios never save. */
export async function playTurn(deps: TurnDeps): Promise<TurnOutcome> {
  const report = await deps.bridge.next();
  if (deps.mode !== 'free' || !deps.saves) return { report };
  try {
    await deps.saves.write('auto', 'free', await deps.bridge.save(), report.turn);
    return { report };
  } catch (e) {
    return { report, saveError: e instanceof Error ? e.message : String(e) };
  }
}
