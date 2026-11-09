import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { mkdirSync } from 'node:fs';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  mkdirSync('test-results/ui', { recursive: true });
  server = await createServer({ server: { port: 4182, strictPort: true }, logLevel: 'error' });
  await server.listen(); url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });
async function start(page: Page): Promise<void> {
  await page.click('#scenario-1'); await page.waitForSelector('#dialog'); await page.click('#dialog-ok');
}
describe('guide in play', () => {
  it('uses the approved steps and remains skipped on the next start', async () => {
    const page = await browser.newPage({ viewport: { width: 360, height: 740 } });
    await page.goto(url); await start(page);
    expect(await page.locator('#guide').count()).toBe(1);
    for (const title of ['Your purse', 'The market', 'Choose where to sell', 'End your week']) {
      expect(await page.textContent('#guide-title')).toBe(title);
      if (title === 'Choose where to sell') await page.screenshot({ path: 'test-results/ui/task-8-guide-phone.png' });
      await page.click('#guide-next');
    }
    expect(await page.locator('#guide').count()).toBe(0);
    expect(await page.evaluate(() => localStorage.getItem('fes-guide-s1-done'))).toBe('1');
    await page.click('#menu'); await start(page);
    expect(await page.locator('#guide').count()).toBe(0);
    await page.click('#menu'); await page.click('.settings summary'); await page.click('#guide-replay');
    await page.waitForSelector('#dialog'); await page.click('#dialog-ok');
    await page.click('#guide-skip');
    expect(await page.evaluate(() => localStorage.getItem('fes-guide-s1-done'))).toBe('1');
    await page.close();
  });
  it('Escape skips and play continues when storage is blocked', async () => {
    const context = await browser.newContext();
    await context.addInitScript(() => Object.defineProperty(window, 'localStorage', { get() { throw new Error('Blocked storage'); } }));
    const page = await context.newPage(), errors: string[] = [];
    page.on('pageerror', (error) => errors.push(String(error)));
    await page.goto(url); await start(page);
    expect(await page.locator('#guide').count()).toBe(1);
    await page.keyboard.press('Escape');
    expect(await page.locator('#guide').count()).toBe(0);
    expect(await page.locator('.game[inert]').count()).toBe(0);
    await page.click('#next');
    await page.waitForFunction(() => document.getElementById('date')?.textContent === 'Week 2 / 20');
    expect(errors).toEqual([]);
    await context.close();
  });
  it('shows the supplied free-mode hint once and lets it be dismissed', async () => {
    const page = await browser.newPage();
    await page.goto(url); await page.click('#free-mode'); await page.waitForSelector('#map canvas');
    expect(await page.textContent('.free-hint')).toContain('Free mode: the full economy, no goals. Save your game any time.');
    await page.getByRole('button', { name: 'Dismiss hint' }).click();
    expect(await page.locator('.free-hint').count()).toBe(0);
    await page.click('#menu'); await page.click('#free-mode'); await page.waitForSelector('#map canvas');
    expect(await page.locator('.free-hint').count()).toBe(0);
    await page.close();
  });
});
