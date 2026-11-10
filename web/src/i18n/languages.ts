export const LANGUAGES = [
  { code: 'en', nativeName: 'English', dir: 'ltr', locale: 'en-US', fontStack: 'Atkinson Hyperlegible, system-ui, sans-serif' },
  { code: 'zh-Hans', nativeName: '中文（简体）', dir: 'ltr', locale: 'zh-Hans', fontStack: 'Noto Sans SC, Microsoft YaHei, sans-serif' },
  { code: 'hi', nativeName: 'हिन्दी', dir: 'ltr', locale: 'hi-IN', fontStack: 'Noto Sans Devanagari, Nirmala UI, Mangal, sans-serif' },
  { code: 'es', nativeName: 'Español', dir: 'ltr', locale: 'es', fontStack: 'system-ui, sans-serif' },
  { code: 'ar', nativeName: 'العربية', dir: 'rtl', locale: 'ar', fontStack: 'Noto Sans Arabic, Tahoma, sans-serif' },
  { code: 'fr', nativeName: 'Français', dir: 'ltr', locale: 'fr', fontStack: 'system-ui, sans-serif' },
  { code: 'bn', nativeName: 'বাংলা', dir: 'ltr', locale: 'bn', fontStack: 'Noto Sans Bengali, Nirmala UI, sans-serif' },
  { code: 'pt', nativeName: 'Português', dir: 'ltr', locale: 'pt', fontStack: 'system-ui, sans-serif' },
  { code: 'ru', nativeName: 'Русский', dir: 'ltr', locale: 'ru', fontStack: 'system-ui, sans-serif' },
  { code: 'id', nativeName: 'Bahasa Indonesia', dir: 'ltr', locale: 'id', fontStack: 'system-ui, sans-serif' },
  { code: 'ur', nativeName: 'اردو', dir: 'rtl', locale: 'ur', fontStack: 'Noto Nastaliq Urdu, Noto Sans Arabic, serif' },
  { code: 'de', nativeName: 'Deutsch', dir: 'ltr', locale: 'de', fontStack: 'system-ui, sans-serif' },
  { code: 'ja', nativeName: '日本語', dir: 'ltr', locale: 'ja', fontStack: 'Noto Sans JP, Yu Gothic, sans-serif' },
  { code: 'pcm', nativeName: 'Naijá', dir: 'ltr', locale: 'en-NG', fontStack: 'system-ui, sans-serif' },
  { code: 'mr', nativeName: 'मराठी', dir: 'ltr', locale: 'mr-IN', fontStack: 'Noto Sans Devanagari, Nirmala UI, sans-serif' },
  { code: 'te', nativeName: 'తెలుగు', dir: 'ltr', locale: 'te', fontStack: 'Noto Sans Telugu, Nirmala UI, sans-serif' },
  { code: 'tr', nativeName: 'Türkçe', dir: 'ltr', locale: 'tr', fontStack: 'system-ui, sans-serif' },
  { code: 'ha', nativeName: 'Hausa', dir: 'ltr', locale: 'ha', fontStack: 'system-ui, sans-serif' },
  { code: 'ta', nativeName: 'தமிழ்', dir: 'ltr', locale: 'ta', fontStack: 'Noto Sans Tamil, Nirmala UI, sans-serif' },
  { code: 'vi', nativeName: 'Tiếng Việt', dir: 'ltr', locale: 'vi', fontStack: 'system-ui, sans-serif' },
  { code: 'ko', nativeName: '한국어', dir: 'ltr', locale: 'ko', fontStack: 'Malgun Gothic, Apple SD Gothic Neo, sans-serif' },
] as const;

export type LanguageCode = typeof LANGUAGES[number]['code'];

export function languageOf(code: string | null | undefined): typeof LANGUAGES[number] {
  return LANGUAGES.find(language => language.code === code) ?? LANGUAGES[0];
}
