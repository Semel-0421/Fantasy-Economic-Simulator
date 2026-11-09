import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { mkdirSync } from 'node:fs';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  mkdirSync('test-results/ui', { recursive: true });
  server = await createServer({ server: { port: 4180, strictPort: true }, logLevel: 'error' });
  await server.listen();
  url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

async function start(scenario: boolean): Promise<Page> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  await page.goto(url);
  await page.click(scenario ? '#scenario-1' : '#free-mode');
  await page.waitForSelector('#map canvas');
  if (scenario) await page.click('#dialog-ok');
  return page;
}

describe('desktop shell', () => {
  it('opens a scenario on Goal and supports tab arrow, Home and End keys', async () => {
    const page = await start(true);
    expect(await page.locator('#tab-goal[aria-selected="true"]').count()).toBe(1);
    expect(await page.textContent('#date')).toBe('Week 1 / 20');
    await page.focus('#tab-goal');
    await page.keyboard.press('ArrowRight');
    expect(await page.getAttribute('#tab-market', 'aria-selected')).toBe('true');
    expect(await page.locator('#panel-goal').isHidden()).toBe(true);
    expect(await page.locator('#tab-market').evaluate((node) => node === document.activeElement)).toBe(true);
    await page.keyboard.press('End');
    expect(await page.getAttribute('#tab-log', 'aria-selected')).toBe('true');
    await page.keyboard.press('Home');
    expect(await page.getAttribute('#tab-goal', 'aria-selected')).toBe('true');
    await page.close();
  });
  it('opens free mode on Market, hides Goal, and gives the map sixty percent', async () => {
    const page = await start(false);
    expect(await page.locator('#tab-market[aria-selected="true"]').count()).toBe(1);
    expect(await page.locator('#tab-goal').count()).toBe(0);
    const map = (await page.locator('#map').boundingBox())!;
    const main = (await page.locator('.play').boundingBox())!;
    expect(map.width / main.width).toBeGreaterThanOrEqual(0.6);
    expect(await page.locator('#map #travel-panel').count()).toBe(1);
    await page.screenshot({ path: 'test-results/ui/task-3-desktop.png' });
    await page.close();
  });
});
