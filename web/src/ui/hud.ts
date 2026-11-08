// The in-game screen: coins panel, date, Next week button, the map with its tools, side panels and the turn log.
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
  /** Shows that the simulation is gone: a persistent alert; the turn and save buttons stop. */
  fail(text: string): void;
  /** The element the map canvas goes into. */
  mapHost: HTMLElement;
  /** Under the map: travel controls. */
  mapTools: HTMLElement;
  /** Beside the map, above the log: market, cargo and purse. */
  side: HTMLElement;
}

export interface HudActions {
  nextWeek(): void;
  save(slot: Slot): void;
  menu(): void;
}

const fmt = (n: number): string => n.toLocaleString('en-US');

export function renderHud(root: HTMLElement, mode: SaveMode, actions: HudActions): Hud {
  const coins = el('strong', { id: 'coins', text: '-' });
  const date = el('span', { id: 'date', text: '-' });
  const caption = el('span', { id: 'next-label', text: 'Next week' });
  const next = button('', actions.nextWeek, 'next');
  next.append(caption, el('span', { className: 'bar' }));
  const log = el('ol', { id: 'log', className: 'log' });
  log.setAttribute('role', 'log');
  log.setAttribute('aria-live', 'polite');
  const alert = el('p', { id: 'failure', className: 'failure' });
  alert.setAttribute('role', 'alert');
  alert.hidden = true;
  const mapHost = el('div', { id: 'map', className: 'map', title: 'Map: drag to pan, wheel or pinch to zoom, tap a town' });
  const mapTools = el('div', { className: 'maptools' });
  const side = el('div', { className: 'panels' });
  const slot = el('select', { id: 'slot' });
  slot.setAttribute('aria-label', 'Save slot');
  for (const s of SLOTS.filter((x) => x !== 'auto')) slot.append(new Option(slotName(s), s));
  const saveButton = mode === 'free' ? button('Save', () => actions.save(slot.value as Slot), 'save') : null;
  const saveControls = saveButton ? [slot, saveButton] : [];

  root.replaceChildren(
    el(
      'div',
      { className: 'game' },
      alert,
      el(
        'header',
        { className: 'hud' },
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Coins' }), coins),
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Date' }), date),
        next,
        el('div', { className: 'tools' }, ...saveControls, button('Menu', actions.menu, 'menu')),
      ),
      el(
        'main',
        { className: 'play' },
        el('div', { className: 'mapcol' }, mapHost, mapTools),
        el('aside', { className: 'side' }, side, el('section', { className: 'logbox' }, el('h2', { text: 'Turn log' }), log)),
      ),
    ),
  );

  let timer = 0;
  let failed = false;
  return {
    mapHost,
    mapTools,
    side,
    setState(turn, coinList, local) {
      const d = dateOf(turn);
      date.textContent = `Year ${d.year}, Week ${d.week}`;
      coins.textContent = fmt(coinList[local] ?? coinList[0] ?? 0);
      coins.title = coinList.map((c, i) => `Continent ${i}: ${fmt(c)}`).join('\n');
    },
    setBusy(busy) {
      window.clearInterval(timer);
      next.disabled = busy || failed;
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
    fail(text) {
      failed = true;
      window.clearInterval(timer);
      caption.textContent = 'Next week';
      next.classList.remove('busy');
      next.setAttribute('aria-busy', 'false');
      next.disabled = true;
      saveButton?.setAttribute('disabled', '');
      alert.textContent = `${text} Use Menu to start again.`;
      alert.hidden = false;
      log.prepend(el('li', { className: 'message', text }));
    },
    addMessage(text) {
      log.prepend(el('li', { className: 'message', text }));
    },
  };
}
