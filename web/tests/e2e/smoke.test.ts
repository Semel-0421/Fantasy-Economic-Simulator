// Production-build smoke (Playwright, `vite preview` of a fresh build; BROWSER_CHANNEL picks the browser,
// default the installed Edge, `chromium` in CI): menu -> Free mode -> buy -> Next week -> save -> reload ->
// continue, plus the PWA: installable manifest and playing offline after one visit.
import { cpSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import { tmpdir } from 'node:os';
import { extname, join } from 'node:path';
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

  it('keeps a running tab on its build when a new build is deployed, and offers a reload', async () => {
    const root = mkdtempSync(join(tmpdir(), 'fes-update-'));
    const site = join(root, 'site');
    cpSync('dist-e2e', site, { recursive: true });
    const types: Record<string, string> = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.csv': 'text/csv', '.png': 'image/png', '.webmanifest': 'application/manifest+json' };
    const http: Server = createServer((req, res) => {
      const path = decodeURIComponent((req.url ?? '/').split('?')[0]!).split('/').filter((part) => part && part !== '..').join('/');
      let file = join(site, path);
      if (!path || path.endsWith('/') || !existsSync(file)) file = join(site, 'index.html');
      try {
        res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream', 'cache-control': 'no-cache' });
        res.end(readFileSync(file));
      } catch {
        res.writeHead(404).end();
      }
    });
    await new Promise<void>((resolve) => http.listen(4178, '127.0.0.1', resolve));
    const context = await browser.newContext({ viewport: { width: 1200, height: 900 } });
    try {
      const page = await context.newPage();
      const errors: string[] = [];
      page.on('pageerror', (e) => errors.push(String(e)));
      await page.goto('http://127.0.0.1:4178/');
      await page.waitForFunction(async () => {
        const names = (await caches.keys()).filter((n) => n.startsWith('fes-'));
        return names.length === 1 && (await (await caches.open(names[0]!)).keys()).some((r) => r.url.endsWith('.wasm'));
      }, null, { timeout: 60000 });
      await page.evaluate(() => navigator.serviceWorker.ready.then(() => undefined));
      await page.reload(); // build A, controlled and cached
      await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
      const cacheA = await page.evaluate(async () => (await caches.keys()).filter((n) => n.startsWith('fes-'))[0]);
      expect(await page.locator('#update-prompt').count()).toBe(0);

      // Deploy build B: the same files under a new worker and cache name.
      const sw = readFileSync(join(site, 'sw.js'), 'utf8');
      expect(sw).toContain(cacheA!);
      writeFileSync(join(site, 'sw.js'), sw.replace(cacheA!, 'fes-bbbbbbbbbbbb'));
      await page.evaluate(async () => (await navigator.serviceWorker.getRegistration())?.update());

      await page.waitForSelector('#update-prompt');
      expect(await page.textContent('#update-prompt')).toContain('A new version is ready');
      // The old tab still plays, and still runs on build A's cache.
      await startFree(page);
      await page.click('#next');
      await waitDate(page, 'Year 1, Week 2');
      const names = await page.evaluate(async () => (await caches.keys()).filter((n) => n.startsWith('fes-')).sort());
      expect(names).toEqual([cacheA, 'fes-bbbbbbbbbbbb'].sort());

      // Reload on request: build B takes over and build A's cache goes.
      await page.click('#update-reload');
      await page.waitForFunction(async () => (await caches.keys()).filter((n) => n.startsWith('fes-')).join() === 'fes-bbbbbbbbbbbb', null, { timeout: 30000 });
      await page.waitForSelector('#free-mode');
      expect(await page.locator('#update-prompt').count()).toBe(0);
      expect(errors).toEqual([]);
    } finally {
      await context.close();
      await new Promise<void>((resolve) => http.close(() => resolve()));
      rmSync(root, { recursive: true, force: true });
    }
  }, 180000);
});
