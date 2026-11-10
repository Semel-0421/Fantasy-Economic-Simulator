import { t as i18nText, language, chooseLanguage } from '../i18n';
import { LANGUAGES } from '../i18n/languages';
// Main menu: the three scenarios, Free mode last, Continue, saves, Settings.
import { BROKEN_MESSAGE, type Slot, type SlotInfo } from '../save/store';
import { SCENARIOS } from '../data/scenarios';
import { button, el } from './dom';
import { renderCredits } from './credits';
import { resetGuide } from './guide';
import { setVolume, setMusic } from '../audio/sound';

export interface MenuActions {
  scenario(n: number): void;
  freeMode(): void;
  continueLatest(slot: Slot): void;
  loadSlot(slot: Slot): void;
}

const SLOT_NAMES: Record<Slot, string> = { auto: i18nText("menu.autosave"), manual1: i18nText("menu.slot-1"), manual2: i18nText("menu.slot-2"), manual3: i18nText("menu.slot-3") };
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

function settings(replay: () => void): HTMLElement {
  const languages = el('select', { id: 'language' });
  languages.replaceChildren(...LANGUAGES.map(entry => {
    const option = new Option(entry.nativeName, entry.code);
    option.lang = entry.code;
    option.dir = entry.dir;
    return option;
  }));
  languages.value = language.code;
  languages.addEventListener('change', () => chooseLanguage(languages.value));
  const languageLabel = el('label', {}, i18nText('menu.language'), languages);
  languageLabel.htmlFor = languages.id;
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
  slider.setAttribute('aria-label', i18nText("menu.sound-volume"));
  slider.addEventListener('input', () => writeVolume(Number(slider.value)));
  return el(
    'details',
    { className: 'settings' },
    el('summary', { text: i18nText("menu.settings") }),
    languageLabel,
    el('label', {}, i18nText(language.code === 'en' ? 'menu.sound-volume-2' : 'menu.sound-volume'), slider),
    el('label', { className: 'check' }, music, i18nText("menu.music")),
    el('p', { className: 'note', text: i18nText("menu.reduced-motion-follows-your-system-setting") }),
    button(i18nText("menu.show-the-guide-again"), replay, 'guide-replay'),
  );
}

function saveRow(info: SlotInfo, actions: MenuActions): HTMLElement {
  const row = el('li', { className: `save save-${info.status}` }, el('span', { className: 'save-name', text: slotName(info.slot) }));
  if (info.status === 'ok') {
    row.append(el('span', { className: 'save-meta', text: i18nText("menu.week", { arg1: info.turn ?? 0 }) }), button(i18nText("menu.load"), () => actions.loadSlot(info.slot)));
  } else if (info.status === 'broken') {
    row.append(el('span', { className: 'save-error', text: info.message ?? BROKEN_MESSAGE }));
  } else {
    row.append(el('span', { className: 'save-meta', text: i18nText("menu.empty") }));
  }
  return row;
}

export function renderMenu(root: HTMLElement, saves: SlotInfo[], latest: SlotInfo | null, actions: MenuActions, notice = ''): void {
  const scenarios = SCENARIOS.map((s) => {
    const b = button(i18nText("menu.scenario", { arg1: s.n, arg2: s.title }), () => actions.scenario(s.n), `scenario-${s.n}`);
    b.classList.add('scenario');
    if (s.n === 1) b.classList.add('primary');
    const pitch = el('p', { className: 'scenario-pitch', id: `pitch-${s.n}`, text: s.pitch });
    b.setAttribute('aria-describedby', pitch.id);
    return el('li', {}, b, pitch, el('p', { className: 'note', text: s.n === 1 ? i18nText("menu.30-min-recommended-first") : i18nText("menu.30-min") }));
  });
  const cont = button(i18nText("menu.continue"), () => latest && actions.continueLatest(latest.slot), 'continue');
  cont.disabled = !latest;
  const free = button(i18nText("menu.free-mode"), actions.freeMode, 'free-mode');
  root.replaceChildren(
    el(
      'main',
      { className: 'menu' },
      el('h1', { text: i18nText("menu.fantasy-economic-simulator") }),
      el('p', { className: 'tagline', text: i18nText("menu.buy-low-sail-far-sell-dear") }),
      notice ? el('p', { className: 'notice', id: 'notice', text: notice }) : el('span'),
      el('ul', { className: 'scenarios' }, ...scenarios),
      free,
      cont,
      el('h2', { text: i18nText("menu.saves") }),
      el('ul', { className: 'saves', id: 'saves' }, ...saves.map((s) => saveRow(s, actions))),
      settings(() => { resetGuide(); actions.scenario(1); }),
      button(i18nText("menu.credits"), () => renderCredits(root, () => renderMenu(root, saves, latest, actions, notice)), 'credits'),
    ),
  );
}
