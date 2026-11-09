import { chromium, type Browser } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, expect, it } from 'vitest';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  server = await createServer({ server: { port: 4191, strictPort: true }, logLevel: 'error' });
  await server.listen();
  url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

it('shows names on all three scenario starts and the results fixture', async () => {
  for (const n of [1, 2, 3]) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      await page.addInitScript(() => localStorage.setItem('fes-guide-s1-done', '1'));
      await page.goto(url);
      await page.click(`#scenario-${n}`);
      await page.waitForSelector('#dialog', { timeout: 60000 });
      expect(await page.locator('body').innerText()).not.toMatch(/\bk\d+(?:n\d+)?|\bg\d+_\d+\b/);
      await page.click('#dialog-ok');
      if (await page.locator('#guide-skip').count()) await page.click('#guide-skip');
      for (const tab of ['goal', 'market', 'cargo', 'finance', 'log']) {
        await page.click(`#tab-${tab}`);
        expect(await page.locator('body').innerText()).not.toMatch(/\bk\d+(?:n\d+)?|\bg\d+_\d+\b/);
      }
      await page.goto(new URL('tests/e2e/fixtures/ui.html', url).href);
      await page.waitForSelector('#results');
      expect(await page.locator('body').innerText()).not.toMatch(/\bk\d+(?:n\d+)?|\bg\d+_\d+\b/);
    } finally { await context.close(); }
  }
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await page.goto(url);
    await page.click('#free-mode');
    await page.waitForSelector('#map canvas');
    expect(await page.getAttribute('#coins', 'title')).toContain('Aurelia:');
  } finally { await context.close(); }
}, 120000);
