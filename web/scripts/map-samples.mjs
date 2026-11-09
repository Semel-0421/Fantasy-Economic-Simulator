// Renders sample maps for review: npm run maps:samples [-- seed ...]
// Writes capture/maps/<seed>.png (production renderer, fitted view), <seed>-debug.png
// (plates, elevation, rainfall, biomes/rivers) and stats.json. Development only.
import { mkdirSync, writeFileSync } from 'node:fs';
import { chromium } from 'playwright';
import { createServer } from 'vite';

const seeds = process.argv.slice(2).length ? process.argv.slice(2)
  : ['scenario-1', 'scenario-2', 'scenario-3', 'sample-4', 'sample-5', 'sample-6', 'sample-7', 'sample-8', 'sample-9', 'sample-10'];
const out = 'capture/maps';
mkdirSync(out, { recursive: true });
const server = await createServer({ server: { port: 4191, strictPort: true }, logLevel: 'error' });
await server.listen();
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
const stats = [];
try {
  const page = await browser.newPage({ viewport: { width: 1600, height: 2100 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto('http://localhost:4191/samples.html');
  await page.waitForFunction(() => document.getElementById('status')?.textContent === 'ready', null, { timeout: 120000 });
  for (const seed of seeds) {
    const s = await page.evaluate((x) => window.renderSeed(x), seed);
    await page.locator('#map').screenshot({ path: `${out}/${seed}.png` });
    await page.locator('#debug').screenshot({ path: `${out}/${seed}-debug.png` });
    // The world wraps east-west: drag it half a screen so the seam sits in the middle of the view.
    const box = await page.locator('#map canvas').boundingBox();
    await page.mouse.move(box.x + 1200, box.y + 500); await page.mouse.down();
    for (let k = 1; k <= 20; k++) await page.mouse.move(box.x + 1200 - k * 40, box.y + 500);
    await page.mouse.up();
    await page.waitForTimeout(300);
    await page.locator('#map').screenshot({ path: `${out}/${seed}-seam.png` });
    stats.push(s);
    console.log(JSON.stringify(s));
  }
  writeFileSync(`${out}/stats.json`, JSON.stringify(stats, null, 2));
  if (errors.length) { console.error(errors); process.exitCode = 1; }
} finally {
  await browser.close();
  await server.close();
}
