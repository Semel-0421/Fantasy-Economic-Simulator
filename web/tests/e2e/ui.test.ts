import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium, type Browser, type Page } from 'playwright';
import { build, preview, type PreviewServer } from 'vite';
import AxeBuilder from '@axe-core/playwright';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

let server: PreviewServer, browser: Browser, url: string;
const output = 'test-results/ui';
beforeAll(async () => {
  mkdirSync(output, { recursive: true });
  process.env.NODE_ENV = 'production';
  await build({ mode: 'production', logLevel: 'silent', build: {
    outDir: 'dist-ui', rollupOptions: { input: { app: resolve('index.html'), fixture: resolve('tests/e2e/fixtures/ui.html') } },
  } });
  server = await preview({ build: { outDir: 'dist-ui' }, preview: { port: 4184, strictPort: true } });
  url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
}, 180000);
afterAll(async () => {
  await browser?.close();
  await new Promise<void>((done) => server ? server.httpServer.close(() => done()) : done());
});

async function screen(page: Page, name: string, errors: string[], focus = true): Promise<void> {
  await page.evaluate(async () => { await document.fonts.ready; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}: horizontal overflow`).toBe(true);
  const small = await page.locator('button, a').evaluateAll((nodes) => nodes.filter((node) => {
    const rect = node.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && (rect.width < 43.9 || rect.height < 43.9);
  }).map((node) => ({ id: node.id, text: node.textContent, width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height })));
  expect(small, `${name}: touch targets`).toEqual([]);
  if (focus) {
    const focusState = (): { visible: boolean; pageFocused: boolean; tag: string } => {
      const node = document.activeElement;
      const style = node instanceof HTMLElement ? getComputedStyle(node) : null;
      return { pageFocused: document.hasFocus(), tag: node?.tagName ?? '',
        visible: node instanceof HTMLElement && node.matches(':focus-visible') && style!.outlineStyle !== 'none' && parseFloat(style!.outlineWidth) >= 2 };
    };
    await page.keyboard.press('Tab');
    let state = await page.evaluate(focusState);
    // Tab after the final phone control visits browser chrome before re-entering the document.
    if (!state.pageFocused) { await page.keyboard.press('Tab'); state = await page.evaluate(focusState); }
    expect(state.visible, `${name}: visible keyboard focus (${JSON.stringify(state)})`).toBe(true);
  }
  await page.screenshot({ path: `${output}/${name}.png`, fullPage: true });
  const audit = await new AxeBuilder({ page }).analyze();
  writeFileSync(`${output}/${name}.axe.json`, JSON.stringify(audit.violations, null, 2));
  expect(audit.violations.filter((issue) => issue.impact === 'serious' || issue.impact === 'critical')
    .map((issue) => ({ id: issue.id, nodes: issue.nodes.map((node) => ({ target: node.target, summary: node.failureSummary })) })), name).toEqual([]);
  expect(errors, `${name}: browser errors`).toEqual([]);
}

async function order(page: Page, side: 'buy' | 'sell'): Promise<void> {
  await page.getByRole('button', { name: 'Golden wheat (mid)', exact: true }).click();
  await page.fill('#qty', '1');
  await page.click(`#${side}`);
  await page.waitForSelector('#confirm');
  await page.click('#confirm');
}

describe('production UI', () => {
  for (const viewport of [{ width: 1280, height: 800 }, { width: 900, height: 700 }, { width: 360, height: 740 }]) {
    for (const reducedMotion of ['no-preference', 'reduce'] as const) {
      it(`${viewport.width}x${viewport.height}, ${reducedMotion}`, async () => {
        const context = await browser.newContext({ viewport, reducedMotion, serviceWorkers: 'block' });
        const page = await context.newPage(), errors: string[] = [];
        page.on('pageerror', (error) => errors.push(String(error)));
        page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
        page.on('request', (request) => {
          const target = new URL(request.url());
          if (target.protocol.startsWith('http') && target.origin !== new URL(url).origin) errors.push(`External request: ${target.href}`);
        });
        const prefix = `${viewport.width}-${reducedMotion}`;
        try {
          await page.goto(url); await page.waitForSelector('#scenario-1');
          await screen(page, `${prefix}-menu`, errors);
          await page.click('#scenario-1'); await page.waitForSelector('#dialog');
          await screen(page, `${prefix}-intro`, errors);
          await page.keyboard.press('Escape'); await page.waitForSelector('#guide');
          await screen(page, `${prefix}-guide`, errors);
          await page.click('#guide-skip');
          await screen(page, `${prefix}-goal`, errors);
          await page.click('#tab-market');
          await screen(page, `${prefix}-market`, errors);
          await order(page, 'buy');
          await page.click('#next');
          await page.waitForFunction(() => document.getElementById('date')?.textContent === 'Week 2 / 20' && !document.querySelector<HTMLButtonElement>('#next')!.disabled);
          await screen(page, `${prefix}-after-buy`, errors);
          const before = Number((await page.textContent('#coins'))!.replaceAll(',', ''));
          await order(page, 'sell'); await page.click('#next'); await page.waitForSelector('#growth');
          const instant = await page.textContent('#coins');
          if (viewport.width === 360 && reducedMotion === 'no-preference') {
            await page.waitForFunction(() => new DOMMatrixReadOnly(getComputedStyle(document.getElementById('coins')!.parentElement!).transform).a >= 1.249);
            const panel = (await page.locator('#coins').evaluate((node) => node.parentElement!.getBoundingClientRect().toJSON())) as { x: number; y: number; width: number };
            const date = (await page.locator('#date').boundingBox())!;
            expect(panel.x + panel.width, 'growing purse must leave the week readable').toBeLessThanOrEqual(date.x);
            expect(panel.y, 'gold frame must stay inside the screen').toBeGreaterThanOrEqual(4);
          }
          await screen(page, `${prefix}-growth`, errors, false);
          if (reducedMotion === 'reduce') {
            expect(await page.locator('.panel.growing').count()).toBe(0);
            await page.waitForSelector('#growth', { state: 'detached' });
            expect(await page.textContent('#coins')).toBe(instant);
            expect(Number(instant!.replaceAll(',', ''))).toBeGreaterThan(before);
          } else if (!(await page.locator('#dialog').count()) && await page.locator('.game[inert]').count()) await page.keyboard.press('Escape');
          if (!(await page.locator('#dialog').count())) {
            await page.waitForFunction(() => !document.querySelector<HTMLButtonElement>('#next')!.disabled);
            await page.click('#next'); await page.waitForSelector('#dialog');
          }
          await screen(page, `${prefix}-news`, errors);
          await page.keyboard.press('Escape');
          await page.goto(`${url}tests/e2e/fixtures/ui.html`); await page.waitForSelector('#score');
          expect(await page.textContent('#score')).toContain('Net profit 2,140');
          expect(await page.textContent('#results-target')).toContain('Target 1,000 · You 2,140');
          expect(await page.locator('#results-objectives .objective-mark.checked').count()).toBe(3);
          await screen(page, `${prefix}-results`, errors);
          await page.click('#results-menu'); await page.waitForSelector('#credits'); await page.click('#credits');
          await screen(page, `${prefix}-credits`, errors);
        } finally { await context.close(); }
      });
    }
  }
});
