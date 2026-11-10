import { availableFiles } from '../assets/loader';
import { button, el } from './dom';

export const BUNDLED_LICENSES = {
  '@fontsource/pixelify-sans': { name: 'Pixelify Sans', licence: 'SIL Open Font License 1.1', file: 'pixelify-sans.txt' },
  '@fontsource/atkinson-hyperlegible': { name: 'Atkinson Hyperlegible', licence: 'SIL Open Font License 1.1', file: 'atkinson-hyperlegible.txt' },
  'pixi.js': { name: 'PixiJS', licence: 'MIT', file: 'pixi.txt' },
};

export function renderCredits(root: HTMLElement, back: () => void): void {
  const licences = Object.values(BUNDLED_LICENSES).map((item) => {
    const link = el('a', { text: `${item.name} — ${item.licence}` });
    link.href = `licenses/${item.file}`;
    return el('li', {}, link);
  });
  const art = [...availableFiles].some((file) => file.endsWith('.png'));
  const sound = [...availableFiles].some((file) => file.endsWith('.ogg'));
  const disclosure: HTMLElement[] = [];
  if (art) disclosure.push(el('p', { text: 'Art made with SpriteCook.' }));
  if (sound) disclosure.push(el('p', { text: 'Music and sound effects made with ElevenLabs.' }));
  if (!art && !sound) disclosure.push(el('p', { text: 'Art and sound: placeholders drawn in code.' }));
  root.replaceChildren(el('main', { className: 'menu credits' },
    el('h1', { text: 'Credits' }),
    el('p', { text: `v${__APP_VERSION__}` }),
    el('h2', { text: 'Fonts and libraries' }), el('ul', {}, ...licences),
    el('h2', { text: 'Production' }), ...disclosure,
    el('p', { text: 'English scenario text drafted with Claude and edited by the developer. The game makes no AI or network calls while you play.' }),
    button('Main menu', back, 'credits-back'),
  ));
}
