// Main menu: the three scenarios, Free mode last, Continue, saves, Settings.
import { BROKEN_MESSAGE, type Slot, type SlotInfo } from '../save/store';
import { SCENARIOS } from '../data/scenarios';
import { button, el } from './dom';
import { renderCredits } from './credits';
import { setVolume, setMusic } from '../audio/sound';

export interface MenuActions {
  scenario(n: number): void;
  freeMode(): void;
  continueLatest(slot: Slot): void;
  loadSlot(slot: Slot): void;
}

const SLOT_NAMES: Record<Slot, string> = { auto: 'Autosave', manual1: 'Slot 1', manual2: 'Slot 2', manual3: 'Slot 3' };
export const slotName = (slot: Slot): string => SLOT_NAMES[slot];

const VOLUME_KEY = 'fes-volume';

export function readVolume(): number {
  try {
    const raw = localStorage.getItem(VOLUME_KEY);
    const v = raw === null ? NaN : Number(raw);
    return Number.isFinite(v) && v >= 0 && v <= 100 ? v : 70;
  } catch {
    return 70;
  }
}

function writeVolume(v: number): void {
  setVolume(v);
  try {
    localStorage.setItem(VOLUME_KEY, String(v));
  } catch {
    /* storage can be blocked; the setting still applies to this session */
  }
}

export function readMusic(): boolean {
  try { return localStorage.getItem('fes-music') !== 'off'; } catch { return true; }
}

function settings(): HTMLElement {
  const music = el('input', { id: 'music', type: 'checkbox' });
  music.checked = readMusic();
  music.addEventListener('change', () => {
    setMusic(music.checked);
    try { localStorage.setItem('fes-music', music.checked ? 'on' : 'off'); } catch { /* Session preference still applies. */ }
  });
  const slider = el('input', { id: 'volume', type: 'range' });
  slider.min = '0';
  slider.max = '100';
  slider.value = String(readVolume());
  slider.setAttribute('aria-label', 'Sound volume');
  slider.addEventListener('input', () => writeVolume(Number(slider.value)));
  return el(
    'details',
    { className: 'settings' },
    el('summary', { text: 'Settings' }),
    el('label', {}, 'Sound volume ', slider),
    el('label', { className: 'check' }, music, 'Music'),
    el('p', { className: 'note', text: 'Reduced motion follows your system setting.' }),
  );
}

function saveRow(info: SlotInfo, actions: MenuActions): HTMLElement {
  const row = el('li', { className: `save save-${info.status}` }, el('span', { className: 'save-name', text: slotName(info.slot) }));
  if (info.status === 'ok') {
    row.append(el('span', { className: 'save-meta', text: `Week ${info.turn ?? 0}` }), button('Load', () => actions.loadSlot(info.slot)));
  } else if (info.status === 'broken') {
    row.append(el('span', { className: 'save-error', text: info.message ?? BROKEN_MESSAGE }));
  } else {
    row.append(el('span', { className: 'save-meta', text: 'Empty' }));
  }
  return row;
}

export function renderMenu(root: HTMLElement, saves: SlotInfo[], latest: SlotInfo | null, actions: MenuActions, notice = ''): void {
  const scenarios = SCENARIOS.map((s) => {
    const b = button(`Scenario ${s.n}: ${s.title}`, () => actions.scenario(s.n), `scenario-${s.n}`);
    b.classList.add('scenario');
    if (s.n === 1) b.classList.add('primary');
    const pitch = el('p', { className: 'scenario-pitch', id: `pitch-${s.n}`, text: s.pitch });
    b.setAttribute('aria-describedby', pitch.id);
    return el('li', {}, b, pitch, el('p', { className: 'note', text: s.n === 1 ? '~30 min · Recommended first' : '~30 min' }));
  });
  const cont = button('Continue', () => latest && actions.continueLatest(latest.slot), 'continue');
  cont.disabled = !latest;
  const free = button('Free mode', actions.freeMode, 'free-mode');
  root.replaceChildren(
    el(
      'main',
      { className: 'menu' },
      el('h1', { text: 'Fantasy Economic Simulator' }),
      el('p', { className: 'tagline', text: 'Buy low, sail far, sell dear.' }),
      notice ? el('p', { className: 'notice', id: 'notice', text: notice }) : el('span'),
      el('ul', { className: 'scenarios' }, ...scenarios),
      free,
      cont,
      el('h2', { text: 'Saves' }),
      el('ul', { className: 'saves', id: 'saves' }, ...saves.map((s) => saveRow(s, actions))),
      settings(),
      button('Credits', () => renderCredits(root, () => renderMenu(root, saves, latest, actions, notice)), 'credits'),
    ),
  );
}
