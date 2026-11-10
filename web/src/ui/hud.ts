import { t as i18nText, formatNumber } from '../i18n';
// The in-game screen: coins panel, date, Next week button, the map with its tools, side panels and the turn log.
import { SLOTS, type SaveMode, type Slot } from '../save/store';
import { button, el } from './dom';
import { isMuted, toggleMute } from '../audio/sound';
import { gameTabs, type GameTabs, type Tab } from './tabs';
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

/** The elements of the game screen that the HUD updates after it is built. */
interface Parts {
  coins: HTMLElement;
  date: HTMLElement;
  caption: HTMLElement;
  next: HTMLButtonElement;
  log: HTMLElement;
  alert: HTMLElement;
  mapHost: HTMLElement;
  mapTools: HTMLElement;
  tabs: GameTabs;
  status: HTMLElement;
  saveButton: HTMLButtonElement | null;
}

/** The busy timer and whether the simulation has failed. */
interface Run {
  timer: number;
  failed: boolean;
}

function menuButtonFor(actions: HudActions): HTMLButtonElement {
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
  return menuButton;
}

function muteButton(): HTMLButtonElement {
  const mute = button(i18nText("hud.sound"), () => {
    mute.setAttribute('aria-pressed', String(toggleMute()));
  }, 'mute');
  mute.setAttribute('aria-description', i18nText("hud.mute-sound"));
  mute.setAttribute('aria-pressed', String(isMuted()));
  return mute;
}

function saveControlsFor(mode: SaveMode, actions: HudActions): { saveButton: HTMLButtonElement | null; saveControls: HTMLElement[] } {
  const slot = el('select', { id: 'slot' });
  slot.setAttribute('aria-label', i18nText("hud.save-slot"));
  for (const s of SLOTS.filter((x) => x !== 'auto')) slot.append(new Option(slotName(s), s));
  const saveButton = mode === 'free' ? button(i18nText("hud.save"), () => actions.save(slot.value as Slot), 'save') : null;
  return { saveButton, saveControls: saveButton ? [slot, saveButton] : [] };
}

function nextButton(actions: HudActions): { next: HTMLButtonElement; caption: HTMLElement } {
  const caption = el('span', { id: 'next-label', text: i18nText("hud.next-week") });
  const next = button('', actions.nextWeek, 'next');
  next.className = 'primary';
  next.append(caption, el('span', { className: 'bar' }));
  return { next, caption };
}

function setBusy(p: Parts, run: Run, busy: boolean): void {
  window.clearInterval(run.timer);
  p.next.disabled = busy || run.failed;
  p.next.classList.toggle('busy', busy);
  p.next.setAttribute('aria-busy', String(busy));
  p.tabs.node.inert = busy;
  p.mapTools.inert = busy;
  if (p.saveButton) p.saveButton.disabled = busy || run.failed;
  if (!busy) {
    p.caption.textContent = i18nText("hud.next-week");
    return;
  }
  const t0 = performance.now();
  const tick = (): void => {
    p.caption.textContent = i18nText("hud.merchants-moving-s", { arg1: Math.floor((performance.now() - t0) / 1000) });
  };
  tick();
  run.timer = window.setInterval(tick, 250);
}

function failHud(p: Parts, run: Run, text: string): void {
  run.failed = true;
  window.clearInterval(run.timer);
  p.caption.textContent = i18nText("hud.next-week");
  p.next.classList.remove('busy');
  p.next.setAttribute('aria-busy', 'false');
  p.next.disabled = true;
  p.saveButton?.setAttribute('disabled', '');
  p.alert.textContent = i18nText("hud.open-the-menu-to-start-again", { arg1: text });
  p.alert.hidden = false;
  p.log.prepend(el('li', { className: 'message', text }));
}

function addTurnLines(log: HTMLElement, dateLabel: DateLabel, turn: number, lines: LogLine[]): void {
  const items = lines.slice(0, 200).map((l) => el('li', { text: l.detail ? i18nText('hud.event-detail', { title: l.title, detail: l.detail }) : l.title }));
  const head = el('strong', { text: i18nText("hud.events", { arg1: dateLabel(turn), arg2: lines.length }) });
  log.prepend(el('li', { className: 'turn' }, head, el('ul', {}, ...items)));
}

/** Puts the game screen into the root. The landmark and the page heading sit outside .game, so they
 * stay in the accessibility tree while the growth presentation makes .game inert. */
function mountHud(root: HTMLElement, title: string, p: Parts, tools: HTMLElement[]): void {
  root.replaceChildren(
    el(
      'main',
      { className: 'game-main' },
      el('h1', { className: 'sr-only', text: title }),
      el(
      'div',
      { className: 'game' },
      p.alert,
      el(
        'header',
        { className: 'hud' },
        el('div', { className: 'panel' }, el('span', { className: 'label', text: i18nText("hud.coins") }), p.coins),
        el('div', { className: 'panel' }, el('span', { className: 'label', text: i18nText("hud.date") }), p.date),
        p.status,
        el('div', { className: 'tools' }, ...tools),
        p.next,
      ),
      el(
        'div',
        { className: 'play' },
        el('div', { className: 'mapcol' }, p.mapHost),
        p.tabs.node,
      ),
      ),
    ),
  );
}

export function renderHud(root: HTMLElement, mode: SaveMode, actions: HudActions, dateLabel: DateLabel = calendarDate, weeks?: number, title = mode === 'free' ? i18nText("hud.free-mode") : i18nText("hud.scenario")): Hud {
  const coins = el('strong', { id: 'coins', text: '-' });
  const date = el('span', { id: 'date', text: '-' });
  const { next, caption } = nextButton(actions);
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
  const { saveButton, saveControls } = saveControlsFor(mode, actions);
  const mute = muteButton();
  const menuButton = menuButtonFor(actions);
  const parts: Parts = { coins, date, caption, next, log, alert, mapHost, mapTools, tabs, status, saveButton };
  mountHud(root, title, parts, [mute, ...saveControls, menuButton]);
  const run: Run = { timer: 0, failed: false };
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
    destroy() { window.clearInterval(run.timer); tabs.destroy(); phone.destroy(); },
    coins,
    setState(turn, coinList, local, continents) {
      date.textContent = weeks === undefined ? dateLabel(turn) : i18nText('hud.date-total', { date: dateLabel(turn), weeks });
      coins.textContent = fmt(coinList[local] ?? coinList[0] ?? 0);
      coins.title = coinList.map((c, i) => i18nText('hud.continent-coins', { continent: continents[i] ?? i18nText("hud.continent", { arg1: i }), coins: fmt(c) })).join('\n');
    },
    setBusy(busy) { setBusy(parts, run, busy); },
    addTurn(turn, lines) { addTurnLines(log, dateLabel, turn, lines); },
    fail(text) { failHud(parts, run, text); },
    addMessage(text) {
      log.prepend(el('li', { className: 'message', text }));
    },
  };
}
