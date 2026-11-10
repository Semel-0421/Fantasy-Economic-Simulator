import { FONT_PROFILES } from './font-profiles';
import type { LANGUAGES } from './languages';

const fontUrls = {
  pixelify: new URL('../../node_modules/@fontsource/pixelify-sans/files/pixelify-sans-latin-400-normal.woff', import.meta.url).href,
  galmuri: new URL('../assets/fonts/Galmuri11.woff2', import.meta.url).href,
};

export async function loadLanguageFont(language: typeof LANGUAGES[number]): Promise<void> {
  if (language.code === 'en' || typeof document === 'undefined') return;
  const root = document.documentElement.style;
  root.setProperty('--font-body', language.fontStack);
  root.setProperty('--font-display', language.fontStack);
  const profile = FONT_PROFILES[language.code];
  if (profile === 'system') return;
  const family = profile === 'galmuri' ? 'FES Galmuri11' : 'FES Pixelify';
  try {
    const face = new FontFace(family, `url("${fontUrls[profile]}")`, profile === 'pixelify' ? { unicodeRange: 'U+0000-002F, U+003A-FFFF' } : {});
    document.fonts.add(await face.load());
    root.setProperty('--font-display', `"${family}", ${language.fontStack}`);
  } catch { /* A damaged font falls back to the language's system stack. */ }
}
