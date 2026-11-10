import type { LanguageCode } from './languages';

// Rechecked against the complete catalogues, scenarios and rendered names by font-coverage.test.mjs.
export const FONT_PROFILES: Record<LanguageCode, 'pixelify' | 'galmuri' | 'system'> = {
  "en": "pixelify",
  "zh-Hans": "system",
  "hi": "system",
  "es": "pixelify",
  "ar": "system",
  "fr": "system",
  "bn": "system",
  "pt": "pixelify",
  "ru": "system",
  "id": "pixelify",
  "ur": "system",
  "de": "pixelify",
  "ja": "galmuri",
  "pcm": "pixelify",
  "mr": "system",
  "te": "system",
  "tr": "system",
  "ha": "system",
  "ta": "system",
  "vi": "system",
  "ko": "galmuri"
};
