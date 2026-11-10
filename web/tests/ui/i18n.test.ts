import { chromium, type Browser } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, expect, it } from 'vitest';
import { LANGUAGES } from '../../src/i18n/languages';
import { ar } from '../../src/i18n/ar';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  server = await createServer({ server: { port: 4186, strictPort: true }, logLevel: 'error' });
  await server.listen(); url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

it('lists all native language names, persists a choice and reloads into translated RTL text', async () => {
  const page = await browser.newPage({ viewport: { width: 360, height: 740 } });
  try {
    await page.goto(url); await page.waitForSelector('#free-mode');
    expect(await page.getAttribute('html', 'lang')).toBe('en');
    expect(await page.getAttribute('html', 'dir')).toBe('ltr');
    await page.locator('.settings summary').click();
    expect(await page.getByLabel('Language').locator('option').allTextContents()).toEqual(LANGUAGES.map(language => language.nativeName));
    await page.getByLabel('Language').selectOption('ar');
    await page.waitForFunction(() => document.documentElement.lang === 'ar');
    await page.waitForSelector('#free-mode');
    expect(await page.getAttribute('html', 'dir')).toBe('rtl');
    expect(await page.evaluate(() => localStorage.getItem('fes-lang'))).toBe('ar');
    expect(await page.textContent('#free-mode')).toBe(ar['menu.free-mode']);
    await page.locator('.settings summary').click();
    expect(await page.locator('#language').inputValue()).toBe('ar');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.evaluate(() => localStorage.setItem('fes-lang', '__proto__'));
    await page.reload(); await page.waitForSelector('#free-mode');
    expect(await page.getAttribute('html', 'lang')).toBe('en');
    expect(await page.getAttribute('html', 'dir')).toBe('ltr');
  } finally { await page.close(); }
});

it('keeps the RTL map unmirrored and moves tab focus in visual reading order', async () => {
  const page = await browser.newPage();
  try {
    await page.addInitScript(() => localStorage.setItem('fes-lang', 'ur'));
    await page.goto(url); await page.click('#free-mode'); await page.waitForSelector('#map canvas');
    expect(await page.getAttribute('html', 'dir')).toBe('rtl');
    expect(await page.locator('#map canvas').evaluate(node => getComputedStyle(node).transform)).toBe('none');
    await page.locator('#tab-market').focus(); await page.keyboard.press('ArrowLeft');
    expect(await page.getAttribute('#tab-cargo', 'aria-selected')).toBe('true');
    await page.keyboard.press('ArrowRight');
    expect(await page.getAttribute('#tab-market', 'aria-selected')).toBe('true');
  } finally { await page.close(); }
});

it('shows the Korean scenario title and translates its intro before replacing world keys', async () => {
  const page = await browser.newPage();
  try {
    await page.addInitScript(() => localStorage.setItem('fes-lang', 'ko'));
    await page.goto(url); await page.waitForSelector('#scenario-1');
    expect(await page.textContent('#scenario-1')).toContain('첫 장사');
    await page.click('#scenario-1'); await page.waitForSelector('#dialog');
    expect(await page.textContent('#dialog-title')).toBe('첫 장사');
    const text = await page.locator('#dialog').innerText();
    expect(text).toContain('500코인');
    expect(text).not.toMatch(/\b(?:k\d+n\d+|g\d+_\d+)/);
  } finally { await page.close(); }
});
