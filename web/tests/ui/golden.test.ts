// Scenario golden paths in the browser (Playwright + installed Edge; BROWSER_CHANNEL overrides) on the
// full-scale world. Orders go through the development handle `__fesGame` (the engine tests play the
// same orders); Next week, the news dialogs, Accept and the results screen are driven through the UI.
// Each run must end with the net profit the engine's golden test records (Plan 6 Tasks 4-6).
import { chromium, type Browser, type Page } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { PlayerCommand } from '../../src/sim/types';

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

type Game = {
  queue(c: PlayerCommand): Promise<void>;
  view(name: string, arg?: number): Promise<unknown>;
};
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

  async order(c: PlayerCommand): Promise<void> {
    await this.page.evaluate((cmd) => (window as unknown as { __fesGame: Game }).__fesGame.queue(cmd), c);
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
    await this.order({ Travel: { destination: this.node(key), guards: protectedTrip, insurance: protectedTrip } });
    await this.week();
    while ((await this.view<Purse>('purse')).travel_destination >= 0) await this.week();
  }

  async finish(): Promise<string> {
    while (await this.week());
    return (await this.page.textContent('#score')) ?? '';
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
    expect(await p.page.locator('#save').count()).toBe(0); // scenarios never save
    const grain = p.good('g1_0');
    const tools = p.good('g6_0');
    await p.order({ BuyGoods: { good: grain, quantity: 30, pay_from_deposit: false } });
    await p.go('k0n0r0c0', false);
    await p.order({ SellGoods: { good: grain, quantity: 30 } });
    await p.week();
    expect(p.news.some((n) => n.includes('18 crates of tools'))).toBe(true);
    await p.page.click('#accept-tools');
    await p.page.waitForFunction(() => document.querySelector('#scenario-contracts')?.textContent?.includes('Accepted'));
    await p.order({ BuyGoods: { good: tools, quantity: 18, pay_from_deposit: false } });
    await p.go('k0n0r0', false);
    await p.go('k0n0r0c1', true);
    await p.week();
    expect(await p.page.textContent('#objectives')).toContain('[x] Deliver the tool order');
    expect(await p.finish()).toBe('Net profit 1,059 coins in 20 weeks');
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
    await p.order({ BuyShare: { facility: 130, count: 49 } });
    await p.until(7);
    expect(p.news.some((n) => n.includes('490 coins'))).toBe(true); // the dividend, paid on screen
    await p.order({ SellShare: { facility: 130, count: 49 } });
    await p.until(9);
    await p.order({ BuyBond: { issuer: -1, amount: Math.floor((await p.coins()) / 100) * 100 } });
    expect(await p.finish()).toBe('Net profit 3,657 coins in 22 weeks');
    expect(await p.page.textContent('#statement')).toContain('Shares and bonds held');
    expect(await savesWritten(p.page)).toBe(0);
    expect(errors).toEqual([]);
  }, 600000);

  it('scenario 3: licences, the guarded trip abroad and home in time', async () => {
    const { p, errors } = await player();
    await p.open(3);
    const g = p.good('g19_0');
    await p.order({ BuyTradeLicense: { kind: -1 } });
    await p.order({ BuyGoods: { good: g, quantity: 60, pay_from_deposit: false } });
    await p.week();
    for (const to of ['k0n0', 'k0', 'k0n1']) await p.go(to, true);
    await p.order({ BuyLicense: { good: g } });
    for (let left = await p.held(g); left > 0; left = await p.held(g)) {
      await p.order({ SellGoods: { good: g, quantity: left } });
      await p.week();
    }
    for (const to of ['k0', 'k0n0', 'k0n0r0']) await p.go(to, false);
    expect(await p.finish()).toBe('Net profit 1,235 coins in 24 weeks');
    expect(await p.page.locator('#next-scenario').count()).toBe(0); // the last scenario
    await p.page.click('#results-menu');
    await p.page.waitForSelector('#scenario-1');
    expect(await savesWritten(p.page)).toBe(0);
    expect(errors).toEqual([]);
  }, 600000);
});
