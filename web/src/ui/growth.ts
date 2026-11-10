import { t as i18nText, formatNumber } from '../i18n';
// The growth presentation (spec 5.6): when a week brings coins in (a sale, a dividend, a coupon, a
// delivered contract), input pauses briefly, the coins panel grows and its number counts up from the
// old total to the new, with a coin sound. One presentation per week covers all its gains (batched);
// a click or a key skips it, and with reduced motion the number is set at once, without the grow.
import type { SimEventRecord, TurnReport } from '../sim/types';
import { play } from '../audio/sound';
import { el } from './dom';
import { displayText } from '../data/names';
import { translateScenario } from '../i18n/scenario';

export interface Gain {
  label: string;
  coins: number;
}

/** A tenfold-purse gain reaches the cap; small gains still get a readable 800ms. */
export function growthDuration(total: number, coinsBefore: number): number {
  const relative = Math.max(0, total) / Math.max(1, Math.abs(coinsBefore));
  return Math.min(2000, 800 + 1200 * Math.log1p(relative) / Math.log(11));
}

const fmt = formatNumber;

/** The week's coin gains, in event order: filled sales and scripted payments. */
export function gainsOf(report: TurnReport, goodName: (good: number) => string): Gain[] {
  const out: Gain[] = [];
  for (const e of report.events as SimEventRecord[]) {
    if (typeof e !== 'object') continue;
    if ('GoodsTraded' in e) {
      const t = e.GoodsTraded;
      if (!t.bought && t.filled > 0) out.push({ label: i18nText("growth.sold", { arg1: t.filled, arg2: goodName(t.good) }), coins: t.filled * t.price });
    } else if ('ScenarioDialog' in e && e.ScenarioDialog.coins > 0) {
      out.push({ label: displayText(translateScenario(e.ScenarioDialog.text)), coins: e.ScenarioDialog.coins });
    }
  }
  return out;
}

/** The number shown `elapsed` ms into the count-up (ease-out cubic), a whole coin, exact at the end. */
export function countedValue(from: number, to: number, elapsed: number, duration: number): number {
  const t = Math.min(1, Math.max(0, elapsed / duration));
  if (t >= 1) return to;
  const eased = 1 - (1 - t) ** 3;
  return Math.round(from + (to - from) * eased);
}

const prefersReducedMotion = (): boolean =>
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
  const duration = growthDuration(total, from);
  const badge = el('span', { id: 'growth', className: 'growth', text: i18nText('growth.badge', { amount: fmt(total) }) });
  badge.title = gains.map((g) => i18nText('growth.gain-line', { label: g.label, amount: fmt(g.coins) })).join('\n');
  badge.setAttribute('aria-hidden', 'true');
  const announcement = el('span', { id: 'growth-announcement', className: 'sr-only' });
  announcement.setAttribute('role', 'status');
  announcement.setAttribute('aria-live', 'polite');
  announcement.setAttribute('aria-atomic', 'true');
  document.body.append(announcement);
  requestAnimationFrame(() => {
    if (announcement.isConnected) announcement.textContent = i18nText('growth.announcement', { amount: fmt(total), details: badge.title.replaceAll('\n', '. ') });
  });
  panel?.append(badge);
  play('coin');
  const finish = (): void => {
    coins.textContent = fmt(to);
    panel?.classList.remove('growing');
    game?.removeAttribute('inert');
    window.setTimeout(() => { badge.remove(); announcement.remove(); }, 1500);
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
      coins.textContent = fmt(countedValue(from, to, elapsed, duration));
      if (elapsed >= duration) stop();
      else requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  });
}
