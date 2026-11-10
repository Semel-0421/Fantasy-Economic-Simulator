import { resolve } from 'node:path';
import { chromium, type Browser } from 'playwright';
import { build, preview, type PreviewServer } from 'vite';
import { afterAll, beforeAll, expect, it } from 'vitest';

let browser: Browser, server: PreviewServer, url: string;
beforeAll(async () => {
  process.env.NODE_ENV = 'production';
  await build({ mode: 'production', logLevel: 'silent', build: { outDir: 'dist-e2e', rollupOptions: { input: resolve('index.html') } } });
  server = await preview({ build: { outDir: 'dist-e2e' }, preview: { port: 4190, strictPort: true } });
  url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 180000);
afterAll(async () => {
  await browser?.close();
  await new Promise<void>(done => server ? server.httpServer.close(() => done()) : done());
});

it('loads no non-English language chunk or optional font, including service-worker precaching', async () => {
  const context = await browser.newContext();
  const requests: string[] = [];
  context.on('request', request => requests.push(request.url()));
  try {
    const page = await context.newPage();
    await page.goto(url); await page.waitForSelector('#free-mode');
    await page.evaluate(async () => { await navigator.serviceWorker.ready; });
    await page.reload(); await page.waitForSelector('#free-mode');
    expect(await page.getAttribute('html', 'lang')).toBe('en');
    expect(requests.filter(path => /language-[^/]+\.js|Galmuri11|pixelify-sans.*\.woff$/.test(path))).toEqual([]);
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--font-display'))).toContain('Pixelify Sans');
  } finally { await context.close(); }
}, 60000);

it('loads only Korean language data/font before rendering, then serves it offline', async () => {
  const context = await browser.newContext();
  const requests: string[] = [];
  context.on('request', request => requests.push(request.url()));
  await context.addInitScript(() => localStorage.setItem('fes-lang', 'ko'));
  try {
    const page = await context.newPage();
    await page.goto(url); await page.waitForSelector('#scenario-1');
    expect(await page.textContent('#scenario-1')).toContain('첫 장사');
    expect(await page.evaluate(() => [...document.fonts].some(face => face.family.includes('FES Galmuri11') && face.status === 'loaded'))).toBe(true);
    await page.waitForFunction(async () => {
      const names = await caches.keys();
      const cache = await caches.open(names.find(name => name.startsWith('fes-')) ?? '');
      const urls = (await cache.keys()).map(request => request.url);
      return urls.some(path => /language-ko-/.test(path)) && urls.some(path => /Galmuri11/.test(path));
    }, null, { timeout: 30000 });
    expect(requests.filter(path => /language-[^/]+\.js/.test(path) && !/language-ko-/.test(path))).toEqual([]);
    await page.reload(); await page.waitForSelector('#scenario-1');
    await context.setOffline(true);
    await page.reload(); await page.waitForSelector('#scenario-1');
    await page.click('#scenario-1'); await page.waitForSelector('#dialog');
    expect(await page.locator('#dialog').innerText()).toContain('밀브룩');
    expect(await page.locator('#dialog').innerText()).not.toContain('Millbrook');
  } finally { await context.close(); }
}, 60000);
