// Browser flow with Playwright and the installed Edge (BROWSER_CHANNEL overrides): menu, a turn with
// progress, a map that stays responsive during the turn, autosave + Continue, a corrupt save,
// and a 360 px layout with no sideways scroll.
import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

let server: ViteDevServer;
let browser: Browser;
let url = '';

beforeAll(async () => {
  server = await createServer({ server: { port: 4174, strictPort: true }, logLevel: 'error' });
  await server.listen();
  url = server.resolvedUrls?.local[0] ?? 'http://localhost:4174/';
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);

afterAll(async () => {
  await browser?.close();
  await server?.close();
});

async function fresh(width = 1000): Promise<{ page: Page; errors: string[] }> {
  const context = await browser.newContext({ viewport: { width, height: 800 } });
  const page = await context.newPage();
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto(url);
  return { page, errors };
}

async function startFree(page: Page): Promise<void> {
  await page.click('#free-mode');
  await page.waitForFunction(() => (document.getElementById('coins')?.textContent ?? '-') !== '-', null, { timeout: 60000 });
}

describe('web flow', () => {
  it('menu lists scenarios as coming soon, Free mode last, Continue disabled without saves', async () => {
    const { page, errors } = await fresh();
    await page.waitForSelector('#free-mode');
    expect(await page.locator('.scenario').count()).toBe(3);
    expect(await page.locator('.scenario:disabled').count()).toBe(3);
    expect(await page.locator('.soon').allTextContents()).toEqual(['Coming soon', 'Coming soon', 'Coming soon']);
    expect(await page.locator('#continue').isDisabled()).toBe(true);
    const order = await page.locator('main.menu > *').evaluateAll((n) => n.map((x) => x.id || x.className));
    expect(order.indexOf('scenarios')).toBeLessThan(order.indexOf('free-mode'));
    expect(errors).toEqual([]);
  });

  it('says so and returns to the menu when the simulation fails to load', async () => {
    const context = await browser.newContext({ viewport: { width: 1000, height: 800 } });
    await context.route('**/*.wasm', (route) => route.abort());
    const page = await context.newPage();
    await page.goto(url);
    await page.click('#free-mode');
    await page.waitForSelector('#notice', { timeout: 30000 });
    expect(await page.textContent('#notice')).toMatch(/^Could not start: /);
    expect(await page.locator('#status').count()).toBe(0); // no endless "Loading"
    expect(await page.isDisabled('#free-mode')).toBe(false);
    await context.close();
  });

  it('plays a turn with visible progress, a live map, a log, an autosave and Continue', async () => {
    const { page, errors } = await fresh();
    await startFree(page);
    expect(await page.textContent('#date')).toBe('Year 1, Week 1');
    await page.evaluate(() => {
      const w = window as unknown as { __long: number[] };
      w.__long = [];
      new PerformanceObserver((l) => l.getEntries().forEach((e) => w.__long.push(e.duration))).observe({ entryTypes: ['longtask'] });
    });
    await page.click('#next');
    expect(await page.getAttribute('#next', 'aria-busy')).toBe('true');
    expect(await page.isDisabled('#next')).toBe(true);
    expect(await page.textContent('#next-label')).toContain('moving');
    await page.click('#map');
    await page.waitForFunction(() => document.getElementById('date')?.textContent === 'Year 1, Week 2', null, { timeout: 60000 });
    expect(await page.getAttribute('#next', 'aria-busy')).toBe('false');
    expect(await page.textContent('#next-label')).toBe('Next week');
    expect(await page.locator('#log > li.turn').count()).toBe(1);
    const long = await page.evaluate(() => (window as unknown as { __long: number[] }).__long);
    expect(Math.max(0, ...long)).toBeLessThan(200);

    await page.click('#menu');
    await page.waitForSelector('#free-mode');
    expect(await page.isDisabled('#continue')).toBe(false);
    await page.click('#continue');
    await page.waitForFunction(() => document.getElementById('date')?.textContent === 'Year 1, Week 2', null, { timeout: 60000 });
    expect(errors).toEqual([]);
  }, 120000);

  it('shows a corrupt save as unloadable and keeps the other slots', async () => {
    const { page } = await fresh();
    await startFree(page);
    await page.selectOption('#slot', 'manual1');
    await page.click('#save');
    await page.waitForSelector('#log .message');
    await page.evaluate(
      () =>
        new Promise<void>((resolve, reject) => {
          const open = indexedDB.open('fes-saves', 1);
          open.onsuccess = () => {
            const tx = open.result.transaction('slots', 'readwrite');
            tx.objectStore('slots').put({ foreign: true }, 'manual2');
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
          };
        }),
    );
    await page.click('#menu');
    await page.waitForSelector('#saves');
    const texts = await page.locator('#saves li').allTextContents();
    expect(texts.some((t) => t.includes("This save can't be loaded."))).toBe(true);
    expect(texts.some((t) => t.includes('Slot 1') && t.includes('Load'))).toBe(true);
  }, 120000);

  it('fits 360 px wide without sideways scrolling', async () => {
    const { page } = await fresh(360);
    await page.waitForSelector('#free-mode');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= 360)).toBe(true);
    await startFree(page);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= 360)).toBe(true);
  }, 120000);
});
