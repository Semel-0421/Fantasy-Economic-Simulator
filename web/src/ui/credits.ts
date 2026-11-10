import { t as i18nText } from '../i18n';
import { availableFiles } from '../assets/loader';
import { button, el } from './dom';

export const BUNDLED_LICENSES = {
  '@fontsource/pixelify-sans': { name: i18nText("credits.pixelify-sans"), licence: i18nText("credits.sil-open-font-license-1-1"), file: 'pixelify-sans.txt' },
  '@fontsource/atkinson-hyperlegible': { name: i18nText("credits.atkinson-hyperlegible"), licence: i18nText("credits.sil-open-font-license-1-1"), file: 'atkinson-hyperlegible.txt' },
  'pixi.js': { name: i18nText("credits.pixijs"), licence: i18nText("credits.mit"), file: 'pixi.txt' },
};

export function renderCredits(root: HTMLElement, back: () => void): void {
  const licences = [...Object.values(BUNDLED_LICENSES), { name: 'Galmuri11', licence: i18nText('credits.sil-open-font-license-1-1'), file: 'galmuri.txt' }].map((item) => {
    const link = el('a', { text: i18nText('credits.licence-line', { name: item.name, licence: item.licence }) });
    link.href = `licenses/${item.file}`;
    return el('li', {}, link);
  });
  const art = [...availableFiles].some((file) => file.endsWith('.png'));
  const sound = [...availableFiles].some((file) => file.endsWith('.ogg'));
  const disclosure: HTMLElement[] = [];
  if (art) disclosure.push(el('p', { text: i18nText("credits.art-made-with-spritecook") }));
  if (sound) disclosure.push(el('p', { text: i18nText("credits.music-and-sound-effects-made-with-elevenlabs") }));
  if (!art && !sound) disclosure.push(el('p', { text: i18nText("credits.art-and-sound-placeholders-drawn-in-code") }));
  root.replaceChildren(el('main', { className: 'menu credits' },
    el('h1', { text: i18nText("credits.credits") }),
    el('p', { text: i18nText("credits.v", { arg1: __APP_VERSION__ }) }),
    el('h2', { text: i18nText("credits.fonts-and-libraries") }), el('ul', {}, ...licences),
    el('h2', { text: i18nText("credits.production") }), ...disclosure,
    el('p', { text: i18nText("credits.english-scenario-text-drafted-with-claude-and-edited") }),
    el('p', { text: i18nText('credits.machine-translations') }),
    button(i18nText("credits.main-menu"), back, 'credits-back'),
  ));
}
