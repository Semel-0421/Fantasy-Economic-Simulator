import { t as i18nText } from '../i18n';
// Turn flow (spec 5.2a): the player acts, presses Next week, the Worker moves the NPCs, and the
// report's events come back. Pure logic here; the DOM lives in hud.ts and main.ts.
import type { SaveMode, SaveStore } from '../save/store';
import type { SimEventRecord, TurnReport } from '../sim/types';
import { displayText } from '../data/names';

export const WEEKS_PER_YEAR = 52;

/** The calendar position of a turn count: turn 0 is Year 1, Week 1. */
export function dateOf(turn: number): { year: number; week: number } {
  return { year: Math.floor(turn / WEEKS_PER_YEAR) + 1, week: (turn % WEEKS_PER_YEAR) + 1 };
}

/** How the game names a turn: the calendar in free mode, the scenario week in a scenario. */
export type DateLabel = (turn: number) => string;

export const calendarDate: DateLabel = (turn) => {
  const d = dateOf(turn);
  return i18nText("turn.year-week", { arg1: d.year, arg2: d.week });
};

/** Scenario weeks count from the turn the scenario starts on (after its warm-up): that turn is Week 1. */
export const scenarioDate =
  (startTurn: number): DateLabel =>
  (turn) =>
    i18nText("turn.week", { arg1: turn - startTurn + 1 });

export interface LogLine {
  title: string;
  detail: string;
}

const humanise = (name: string): string => name.replace(/([a-z0-9])([A-Z])/g, '$1 $2');

/** A readable line for one engine event: its name, and its fields as "key value" pairs. */
export function describeEvent(event: SimEventRecord, nameOf: (field: string, value: unknown) => string = (_, value) => String(value)): LogLine {
  if (typeof event === 'string') return { title: humanise(event), detail: '' };
  const [name, body] = Object.entries(event)[0] ?? [i18nText("turn.unknown"), null];
  const detail =
    body !== null && typeof body === 'object'
      ? Object.entries(body as Record<string, unknown>)
          .filter(([, v]) => typeof v === 'number' || typeof v === 'string' || typeof v === 'boolean')
          .map(([k, v]) => i18nText('turn.field-value', { field: k.replace(/_/g, ' '), value: displayText(nameOf(k, v)) }))
          .join(', ')
      : '';
  return { title: humanise(name), detail };
}

/** What a turn needs from the Worker handle and the save store. */
export interface TurnDeps {
  bridge: { next(): Promise<TurnReport>; save(): Promise<Uint8Array>; mapSeed?: string };
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
    await deps.saves.write('auto', 'free', await deps.bridge.save(), report.turn, ...(deps.bridge.mapSeed ? [deps.bridge.mapSeed] : []));
    return { report };
  } catch (e) {
    return { report, saveError: e instanceof Error ? e.message : String(e) };
  }
}
