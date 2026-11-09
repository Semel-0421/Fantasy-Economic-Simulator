import { mkdirSync, writeFileSync } from 'node:fs';
import { chromium, type Browser } from 'playwright';
import { createServer, type ViteDevServer } from 'vite';
import { afterAll, beforeAll, expect, it } from 'vitest';
import type { MapScreen } from '../../src/map/map';
import { overlaps } from '../../src/map/labels';
import { fullscaleMap } from '../fullscale-map';

let server: ViteDevServer, browser: Browser, url: string;
beforeAll(async () => {
  server = await createServer({ server: { port: 4192, strictPort: true }, logLevel: 'error' });
  await server.listen(); url = server.resolvedUrls!.local[0]!;
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
  mkdirSync('test-results/hex', { recursive: true });
}, 60000);
afterAll(async () => { await browser?.close(); await server?.close(); });

it('shows the full-scale hex map at three widths and samples actual pan/zoom frames', async () => {
  for (const width of [1280, 900, 360]) {
    const context = await browser.newContext({ viewport: { width, height: 800 } });
    try {
      const page = await context.newPage(), errors: string[] = [];
      page.on('pageerror', (error) => errors.push(String(error)));
      await page.addInitScript(() => localStorage.setItem('fes-guide-s1-done', '1'));
      await page.goto(url); await page.click('#scenario-1'); await page.click('#dialog-ok'); await page.waitForSelector('#map canvas');
      expect(await page.evaluate(async () => (await (window as unknown as { __fesGame: { view(name: string): Promise<{ nodes: { key: string }[] }> } }).__fesGame.view('map')).nodes.map((node) => node.key))).toEqual(fullscaleMap.nodes.map((node) => node.key));
      const boxes = await page.evaluate(() => (window as unknown as { __fesMap: MapScreen }).__fesMap.renderedLabelBoxes());
      expect(boxes.length).toBeGreaterThan(0);
      for (const [i, a] of boxes.entries()) for (const b of boxes.slice(i + 1)) expect(overlaps(a, b)).toBe(false);
      await page.screenshot({ path: `test-results/hex/${width}.png`, fullPage: true });
      // Render on demand: an idle map draws no frames.
      const idle = await page.evaluate(async () => {
        const map = (window as unknown as { __fesMap: MapScreen }).__fesMap;
        await new Promise((r) => setTimeout(r, 400));
        const before = map.renderCount();
        await new Promise((r) => setTimeout(r, 1000));
        return map.renderCount() - before;
      });
      expect(idle).toBe(0);
      if (width === 1280) {
        const sample = await page.evaluate(async () => {
          const canvas = document.querySelector<HTMLCanvasElement>('#map canvas')!, box = canvas.getBoundingClientRect();
          const frames: number[] = [];
          let before = 0;
          canvas.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 1, clientX: box.x + 100, clientY: box.y + 100, bubbles: true }));
          for (let i = 0; i < 150; i++) {
            const now = await new Promise<number>((resolve) => requestAnimationFrame(resolve));
            if (i > 30) frames.push(now - before);
            before = now;
            canvas.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: box.x + 100 + i % 30, clientY: box.y + 100, bubbles: true }));
            if (i % 10 === 0) canvas.dispatchEvent(new WheelEvent('wheel', { deltaY: i % 20 ? -15 : 15, clientX: box.x + box.width / 2, clientY: box.y + box.height / 2, bubbles: true, cancelable: true }));
          }
          canvas.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1, bubbles: true }));
          const gl = document.createElement('canvas').getContext('webgl');
          const info = gl?.getExtension('WEBGL_debug_renderer_info');
          const renderer = info ? String(gl!.getParameter(info.UNMASKED_RENDERER_WEBGL)) : 'unknown';
          return { frames: frames.length, mean: frames.reduce((a, b) => a + b, 0) / frames.length, worst: Math.max(...frames), renderer };
        });
        console.info(`Full-scale pan/zoom: ${JSON.stringify(sample)}`);
        writeFileSync('test-results/hex/frame-times.json', JSON.stringify(sample, null, 2));
        // GPU-less CI runners draw with a software renderer (SwiftShader/llvmpipe): there the frame time
        // measures the runner's CPU, not the game, so only a pathological regression fails (the uncached
        // hex grid measured 328 ms there). With a real GPU the 20 ms (dev) / 35 ms (CI) budgets apply.
        const software = /swiftshader|llvmpipe|software/i.test(sample.renderer);
        expect(sample.mean).toBeLessThan(software ? 250 : process.env.CI === 'true' ? 35 : 20);
      }
      expect(errors).toEqual([]);
    } finally { await context.close(); }
  }
}, 120000);
