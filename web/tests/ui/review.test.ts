import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { Label } from '../../src/map/labels';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  server = await createServer({ server: { port: 4185, strictPort: true }, logLevel: 'error' });
  await server.listen(); url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

async function start(width = 1024): Promise<Page> {
  const page = await browser.newPage({ viewport: { width, height: 800 } });
  await page.goto(url); await page.click('#scenario-1'); await page.click('#dialog-ok');
  if (await page.locator('#guide-skip').count()) await page.click('#guide-skip');
  await page.waitForSelector('#map canvas');
  await page.evaluate(async () => { await document.fonts.ready; });
  return page;
}

describe('Planner round 1', () => {
  it('renders every visible numeral in the legible face', async () => {
    const page = await start();
    const unreadable = await page.locator('body').evaluate((root) => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), bad: string[] = [];
      while (walker.nextNode()) {
        const node = walker.currentNode, parent = node.parentElement;
        if (!parent || !/\d/.test(node.textContent ?? '') || !parent.getBoundingClientRect().width || parent.closest('[hidden], .sr-only')) continue;
        if (!getComputedStyle(parent).fontFamily.includes('Atkinson Hyperlegible')) bad.push(`${parent.tagName}#${parent.id}: ${node.textContent?.slice(0, 50)}`);
      }
      return bad;
    });
    expect(unreadable).toEqual([]);
    await page.close();
  });
  it('matches actual rendered label bounds at fitted and zoomed camera scales', async () => {
    const page = await start();
    for (let zoom = 0; zoom < 2; zoom++) {
      const data = await page.evaluate(() => {
        const map = (window as unknown as { __fesMap: { labelBoxes(): Label[]; renderedLabelBoxes(): Label[] } }).__fesMap;
        return { reported: map.labelBoxes(), drawn: map.renderedLabelBoxes() };
      });
      expect(data.drawn.length).toBeGreaterThan(0);
      for (const box of data.reported) {
        const drawn = data.drawn.find((label) => label.id === box.id)!;
        for (const key of ['x', 'y', 'w', 'h'] as const) expect(Math.abs(drawn[key] - box[key]), `zoom ${zoom}, ${box.id}.${key}: drawn ${drawn[key]}, reported ${box[key]}`).toBeLessThanOrEqual(1);
        expect(drawn.h).toBeGreaterThanOrEqual(12);
      }
      await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
      await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
    }
    await page.close();
  });
  it('fits complete desktop tab words at 900px', async () => {
    const page = await start(900);
    const overflow = await page.locator('[role="tab"]').evaluateAll((nodes) => nodes.filter((node) => {
      const box = node.getBoundingClientRect(); if (!box.width) return false;
      const range = document.createRange(); range.selectNodeContents(node);
      const text = range.getBoundingClientRect();
      return text.left < box.left + 2 || text.right > box.right - 2 || node.scrollWidth > node.clientWidth;
    }).map((node) => node.textContent));
    expect(overflow).toEqual([]);
    await page.close();
  });
  it('starts with compact travel and expands on request or selection', async () => {
    const page = await start();
    expect(await page.locator('#travel-panel[open]').count()).toBe(0);
    expect((await page.locator('#travel-panel').boundingBox())!.height).toBeLessThanOrEqual(64);
    await page.click('#travel-summary');
    expect(await page.locator('#routes').isVisible()).toBe(true);
    await page.click('#travel-summary');
    await page.evaluate(() => (window as unknown as { __fesMap: { select(id: number): void } }).__fesMap.select(80));
    const route = page.locator('#routes button').first();
    await page.click('#travel-summary'); await route.click();
    expect(await page.locator('#travel-panel[open]').count()).toBe(1);
    await page.close();
  });
  it('draws objective states and keeps one main heading during growth', async () => {
    const page = await start();
    expect(await page.textContent('#objectives')).not.toContain('[ ]');
    expect(await page.locator('#objectives .objective-mark').count()).toBe(3);
    expect(await page.getByRole('heading', { level: 1 }).count()).toBe(1);
    await page.evaluate(`(async () => {
      const { presentGrowth } = await import('/src/ui/growth.ts');
      void presentGrowth({ coins: document.getElementById('coins'), game: document.querySelector('.game') }, 500, 1000, [{ label: 'Sale', coins: 500 }]);
    })()`);
    expect(await page.getByRole('main').count()).toBe(1);
    expect(await page.getByRole('heading', { level: 1 }).count()).toBe(1);
    await page.keyboard.press('Escape'); await page.close();
  });
});
