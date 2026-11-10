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
  it('switches tool credits automatically for a fake allowlisted present-file list', async () => {
    const page = await browser.newPage();
    try {
      await page.goto(url); await page.waitForSelector('#credits');
      await page.evaluate(`(async () => {
        const { loadAssets } = await import('/src/assets/loader.ts');
        await loadAssets(['goods/wheat.png', 'audio/sfx/coin.ogg'], {
          image: async () => ({ source: { scaleMode: 'nearest' } }), audio: async () => ({})
        });
      })()`);
      await page.click('#credits');
      const text = await page.locator('main').innerText();
      expect(text).toContain('Art made with SpriteCook.');
      expect(text).toContain('Music and sound effects made with ElevenLabs.');
      expect(text).not.toContain('placeholders drawn in code');
    } finally { await page.close(); }
  });
  it('sets reduced-motion growth to the final coins immediately and keeps only the badge', async () => {
    const page = await browser.newPage({ reducedMotion: 'reduce' });
    await page.goto(url); await page.click('#free-mode'); await page.waitForSelector('#map canvas');
    await page.evaluate(`(async () => {
      const { presentGrowth } = await import('/src/ui/growth.ts');
      await presentGrowth({ coins: document.getElementById('coins'), game: document.querySelector('.game') }, 500, 2500, [{ label: 'Dividend', coins: 2000 }]);
    })()`);
    expect(await page.textContent('#coins')).toBe('2,500');
    expect(await page.locator('.game[inert], .panel.growing').count()).toBe(0);
    expect(await page.getAttribute('#growth-announcement', 'aria-live')).toBe('polite');
    expect(await page.getAttribute('#growth', 'title')).toContain('Dividend');
    await page.waitForSelector('#growth', { state: 'detached', timeout: 2500 });
    await page.close();
  });
  it('skips growth by key at the exact final total and releases the game', async () => {
    const page = await browser.newPage({ reducedMotion: 'no-preference' });
    await page.goto(url); await page.click('#free-mode'); await page.waitForSelector('#map canvas');
    await page.evaluate(`(async () => {
      const { presentGrowth } = await import('/src/ui/growth.ts');
      window.growthDone = false;
      void presentGrowth({ coins: document.getElementById('coins'), game: document.querySelector('.game') }, 500, 123456, [{ label: 'Sale abroad', coins: 122956 }]).then(() => { window.growthDone = true; });
    })()`);
    expect(await page.locator('.game[inert]').count()).toBe(1);
    await page.waitForFunction(() => document.getElementById('growth-announcement')?.textContent?.includes('Sale abroad'));
    expect(await page.locator('[aria-live="polite"]').evaluateAll((nodes) => nodes.filter((node) => !node.closest('[inert]') && node.textContent?.includes('Sale abroad')).length)).toBe(1);
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => (window as unknown as { growthDone: boolean }).growthDone);
    expect(await page.textContent('#coins')).toBe('123,456');
    expect(await page.locator('.game[inert]').count()).toBe(0);
    await page.close();
  });
  it('uses the Planner menu pitches and keeps profit targets equal to scenario data', () => {
    expect(SCENARIOS.map((entry) => entry.pitch)).toEqual([
      'Learn to trade: buy cheap, sell dear, deliver a contract.',
      'Grow coins through company shares and bonds.',
      'Licences and trade between nations.',
    ]);
    for (const entry of SCENARIOS) {
      const text = readFileSync(`../data/scenarios/${entry.file}`, 'utf8');
      const targets = [...text.matchAll(/goal:\s*NetProfit\((\d+)\)/g)].map((match) => Number(match[1]));
      expect(entry.targetProfit).toBe(Math.max(...targets));
    }
  });
  it('credits every runtime dependency, including bundled font licences', async () => {
    const deps = (JSON.parse(readFileSync('package.json', 'utf8')) as { dependencies: Record<string, string> }).dependencies;
    expect(Object.keys(BUNDLED_LICENSES).sort()).toEqual(Object.keys(deps).sort());
    const page = await browser.newPage();
    await page.goto(url);
    await page.waitForSelector('#credits');
    expect(await page.locator('#credits').count()).toBe(1);
    await page.screenshot({ path: 'test-results/ui/task-6-menu.png' });
    await page.click('#credits');
    const { version } = JSON.parse(readFileSync('package.json', 'utf8')) as { version: string };
    expect(await page.locator('.credits h1 + p').textContent()).toBe(`v${version}`);
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
