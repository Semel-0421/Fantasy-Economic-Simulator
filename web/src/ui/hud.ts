// The in-game screen: coins panel, date, Next week button, a map area and the turn log.
import { SLOTS, type SaveMode, type Slot } from '../save/store';
import { button, el } from './dom';
import { slotName } from './menu';
import { dateOf, type LogLine } from './turn';

export interface Hud {
  setState(turn: number, coins: number[], localContinent: number): void;
  /** Shows the button as working (with elapsed seconds) or ready again. */
  setBusy(busy: boolean): void;
  addTurn(turn: number, lines: LogLine[]): void;
  addMessage(text: string): void;
}

export interface HudActions {
  nextWeek(): void;
  save(slot: Slot): void;
  menu(): void;
}

const fmt = (n: number): string => n.toLocaleString('en-US');

/** A placeholder for the map that stays interactive, so a long turn visibly does not freeze input. */
function mapPad(): HTMLElement {
  const out = el('output', { id: 'map-hint', text: 'Click or press a key on the map.' });
  const pad = el('div', { id: 'map', className: 'map', title: 'Map (placeholder)' }, el('span', { text: 'Map' }), out);
  pad.tabIndex = 0;
  let n = 0;
  const poke = (): void => {
    n += 1;
    out.textContent = `Map input received: ${n}`;
    pad.dataset.pokes = String(n);
  };
  pad.addEventListener('pointerdown', poke);
  pad.addEventListener('keydown', poke);
  return pad;
}

export function renderHud(root: HTMLElement, mode: SaveMode, actions: HudActions): Hud {
  const coins = el('strong', { id: 'coins', text: '-' });
  const date = el('span', { id: 'date', text: '-' });
  const caption = el('span', { id: 'next-label', text: 'Next week' });
  const next = button('', actions.nextWeek, 'next');
  next.append(caption, el('span', { className: 'bar' }));
  const log = el('ol', { id: 'log', className: 'log' });
  const slot = el('select', { id: 'slot' });
  slot.setAttribute('aria-label', 'Save slot');
  for (const s of SLOTS.filter((x) => x !== 'auto')) slot.append(new Option(slotName(s), s));
  const saveControls = mode === 'free' ? [slot, button('Save', () => actions.save(slot.value as Slot), 'save')] : [];

  root.replaceChildren(
    el(
      'div',
      { className: 'game' },
      el(
        'header',
        { className: 'hud' },
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Coins' }), coins),
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Date' }), date),
        next,
        el('div', { className: 'tools' }, ...saveControls, button('Menu', actions.menu, 'menu')),
      ),
      el('main', { className: 'play' }, mapPad(), el('aside', { className: 'logbox' }, el('h2', { text: 'Turn log' }), log)),
    ),
  );

  let timer = 0;
  return {
    setState(turn, coinList, local) {
      const d = dateOf(turn);
      date.textContent = `Year ${d.year}, Week ${d.week}`;
      coins.textContent = fmt(coinList[local] ?? coinList[0] ?? 0);
      coins.title = coinList.map((c, i) => `Continent ${i}: ${fmt(c)}`).join('\n');
    },
    setBusy(busy) {
      window.clearInterval(timer);
      next.disabled = busy;
      next.classList.toggle('busy', busy);
      next.setAttribute('aria-busy', String(busy));
      if (!busy) {
        caption.textContent = 'Next week';
        return;
      }
      const t0 = performance.now();
      const tick = (): void => {
        caption.textContent = `The merchants are moving... ${Math.floor((performance.now() - t0) / 1000)}s`;
      };
      tick();
      timer = window.setInterval(tick, 250);
    },
    addTurn(turn, lines) {
      const d = dateOf(turn);
      const items = lines.slice(0, 200).map((l) => el('li', { text: l.detail ? `${l.title}: ${l.detail}` : l.title }));
      const head = el('strong', { text: `Year ${d.year}, Week ${d.week} - ${lines.length} events` });
      log.prepend(el('li', { className: 'turn' }, head, el('ul', {}, ...items)));
    },
    addMessage(text) {
      log.prepend(el('li', { className: 'message', text }));
    },
  };
}
