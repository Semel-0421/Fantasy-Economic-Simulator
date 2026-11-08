// Scenario golden paths in the browser (Playwright + installed Edge; BROWSER_CHANNEL overrides) on the
// full-scale world. Every order is placed by clicking the real panels (market, travel, exchange, bonds,
// licences), as a player would; Next week, the news dialogs, Accept and the results screen too. The
// development handle `__fesGame` is only read, to find ids and check positions.
// Each run must end with the net profit the engine's golden test records (Plan 6 Tasks 4-6).
import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

let server: ViteDevServer;
let browser: Browser;
let url = '';

beforeAll(async () => {
  server = await createServer({ server: { port: 4177, strictPort: true }, logLevel: 'error' });
  await server.listen();
  url = server.resolvedUrls?.local[0] ?? 'http://localhost:4177/';
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 60000);

afterAll(async () => {
  await browser?.close();
  await server?.close();
});

type Game = { view(name: string, arg?: number): Promise<unknown> };
type Purse = { node: number; local_continent: number; coins: number[]; travel_destination: number };

class Player {
  readonly news: string[] = [];
  private nodes = new Map<string, number>();
  private goods = new Map<string, number>();
  constructor(readonly page: Page) {}

  async open(n: number): Promise<string> {
    await this.page.click(`#scenario-${n}`);
    await this.page.waitForSelector('#dialog', { timeout: 60000 });
    const intro = (await this.page.textContent('#dialog')) ?? '';
    expect(await this.page.locator('.game[inert]').count()).toBe(1); // the game takes no input behind it
    await this.page.click('#dialog-ok');
    await this.page.waitForSelector('#deadline');
    const [map, goods] = (await this.page.evaluate(async () => {
      const g = (window as unknown as { __fesGame: Game }).__fesGame;
      return [await g.view('map'), await g.view('goods')];
    })) as [{ nodes: { id: number; key: string }[] }, { id: number; key: string }[]];
    for (const x of map.nodes) this.nodes.set(x.key, x.id);
    for (const x of goods) this.goods.set(x.key, x.id);
    return intro;
  }

  node(key: string): number {
    return this.nodes.get(key) ?? -1;
  }

  /** The middle grade of a base good, as the scenario files and engine tests use. */
  good(key: string): number {
    return this.goods.get(`${key}@mid`) ?? -1;
  }

  /** Clicks `ask`, expects the preview's Confirm (or fails with the refusal), confirms, waits for the log. */
  private async place(ask: string, prefix: string, confirm = `#${prefix}-confirm`): Promise<void> {
    const before = await this.page.locator('#log li.message').count();
    await this.page.click(ask);
    const error = `#${prefix}-error`;
    await this.page.waitForSelector(`${confirm}:visible, ${error}:visible`);
    if (await this.page.locator(`${error}:visible`).count()) throw new Error(`refused: ${await this.page.textContent(error)}`);
    await this.page.click(confirm);
    await this.page.waitForFunction(
      (n) => document.querySelectorAll('#log li.message').length > n,
      before,
    );
  }

  /** Market panel: choose the good, the quantity, Buy or Sell, Confirm. */
  async trade(side: 'buy' | 'sell', good: number, quantity: number): Promise<void> {
    await this.page.click(`#market-table tr[data-good="${good}"] button`);
    await this.page.fill('#qty', String(quantity));
    await this.place(`#${side}`, 'trade', '#confirm');
  }

  /** Exchange panel: choose the company, the count, Buy or Sell, Confirm. */
  async shares(side: 'buy' | 'sell', facility: number, count: number): Promise<void> {
    await this.page.click(`#share-${facility}`);
    await this.page.fill('#share-count', String(count));
    await this.place(`#share-${side}`, 'share');
  }

  /** Bonds panel: all coins, in whole hundreds. */
  async bondsWithAllCoins(): Promise<number> {
    await this.page.click('#bond-all');
    const amount = Number(await this.page.inputValue('#bond-amount'));
    await this.place('#bond-buy', 'bond');
    return amount;
  }

  /** Licences panel: the international trade licence, or a licence to sell `good`. */
  async licence(good?: number): Promise<void> {
    if (good === undefined) return this.place('#licence-trade', 'licence');
    await this.page.selectOption('#licence-good', String(good));
    await this.place('#licence-buy', 'licence');
  }

  /** Travel panel: the route button, guards and insurance, Travel (no Confirm: it queues at once). */
  async travel(key: string, protectedTrip: boolean): Promise<void> {
    await this.page.click(`#routes button[data-node="${this.node(key)}"]`);
    await this.page.setChecked('#guards', protectedTrip);
    await this.page.setChecked('#insurance', protectedTrip);
    const before = await this.page.locator('#log li.message').count();
    await this.page.click('#travel');
    await this.page.waitForFunction(
      (n) => document.querySelectorAll('#log li.message').length > n || !document.getElementById('travel-error')?.hidden,
      before,
    );
    if (await this.page.locator('#travel-error:visible').count()) throw new Error(`refused: ${await this.page.textContent('#travel-error')}`);
  }

  async view<T>(name: string): Promise<T> {
    return (await this.page.evaluate((v) => (window as unknown as { __fesGame: Game }).__fesGame.view(v), name)) as T;
  }

  async coins(): Promise<number> {
    const p = await this.view<Purse>('purse');
    return p.coins[p.local_continent] ?? 0;
  }

  async held(good: number): Promise<number> {
    const cargo = await this.view<{ cart: [number, number][] }>('cargo');
    return cargo.cart.find(([g]) => g === good)?.[1] ?? 0;
  }

  /** Presses Next week and dismisses its news; returns false once the results screen is up. */
  async week(): Promise<boolean> {
    const before = await this.page.textContent('#deadline');
    await this.page.click('#next');
    const settled = (prev: string | null): Promise<unknown> =>
      this.page.waitForFunction(
        (p) =>
          document.getElementById('results') ||
          document.getElementById('dialog') ||
          (document.getElementById('deadline')?.textContent !== p && !(document.getElementById('next') as HTMLButtonElement).disabled),
        prev,
        { timeout: 60000 },
      );
    await settled(before);
    while (await this.page.locator('#dialog').count()) {
      this.news.push((await this.page.textContent('#dialog')) ?? '');
      await this.page.click('#dialog-ok');
      await settled(before);
    }
    if (await this.page.locator('#results').count()) return false;
    const weeks = (await this.view<{ weeks: number } | null>('results'))?.weeks ?? 0;
    const info = await this.view<{ weeks: number }>('scenario');
    if (weeks >= info.weeks) {
      await this.page.waitForSelector('#results', { timeout: 60000 });
      return false;
    }
    return true;
  }

  async weeksPlayed(): Promise<number> {
    return (await this.view<{ weeks: number } | null>('results'))?.weeks ?? 0;
  }

  async until(w: number): Promise<void> {
    while ((await this.weeksPlayed()) < w) await this.week();
  }

  async go(key: string, protectedTrip: boolean): Promise<void> {
    await this.travel(key, protectedTrip);
    await this.week();
    while ((await this.view<Purse>('purse')).travel_destination >= 0) await this.week();
  }

  async finish(target: number): Promise<string> {
    while (await this.week());
    const score = (await this.page.textContent('#score')) ?? '';
    const profit = Number(/Net profit ([\d,]+)/.exec(score)?.[1]?.replaceAll(',', ''));
    expect(profit).toBeGreaterThanOrEqual(target * 1.3);
    return score;
  }
}

async function player(): Promise<{ p: Player; errors: string[] }> {
  const context = await browser.newContext({ viewport: { width: 1200, height: 900 } });
  const page = await context.newPage();
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto(url);
  await page.waitForSelector('#scenario-1');
  return { p: new Player(page), errors };
}

const savesWritten = (page: Page): Promise<number> =>
  page.evaluate(
    () =>
      new Promise<number>((resolve) => {
        const req = indexedDB.open('fes-saves');
        req.onsuccess = () => {
          const db = req.result;
          const names = [...db.objectStoreNames];
          if (names.length === 0) return resolve(0);
          const count = db.transaction(names[0] as string).objectStore(names[0] as string).count();
          count.onsuccess = () => resolve(count.result);
          count.onerror = () => resolve(-1);
        };
        req.onerror = () => resolve(-1);
      }),
  );

describe('scenario golden paths', () => {
  it('menu offers the three scenarios before Free mode', async () => {
    const { p, errors } = await player();
    const labels = await p.page.locator('.scenario').allTextContents();
    expect(labels).toEqual(['Scenario 1: First Venture', 'Scenario 2: Shares and Bonds', 'Scenario 3: Licences and Trade Abroad']);
    expect(await p.page.locator('.scenario:disabled').count()).toBe(0);
    expect(errors).toEqual([]);
  });

  it('scenario 1: grain to town, the tool contract under guard, results, then scenario 2', async () => {
    const { p, errors } = await player();
    expect(await p.open(1)).toContain('500 coins');
    expect(await p.page.textContent('#deadline')).toBe('Week 1 of 20 - 20 weeks left');
    expect(await p.page.textContent('#date')).toBe('Week 1'); // the header counts scenario weeks too
    expect(await p.page.locator('#save').count()).toBe(0); // scenarios never save
    const grain = p.good('g1_0');
    const tools = p.good('g6_0');
    await p.trade('buy', grain, 30);
    await p.go('k0n0r0c0', false);
    await p.trade('sell', grain, 30);
    await p.week();
    const deadline = (await p.page.textContent('#deadline')) ?? '';
    expect(deadline.startsWith(`${await p.page.textContent('#date')} of 20`)).toBe(true);
    expect(p.news.some((n) => n.includes('Deliver 18 crates of g6_0 to k0n0r0c1 by week 12.'))).toBe(true);
    await p.page.click('#accept-tools');
    await p.page.waitForFunction(() => document.querySelector('#scenario-contracts')?.textContent?.includes('Accepted'));
    await p.trade('buy', tools, 18);
    await p.go('k0n0r0', false);
    await p.go('k0n0r0c1', true);
    await p.week();
    expect(await p.page.textContent('#objectives')).toContain('[x] Deliver the tool order');
    expect(p.news.some((n) => n.includes('Delivery complete. Promised 1500 coins; you received 1500 coins.'))).toBe(true);
    expect(p.news.some((n) => n.includes('You receive 1,500 coins.'))).toBe(true);
    expect(await p.finish(1000)).toBe('Net profit 1,359 coins in 20 weeks');
    expect(await p.page.textContent('#statement')).toContain('Licences, interest and other');
    expect(await p.page.locator('#results button').allTextContents()).toEqual(['Next scenario', 'Main menu']);
    expect(await savesWritten(p.page)).toBe(0);
    await p.page.click('#next-scenario');
    await p.page.waitForSelector('#dialog', { timeout: 60000 });
    expect(await p.page.textContent('#dialog')).toContain('Shares and Bonds');
    expect(errors).toEqual([]);
  }, 600000);

  it('scenario 2: shares before the orders, sold on the rumour, then bonds', async () => {
    const { p, errors } = await player();
    await p.open(2);
    await p.shares('buy', 130, 49);
    await p.until(7);
    expect(p.news.some((n) => n.includes('490 coins'))).toBe(true); // the dividend, paid on screen
    await p.shares('sell', 130, 49);
    await p.until(9);
    const coins = await p.coins();
    expect(await p.bondsWithAllCoins()).toBe(Math.floor(coins / 100) * 100);
    expect(await p.finish(2500)).toBe('Net profit 3,657 coins in 22 weeks');
    expect(p.news.some((n) => n.includes('Review your bonds before the week 17 coupon.'))).toBe(true);
    expect(p.news.some((n) => n.includes('Two weeks until the special 8% coupon.'))).toBe(true);
    expect(await p.page.textContent('#statement')).toContain('Shares and bonds held');
    expect(await savesWritten(p.page)).toBe(0);
    expect(errors).toEqual([]);
  }, 600000);

  it('scenario 3: licences, the guarded trip abroad and home in time', async () => {
    const { p, errors } = await player();
    await p.open(3);
    const g = p.good('g19_0');
    await p.licence();
    await p.trade('buy', g, 60);
    await p.week();
    for (const to of ['k0n0', 'k0', 'k0n1']) await p.go(to, true);
    await p.licence(g);
    for (let left = await p.held(g); left > 0; left = await p.held(g)) {
      await p.trade('sell', g, left);
      await p.week();
    }
    for (const to of ['k0', 'k0n0', 'k0n0r0']) await p.go(to, false);
    expect(await p.finish(1000)).toBe('Net profit 2,263 coins in 24 weeks');
    expect(await p.page.locator('#next-scenario').count()).toBe(0); // the last scenario
    await p.page.click('#results-menu');
    await p.page.waitForSelector('#scenario-1');
    expect(await savesWritten(p.page)).toBe(0);
    expect(errors).toEqual([]);
  }, 600000);
});
