import { afterEach, describe, expect, it, vi } from 'vitest';
import { en } from '../src/i18n/en';
import { LANGUAGES } from '../src/i18n/languages';
import { catalogues, COMPLETE_LANGUAGES, catalogueCoverage } from '../src/i18n/catalogues';

afterEach(() => { vi.unstubAllGlobals(); vi.resetModules(); });

describe('language state and formatting', () => {
  it('defaults to English without storage and never detects the browser language', async () => {
    vi.stubGlobal('navigator', { language: 'ar' });
    vi.stubGlobal('localStorage', { getItem: () => null });
    const { language, t, formatNumber } = await import('../src/i18n');
    expect(language.code).toBe('en');
    expect(t('turn.year-week', { arg1: 1, arg2: 2 })).toBe('Year 1, Week 2');
    expect(formatNumber(1234)).toBe('1,234');
  });
  it.each(['unknown', 'constructor', '__proto__', 'EN', ''])('rejects unknown stored value %j', async (code) => {
    vi.stubGlobal('localStorage', { getItem: () => code });
    expect((await import('../src/i18n')).language.code).toBe('en');
  });
  it('reads fes-lang only once, sets language/direction and falls back per id', async () => {
    const getItem = vi.fn(() => 'ar');
    vi.stubGlobal('localStorage', { getItem });
    const { language, applyLanguage, t, formatNumber } = await import('../src/i18n');
    const root = { lang: '', dir: '' };
    applyLanguage(root);
    expect(root).toEqual({ lang: 'ar', dir: 'rtl' });
    expect(language.code).toBe('ar');
    expect(t('menu.language')).toBe(catalogues.ar['menu.language']);
    expect(t('main.couldn-t-start-the-game', { arg1: '{arg2} <error>' })).toBe(catalogues.ar['main.couldn-t-start-the-game']!.replace('{arg1}', '{arg2} <error>'));
    const { catalogues: activeCatalogues } = await import('../src/i18n/catalogues');
    const translated = activeCatalogues.ar['menu.language'];
    delete activeCatalogues.ar['menu.language'];
    expect(t('menu.language')).toBe('Language');
    activeCatalogues.ar['menu.language'] = translated;
    expect(formatNumber(1234)).toMatch(/1.*234/);
    expect(formatNumber(1234)).not.toMatch(/[٠-٩]/);
    expect(getItem).toHaveBeenCalledExactlyOnceWith('fes-lang');
  });
  it('handles blocked storage and saves a chosen language before reloading', async () => {
    const getItem = vi.fn(() => { throw new Error('blocked'); });
    const setItem = vi.fn(); const reload = vi.fn();
    vi.stubGlobal('localStorage', { getItem, setItem });
    vi.stubGlobal('location', { reload });
    const { language, chooseLanguage } = await import('../src/i18n');
    expect(language.code).toBe('en');
    chooseLanguage('ko');
    expect(setItem).toHaveBeenCalledWith('fes-lang', 'ko');
    expect(reload).toHaveBeenCalledOnce();
    setItem.mockImplementation(() => { throw new Error('blocked'); });
    expect(() => chooseLanguage('ar')).not.toThrow();
  });
  it('uses structured scenario weeks for Korean licence expiry rather than a Week prefix', async () => {
    vi.stubGlobal('localStorage', { getItem: () => 'ko' });
    const { heldLicenceText } = await import('../src/ui/finance');
    const { scenarioDate } = await import('../src/ui/turn');
    const text = heldLicenceText({ base_good: -1, nation: 7, expires_turn: 55 }, () => 'Goods', () => 'Nation', scenarioDate(4));
    expect(text).toContain('52주');
    expect(text).not.toContain('Week');
    expect(text).not.toContain('56');
  });
});

describe('catalogue coverage', () => {
  it('registers exactly the planned 21 languages and only Arabic/Urdu as RTL', () => {
    expect(LANGUAGES.map(l => l.code)).toEqual(['en', 'zh-Hans', 'hi', 'es', 'ar', 'fr', 'bn', 'pt', 'ru', 'id', 'ur', 'de', 'ja', 'pcm', 'mr', 'te', 'tr', 'ha', 'ta', 'vi', 'ko']);
    expect(LANGUAGES.filter(l => l.dir === 'rtl').map(l => l.code)).toEqual(['ar', 'ur']);
    for (const language of LANGUAGES) {
      expect(language.nativeName).not.toBe('');
      expect(language.fontStack).not.toBe('');
      expect(() => new Intl.NumberFormat(language.locale)).not.toThrow();
    }
  });
  it('reports per-language fallbacks and rejects missing IDs/parameters once complete', () => {
    for (const language of LANGUAGES) {
      const coverage = catalogueCoverage(catalogues[language.code]);
      console.info(`${language.code}: ${coverage.missing.length}/${Object.keys(en).length} English fallbacks`);
      if (COMPLETE_LANGUAGES.includes(language.code)) {
        expect(coverage.missing).toEqual([]);
        expect(coverage.parameterMismatches).toEqual([]);
      }
    }
    expect(catalogueCoverage(en)).toEqual({ missing: [], parameterMismatches: [] });
    const incomplete = catalogueCoverage({ 'turn.year-week': 'Year {arg1}' });
    expect(incomplete.missing).toContain('menu.language');
    expect(incomplete.parameterMismatches).toContain('turn.year-week');
    expect(catalogueCoverage({ ...en, 'turn.year-week': '{arg2} / {arg1}' }).parameterMismatches).toEqual([]);
  });
});
