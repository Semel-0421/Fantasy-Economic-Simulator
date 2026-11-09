// The growth presentation (spec 5.6): when a week brings coins in (a sale, a dividend, a coupon, a
// delivered contract), input pauses briefly, the coins panel grows and its number counts up from the
// old total to the new, with a coin sound. One presentation per week covers all its gains (batched);
// a click or a key skips it, and with reduced motion the number is set at once, without the grow.
import type { SimEventRecord, TurnReport } from '../sim/types';
import { play } from '../audio/sound';
import { el } from './dom';

export interface Gain {
  label: string;
  coins: number;
}

/** How long the count-up runs, in milliseconds. */
export const GROWTH_MS = 1200;

const fmt = (n: number): string => n.toLocaleString('en-US');

/** The week's coin gains, in event order: filled sales and scripted payments. */
export function gainsOf(report: TurnReport, goodName: (good: number) => string): Gain[] {
  const out: Gain[] = [];
  for (const e of report.events as SimEventRecord[]) {
    if (typeof e !== 'object') continue;
    if ('GoodsTraded' in e) {
      const t = e.GoodsTraded;
      if (!t.bought && t.filled > 0) out.push({ label: `Sold ${t.filled} ${goodName(t.good)}`, coins: t.filled * t.price });
    } else if ('ScenarioDialog' in e && e.ScenarioDialog.coins > 0) {
      out.push({ label: e.ScenarioDialog.text, coins: e.ScenarioDialog.coins });
    }
  }
  return out;
}

/** The number shown `elapsed` ms into the count-up (ease-out cubic), a whole coin, exact at the end. */
export function countedValue(from: number, to: number, elapsed: number): number {
  const t = Math.min(1, Math.max(0, elapsed / GROWTH_MS));
  if (t >= 1) return to;
  const eased = 1 - (1 - t) ** 3;
  return Math.round(from + (to - from) * eased);
}

export const prefersReducedMotion = (): boolean =>
  typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

export interface GrowthTargets {
  /** The coins number in the HUD. */
  coins: HTMLElement;
  /** What stops taking input while it plays. */
  game: HTMLElement | null;
}

/**
 * Plays the presentation from `from` to `to` coins; resolves when it ends or is skipped. Leaves the
 * number at `to` either way.
 */
export function presentGrowth(targets: GrowthTargets, from: number, to: number, gains: Gain[]): Promise<void> {
  const { coins, game } = targets;
  const panel = coins.parentElement;
  const total = gains.reduce((s, g) => s + g.coins, 0);
  const badge = el('span', { id: 'growth', className: 'growth', text: `+${fmt(total)}` });
  badge.title = gains.map((g) => `${g.label}: +${fmt(g.coins)}`).join('\n');
  badge.setAttribute('role', 'status');
  panel?.append(badge);
  play('coin');
  const finish = (): void => {
    coins.textContent = fmt(to);
    panel?.classList.remove('growing');
    game?.removeAttribute('inert');
    window.setTimeout(() => badge.remove(), 1500);
  };
  if (prefersReducedMotion()) {
    finish();
    return Promise.resolve();
  }
  game?.setAttribute('inert', '');
  panel?.classList.add('growing');
  return new Promise((resolve) => {
    const t0 = performance.now();
    let done = false;
    const stop = (): void => {
      if (done) return;
      done = true;
      window.removeEventListener('pointerdown', stop, true);
      window.removeEventListener('keydown', stop, true);
      finish();
      resolve();
    };
    window.addEventListener('pointerdown', stop, true);
    window.addEventListener('keydown', stop, true);
    const frame = (now: number): void => {
      if (done) return;
      const elapsed = now - t0;
      coins.textContent = fmt(countedValue(from, to, elapsed));
      if (elapsed >= GROWTH_MS) stop();
      else requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  });
}
