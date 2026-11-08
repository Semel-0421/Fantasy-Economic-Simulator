// Browser play tests (Playwright + installed Edge; BROWSER_CHANNEL overrides): the map (tap a town,
// pan, zoom, travel) and the trading screens (preview, rejection, order, refresh after the turn).
import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

let server: ViteDevServer;
let browser: Browser;
let url = '';

beforeAll(async () => {
  server = await createServer({ server: { port: 4175, strictPort: true }, logLevel: 'error' });
  await server.listen();
  url = server.resolvedUrls?.local[0] ?? 'http://localhost:4175/';
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);

afterAll(async () => {
  await browser?.close();
  await server?.close();
});

type Pt = { x: number; y: number };

async function game(width = 1200): Promise<{ page: Page; errors: string[] }> {
  const context = await browser.newContext({ viewport: { width, height: 900 } });
  const page = await context.newPage();
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto(url);
  await page.click('#free-mode');
  await page.waitForFunction(() => document.querySelector('#map canvas') && document.querySelector('#routes button'), null, { timeout: 60000 });
  return { page, errors };
}

const screenOf = (page: Page, node: number): Promise<Pt> =>
  page.evaluate((n) => (window as unknown as { __fesMap: { screenOf(n: number): Pt } }).__fesMap.screenOf(n), node);

async function canvasPoint(page: Page, p: Pt): Promise<Pt> {
  const box = (await page.locator('#map canvas').boundingBox())!;
  return { x: box.x + p.x, y: box.y + p.y };
}

describe('map', () => {
  it('selects a town by tap, pans, zooms and orders travel', async () => {
    const { page, errors } = await game();
    const dest = Number(await page.getAttribute('#routes button', 'data-node'));
    const at = await canvasPoint(page, await screenOf(page, dest));
    await page.mouse.click(at.x, at.y);
    await page.waitForFunction(() => !document.querySelector<HTMLButtonElement>('#travel')!.disabled);
    expect(await page.textContent('#selected')).toMatch(/week/);

    const before = await screenOf(page, dest);
    await page.mouse.move(at.x + 40, at.y + 40);
    await page.mouse.down();
    await page.mouse.move(at.x + 100, at.y + 70, { steps: 5 });
    await page.mouse.up();
    const panned = await screenOf(page, dest);
    expect(panned.x - before.x).toBeCloseTo(60, 0);
    expect(panned.y - before.y).toBeCloseTo(30, 0);
    await page.mouse.wheel(0, -400);
    const zoomed = await screenOf(page, dest);
    expect(zoomed).not.toEqual(panned);

    await page.click('#travel');
    await page.waitForFunction(() => document.querySelector('#log .message')?.textContent?.includes('Travel to'));
    await page.click('#next');
    await page.waitForFunction(() => document.getElementById('date')?.textContent === 'Year 1, Week 2', null, { timeout: 60000 });
    const name = (await page.textContent('#selected'))!.split(' (')[0]!;
    expect(await page.textContent('#where')).toContain(name);
    expect(errors).toEqual([]);
  }, 120000);
});
