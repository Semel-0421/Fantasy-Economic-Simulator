// The in-game screen: coins panel, date, Next week button, the map with its tools, side panels and the turn log.
import { SLOTS, type SaveMode, type Slot } from '../save/store';
import { button, el } from './dom';
import { isMuted, toggleMute } from '../audio/sound';
import { gameTabs, type Tab } from './tabs';
import { phoneShell } from './phone';
import { slotName } from './menu';
import { calendarDate, type DateLabel, type LogLine } from './turn';

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
  panels: Record<Tab, HTMLElement>;
  setTravel(text: string): void;
  setSelected(text: string): void;
  showTab(id: Tab): void;
  destroy(): void;
  /** The coins number (the growth presentation counts it up). */
  coins: HTMLElement;
}

export interface HudActions {
  nextWeek(): void;
  save(slot: Slot): void;
  menu(): void;
}

const fmt = (n: number): string => n.toLocaleString('en-US');

export function renderHud(root: HTMLElement, mode: SaveMode, actions: HudActions, dateLabel: DateLabel = calendarDate, weeks?: number, title = mode === 'free' ? 'Free mode' : 'Scenario'): Hud {
  const coins = el('strong', { id: 'coins', text: '-' });
  const date = el('span', { id: 'date', text: '-' });
  const caption = el('span', { id: 'next-label', text: 'Next week' });
  const next = button('', actions.nextWeek, 'next');
  next.className = 'primary';
  next.append(caption, el('span', { className: 'bar' }));
  const log = el('ol', { id: 'log', className: 'log' });
  log.setAttribute('role', 'log');
  log.setAttribute('aria-live', 'polite');
  const alert = el('p', { id: 'failure', className: 'failure' });
  alert.setAttribute('role', 'alert');
  alert.hidden = true;
  const mapHost = el('div', { id: 'map', className: 'map', title: 'Map: drag to pan, wheel or pinch to zoom, tap a town' });
  const mapTools = el('div', { className: 'maptools' });
  const tabs = gameTabs(mode !== 'free');
  tabs.panels.log.append(el('section', { className: 'logbox' }, el('h2', { text: 'Turn log' }), log));
  const status = el('span', { className: 'travel-status', id: 'travel-status' });
  status.setAttribute('role', 'status');
  mapHost.append(mapTools);
  const slot = el('select', { id: 'slot' });
  slot.setAttribute('aria-label', 'Save slot');
  for (const s of SLOTS.filter((x) => x !== 'auto')) slot.append(new Option(slotName(s), s));
  const saveButton = mode === 'free' ? button('Save', () => actions.save(slot.value as Slot), 'save') : null;
  const saveControls = saveButton ? [slot, saveButton] : [];
  const mute = button('Sound', () => {
    mute.setAttribute('aria-pressed', String(toggleMute()));
  }, 'mute');
  mute.setAttribute('aria-label', 'Mute sound');
  mute.setAttribute('aria-pressed', String(isMuted()));
  const menuButton = button('', actions.menu, 'menu');
  menuButton.setAttribute('aria-label', 'Main menu');
  const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  icon.setAttribute('viewBox', '0 0 24 24');
  icon.setAttribute('class', 'menu-icon');
  icon.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(icon.namespaceURI, 'path');
  path.setAttribute('d', 'M3 6H21 M3 12H21 M3 18H21');
  path.setAttribute('stroke', 'currentColor');
  path.setAttribute('stroke-width', '2');
  icon.append(path);
  menuButton.append(icon, el('span', { className: 'menu-label', text: 'Menu' }));

  // The landmark and the page heading sit outside .game, so they stay in the accessibility tree while
  // the growth presentation makes .game inert.
  root.replaceChildren(
    el(
      'main',
      { className: 'game-main' },
      el('h1', { className: 'sr-only', text: title }),
      el(
      'div',
      { className: 'game' },
      alert,
      el(
        'header',
        { className: 'hud' },
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Coins' }), coins),
        el('div', { className: 'panel' }, el('span', { className: 'label', text: 'Date' }), date),
        status,
        el('div', { className: 'tools' }, mute, ...saveControls, menuButton),
        next,
      ),
      el(
        'div',
        { className: 'play' },
        el('div', { className: 'mapcol' }, mapHost),
        tabs.node,
      ),
      ),
    ),
  );

  let timer = 0;
  let failed = false;
  const phone = phoneShell(root.querySelector<HTMLElement>('.game')!, tabs, {
    header: root.querySelector<HTMLElement>('.hud')!, tools: root.querySelector<HTMLElement>('.tools')!,
    next, map: mapHost, travel: mapTools, extras: [mute, ...saveControls], menu: actions.menu,
  });
  return {
    mapHost,
    mapTools,
    panels: tabs.panels,
    setTravel(text) { status.textContent = text; },
    setSelected: phone.select,
    showTab: tabs.activate,
    destroy() { window.clearInterval(timer); tabs.destroy(); phone.destroy(); },
    coins,
    setState(turn, coinList, local) {
      date.textContent = `${dateLabel(turn)}${weeks === undefined ? '' : ` / ${weeks}`}`;
      coins.textContent = fmt(coinList[local] ?? coinList[0] ?? 0);
      coins.title = coinList.map((c, i) => `Continent ${i}: ${fmt(c)}`).join('\n');
    },
    setBusy(busy) {
      window.clearInterval(timer);
      next.disabled = busy || failed;
      next.classList.toggle('busy', busy);
      next.setAttribute('aria-busy', String(busy));
      tabs.node.inert = busy;
      mapTools.inert = busy;
      if (saveButton) saveButton.disabled = busy || failed;
      if (!busy) {
        caption.textContent = 'Next week';
        return;
      }
      const t0 = performance.now();
      const tick = (): void => {
        caption.textContent = `Merchants moving… ${Math.floor((performance.now() - t0) / 1000)}s`;
      };
      tick();
      timer = window.setInterval(tick, 250);
    },
    addTurn(turn, lines) {
      const items = lines.slice(0, 200).map((l) => el('li', { text: l.detail ? `${l.title}: ${l.detail}` : l.title }));
      const head = el('strong', { text: `${dateLabel(turn)} - ${lines.length} events` });
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
