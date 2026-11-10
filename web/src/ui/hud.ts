import { t as i18nText, formatNumber } from '../i18n';
// The in-game screen: coins panel, date, Next week button, the map with its tools, side panels and the turn log.
import { SLOTS, type SaveMode, type Slot } from '../save/store';
import { button, el } from './dom';
import { isMuted, toggleMute } from '../audio/sound';
import { gameTabs, type Tab } from './tabs';
import { phoneShell } from './phone';
import { slotName } from './menu';
import { calendarDate, type DateLabel, type LogLine } from './turn';

export interface Hud {
  setState(turn: number, coins: number[], localContinent: number, continents: string[]): void;
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

const fmt = formatNumber;

export function renderHud(root: HTMLElement, mode: SaveMode, actions: HudActions, dateLabel: DateLabel = calendarDate, weeks?: number, title = mode === 'free' ? i18nText("hud.free-mode") : i18nText("hud.scenario")): Hud {
  const coins = el('strong', { id: 'coins', text: '-' });
  const date = el('span', { id: 'date', text: '-' });
  const caption = el('span', { id: 'next-label', text: i18nText("hud.next-week") });
  const next = button('', actions.nextWeek, 'next');
  next.className = 'primary';
  next.append(caption, el('span', { className: 'bar' }));
  const log = el('ol', { id: 'log', className: 'log' });
  log.setAttribute('role', 'log');
  log.setAttribute('aria-live', 'polite');
  const alert = el('p', { id: 'failure', className: 'failure' });
  alert.setAttribute('role', 'alert');
  alert.hidden = true;
  const mapHost = el('div', { id: 'map', className: 'map', title: i18nText("hud.map-drag-to-move-scroll-or-pinch-to-zoom-tap-a-town") });
  const mapTools = el('div', { className: 'maptools' });
  const tabs = gameTabs(mode !== 'free');
  tabs.panels.log.append(el('section', { className: 'logbox' }, el('h2', { text: i18nText("hud.turn-log") }), log));
  const status = el('span', { className: 'travel-status', id: 'travel-status' });
  status.setAttribute('role', 'status');
  mapHost.append(mapTools);
  const slot = el('select', { id: 'slot' });
  slot.setAttribute('aria-label', i18nText("hud.save-slot"));
  for (const s of SLOTS.filter((x) => x !== 'auto')) slot.append(new Option(slotName(s), s));
  const saveButton = mode === 'free' ? button(i18nText("hud.save"), () => actions.save(slot.value as Slot), 'save') : null;
  const saveControls = saveButton ? [slot, saveButton] : [];
  const mute = button(i18nText("hud.sound"), () => {
    mute.setAttribute('aria-pressed', String(toggleMute()));
  }, 'mute');
  mute.setAttribute('aria-description', i18nText("hud.mute-sound"));
  mute.setAttribute('aria-pressed', String(isMuted()));
  const menuButton = button('', actions.menu, 'menu');
  menuButton.setAttribute('aria-label', i18nText("hud.menu"));
  menuButton.setAttribute('aria-description', i18nText("hud.main-menu"));
  const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  icon.setAttribute('viewBox', '0 0 24 24');
  icon.setAttribute('class', 'menu-icon');
  icon.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(icon.namespaceURI, 'path');
  path.setAttribute('d', 'M3 6H21 M3 12H21 M3 18H21');
  path.setAttribute('stroke', 'currentColor');
  path.setAttribute('stroke-width', '2');
  icon.append(path);
  menuButton.append(icon, el('span', { className: 'menu-label', text: i18nText("hud.menu") }));

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
        el('div', { className: 'panel' }, el('span', { className: 'label', text: i18nText("hud.coins") }), coins),
        el('div', { className: 'panel' }, el('span', { className: 'label', text: i18nText("hud.date") }), date),
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
    setState(turn, coinList, local, continents) {
      date.textContent = weeks === undefined ? dateLabel(turn) : i18nText('hud.date-total', { date: dateLabel(turn), weeks });
      coins.textContent = fmt(coinList[local] ?? coinList[0] ?? 0);
      coins.title = coinList.map((c, i) => i18nText('hud.continent-coins', { continent: continents[i] ?? i18nText("hud.continent", { arg1: i }), coins: fmt(c) })).join('\n');
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
        caption.textContent = i18nText("hud.next-week");
        return;
      }
      const t0 = performance.now();
      const tick = (): void => {
        caption.textContent = i18nText("hud.merchants-moving-s", { arg1: Math.floor((performance.now() - t0) / 1000) });
      };
      tick();
      timer = window.setInterval(tick, 250);
    },
    addTurn(turn, lines) {
      const items = lines.slice(0, 200).map((l) => el('li', { text: l.detail ? i18nText('hud.event-detail', { title: l.title, detail: l.detail }) : l.title }));
      const head = el('strong', { text: i18nText("hud.events", { arg1: dateLabel(turn), arg2: lines.length }) });
      log.prepend(el('li', { className: 'turn' }, head, el('ul', {}, ...items)));
    },
    fail(text) {
      failed = true;
      window.clearInterval(timer);
      caption.textContent = i18nText("hud.next-week");
      next.classList.remove('busy');
      next.setAttribute('aria-busy', 'false');
      next.disabled = true;
      saveButton?.setAttribute('disabled', '');
      alert.textContent = i18nText("hud.open-the-menu-to-start-again", { arg1: text });
      alert.hidden = false;
      log.prepend(el('li', { className: 'message', text }));
    },
    addMessage(text) {
      log.prepend(el('li', { className: 'message', text }));
    },
  };
}
