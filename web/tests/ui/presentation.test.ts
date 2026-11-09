import { readFileSync, mkdirSync } from 'node:fs';
import { chromium, type Browser } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { BUNDLED_LICENSES } from '../../src/ui/credits';
import { SCENARIOS } from '../../src/data/scenarios';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  mkdirSync('test-results/ui', { recursive: true });
  server = await createServer({ server: { port: 4181, strictPort: true }, logLevel: 'error' });
  await server.listen(); url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

describe('presentation', () => {
  it('keeps menu copy and profit targets equal to the approved scenario data', () => {
    for (const entry of SCENARIOS) {
      const text = readFileSync(`../data/scenarios/${entry.file}`, 'utf8');
      expect(text).toContain(entry.pitch);
      const targets = [...text.matchAll(/goal:\s*NetProfit\((\d+)\)/g)].map((match) => Number(match[1]));
      expect(entry.targetProfit).toBe(Math.max(...targets));
    }
  });
  it('credits every runtime dependency, including bundled font licences', async () => {
    const deps = (JSON.parse(readFileSync('package.json', 'utf8')) as { dependencies: Record<string, string> }).dependencies;
    expect(Object.keys(BUNDLED_LICENSES).sort()).toEqual(Object.keys(deps).sort());
    const page = await browser.newPage();
    await page.goto(url);
    expect(await page.locator('#credits').count()).toBe(1);
    await page.screenshot({ path: 'test-results/ui/task-6-menu.png' });
    await page.click('#credits');
    const text = await page.textContent('main');
    for (const licence of Object.values(BUNDLED_LICENSES)) expect(text).toContain(licence.name);
    expect(text).toContain('SIL Open Font License');
    expect(text).toContain('MIT');
    expect(text).toContain('Art and sound: placeholders drawn in code.');
    await page.screenshot({ path: 'test-results/ui/task-6-credits.png' });
    await page.close();
  });
  it('traps dialog focus, closes on Escape, and returns focus', async () => {
    const page = await browser.newPage();
    await page.goto(url);
    await page.click('#free-mode');
    await page.waitForSelector('#map canvas');
    await page.evaluate(`(async () => {
      const { dialogs } = await import('/src/ui/dialog.ts');
      document.getElementById('next').focus();
      void dialogs(document.body, () => document.querySelector('.game')).show([{ title: 'News', text: 'Test news.' }]);
    })()`);
    await page.keyboard.press('Tab');
    expect(await page.locator('#dialog-ok').evaluate((node) => node === document.activeElement)).toBe(true);
    await page.keyboard.press('Shift+Tab');
    expect(await page.locator('#dialog-ok').evaluate((node) => node === document.activeElement)).toBe(true);
    await page.keyboard.press('Escape');
    expect(await page.locator('#dialog').count()).toBe(0);
    expect(await page.locator('#next').evaluate((node) => node === document.activeElement)).toBe(true);
    await page.close();
  });
  it('renders net profit as the score, with the target and ratio', async () => {
    const page = await browser.newPage();
    await page.goto(url); await page.waitForSelector('#free-mode');
    await page.evaluate(`(async () => {
      const { renderResults } = await import('/src/ui/results.ts');
      renderResults(document.getElementById('app'), 'First Venture', {
        revenue: 3200, costs: { purchases: 1000, travel: 60, protection: 0, losses: 0, other: 0 },
        net_profit: 2140, start_coins: 500, end_coins: 2640, holdings: 0, weeks: 20,
      }, [], { menu() {} }, 1000);
    })()`);
    expect(await page.textContent('#score')).toContain('Net profit 2,140');
    expect(await page.textContent('#results-target')).toContain('Target 1,000 · You 2,140');
    expect(await page.textContent('#results-target')).toContain('2.1x');
    await page.screenshot({ path: 'test-results/ui/task-6-results.png' });
    await page.close();
  });
});
