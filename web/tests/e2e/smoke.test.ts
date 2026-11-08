// Production-build smoke (Playwright, `vite preview` of a fresh build; BROWSER_CHANNEL picks the browser,
// default the installed Edge, `chromium` in CI): menu -> Free mode -> buy -> Next week -> save -> reload ->
// continue, plus the PWA: installable manifest and playing offline after one visit.
import { chromium, type Browser, type Page } from 'playwright';
import { build, preview, type PreviewServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

let server: PreviewServer;
let browser: Browser;
let url = '';

beforeAll(async () => {
  process.env.NODE_ENV = 'production'; // vitest sets 'test', which would build without the service worker
  await build({ mode: 'production', logLevel: 'silent', build: { outDir: 'dist-e2e' } });
  server = await preview({ build: { outDir: 'dist-e2e' }, preview: { port: 4176, strictPort: true } });
  url = server.resolvedUrls?.local[0] ?? 'http://localhost:4176/';
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 180000);

afterAll(async () => {
  await browser?.close();
  await new Promise<void>((resolve) => (server ? server.httpServer.close(() => resolve()) : resolve()));
});

const waitDate = (page: Page, date: string): Promise<unknown> =>
  page.waitForFunction((d) => document.getElementById('date')?.textContent === d, date, { timeout: 60000 });

async function startFree(page: Page): Promise<void> {
  await page.click('#free-mode');
  await page.waitForFunction(() => document.querySelector('#map canvas') && document.querySelector('#market-table tbody tr'), null, { timeout: 60000 });
}

describe('production smoke', () => {
  it('menu, Free mode, buy, next week, save, reload, continue', async () => {
    const context = await browser.newContext({ viewport: { width: 1200, height: 900 }, serviceWorkers: 'block' });
    const page = await context.newPage();
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.goto(url);
    await startFree(page);
    await page.click('#next');
    await waitDate(page, 'Year 1, Week 2');
    const row = page.locator('#market-table tbody tr').filter({ has: page.locator('td:nth-child(4):not(:text-is("0")):not(:text-is("1"))') }).first();
    await row.locator('button').click();
    await page.fill('#qty', '1');
    await page.click('#buy');
    await page.waitForFunction(() => !document.getElementById('trade-confirm')!.hidden);
    await page.click('#confirm');
    await page.waitForFunction(() => document.querySelector('#log .message')?.textContent?.startsWith('Order for this week'));
    await page.click('#next');
    await waitDate(page, 'Year 1, Week 3');
    await page.click('#save');
    await page.reload();
    await page.waitForSelector('#continue:not([disabled])');
    await page.click('#continue');
    await waitDate(page, 'Year 1, Week 3');
    expect(errors).toEqual([]);
    await context.close();
  }, 180000);

  it('serves an installable manifest', async () => {
    const context = await browser.newContext({ serviceWorkers: 'block' });
    const page = await context.newPage();
    await page.goto(url);
    const href = await page.getAttribute('link[rel=manifest]', 'href');
    const manifest = (await (await page.request.get(new URL(href!, url).href)).json()) as {
      display: string;
      start_url: string;
      icons: { src: string; sizes: string }[];
    };
    expect(manifest.display).toBe('standalone');
    expect(manifest.icons.map((i) => i.sizes)).toEqual(expect.arrayContaining(['192x192', '512x512']));
    for (const icon of manifest.icons) expect((await page.request.get(new URL(icon.src, url).href)).ok()).toBe(true);
    await context.close();
  });

  it('plays offline after one visit, from a versioned cache', async () => {
    const context = await browser.newContext({ viewport: { width: 1200, height: 900 } });
    const page = await context.newPage();
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.goto(url);
    await page.evaluate(() => navigator.serviceWorker.ready.then(() => undefined));
    // Wait until the install finished and the precache holds the wasm and the data.
    await page.waitForFunction(async () => {
      const names = (await caches.keys()).filter((n) => n.startsWith('fes-'));
      if (names.length !== 1) return false;
      const keys = (await (await caches.open(names[0]!)).keys()).map((r) => r.url);
      return keys.some((k) => k.endsWith('.wasm')) && keys.some((k) => k.endsWith('.csv')) && keys.some((k) => /assets\/worker-.*\.js$/.test(k));
    }, null, { timeout: 60000 });
    await page.reload(); // now controlled by the worker
    await page.waitForFunction(() => navigator.serviceWorker.controller !== null);

    await context.setOffline(true);
    await page.reload();
    await startFree(page);
    await page.click('#next');
    await waitDate(page, 'Year 1, Week 2');
    expect(errors).toEqual([]);
    await context.close();
  }, 180000);
});
