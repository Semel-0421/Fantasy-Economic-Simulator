// How the simulation's refusals reach the player: a preview's message before an order is placed,
// and the CommandRejected events of a turn for orders that failed when they ran. The text is the
// simulation's own (the C# message), never rewritten.
import type { CommandResult, SimEventRecord, TurnReport } from '../sim/types';
import { el } from './dom';

/** The message of a refused preview, or null when the command would be accepted. */
export const rejection = (result: CommandResult): string | null => (result.result === 'Error' ? result.message : null);

/** The reason of every command the turn refused. */
export function rejectionsIn(report: TurnReport): string[] {
  const out: string[] = [];
  for (const e of report.events as SimEventRecord[]) {
    if (typeof e === 'object' && 'CommandRejected' in e) out.push(e.CommandRejected.reason);
  }
  return out;
}

/** An alert box that shows one message at a time (empty hides it). */
export function errorBox(id: string): { node: HTMLElement; show(text: string): void } {
  const node = el('p', { id, className: 'error' });
  node.setAttribute('role', 'alert');
  node.hidden = true;
  return {
    node,
    show(text) {
      node.textContent = text;
      node.hidden = text === '';
    },
  };
}
