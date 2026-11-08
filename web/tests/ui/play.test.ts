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

async function nextWeek(page: Page, date: string): Promise<void> {
  await page.click('#next');
  await page.waitForFunction((d) => document.getElementById('date')?.textContent === d, date, { timeout: 60000 });
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

describe('trading screens', () => {
  it('previews, refuses with the simulation message, orders and refreshes after the turn', async () => {
    const { page, errors } = await game();
    await page.waitForSelector('#market-table tbody tr');
    expect(await page.textContent('#market-title')).toMatch(/^Market - /);
    const first = page.locator('#market-table tbody tr').first();
    await first.locator('button').click();
    expect(await page.textContent('#trade-good')).toBe(await first.locator('button').textContent());
    await page.click('#sell');
    await page.waitForFunction(() => !document.getElementById('trade-error')!.hidden);
    expect(await page.textContent('#trade-error')).toBe('The cart holds none of this good.');
    expect(await page.isHidden('#trade-confirm')).toBe(true);

    // Markets open empty; after one week there is stock to buy.
    await nextWeek(page, 'Year 1, Week 2');
    const row = page.locator('#market-table tbody tr').filter({ has: page.locator('td:nth-child(4):not(:text-is("0")):not(:text-is("1"))') }).first();
    const good = await row.getAttribute('data-good');
    const name = await row.locator('button').textContent();
    await row.locator('button').click();
    await page.fill('#qty', '2');
    await page.click('#buy');
    await page.waitForFunction(() => !document.getElementById('trade-confirm')!.hidden);
    expect(await page.isHidden('#trade-error')).toBe(true);
    expect(await page.textContent('#trade-preview')).toMatch(/^Buy 2 .* at \d+ each: \d+ coins\.$/);
    await page.click('#confirm');
    await page.waitForFunction(() => document.querySelector('#log .message')?.textContent?.startsWith('Order for this week'));
    const coinsBefore = await page.textContent('#coins');

    await nextWeek(page, 'Year 1, Week 3');
    await page.waitForFunction((g) => document.querySelector(`#market-table tr[data-good="${g}"] td:last-child`)?.textContent === '2', good);
    expect(await page.textContent('#cart')).toContain(name!);
    expect(await page.textContent('#coins')).not.toBe(coinsBefore);
    expect(errors).toEqual([]);
  }, 120000);

  it('fits 360 px with the map and panels and no sideways scroll', async () => {
    const { page } = await game(360);
    await page.waitForSelector('#market-table tbody tr');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= 360)).toBe(true);
  }, 120000);
});

