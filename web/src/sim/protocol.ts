// The messages between the page and the simulation Worker. Every request carries an id; the
// Worker answers it with a Response of the same id, so calls can be awaited like functions.
import type { GameFiles } from '../data/files';
import type { PlayerCommand, ViewName } from './types';
import type { FullscaleBenchResult } from './fullscale_bench';

export type RequestBody =
  | { op: 'start'; files: GameFiles; seed: bigint }
  | { op: 'load'; files: GameFiles; bytes: Uint8Array }
  | { op: 'scenario'; files: GameFiles; text: string }
  | { op: 'accept'; contract: string }
  | { op: 'next' }
  | { op: 'queue'; command: PlayerCommand }
  | { op: 'preview'; command: PlayerCommand }
  | { op: 'view'; name: ViewName; arg?: number }
  | { op: 'save' }
  | { op: 'turn' }
  | { op: 'hash' }
  | { op: 'bench'; files: GameFiles; expectedHash11: string };

export type { FullscaleBenchResult };

export type Request = { id: number } & RequestBody;

export type Response = { id: number; ok: true; value: unknown } | { id: number; ok: false; message: string };
