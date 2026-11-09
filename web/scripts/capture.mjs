// Real scenario 1, production UI only; shared actions are also exercised by the browser goldens.
import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, renameSync, statSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { preview } from 'vite';
import { chromium } from 'playwright';
import { tradeGoods, travelTo } from '../tests/helpers/scenario-actions.ts';

const output = resolve('capture', `run-${Date.now()}`);
mkdirSync(output, { recursive: true });
const rows = readFileSync('public/data/fullscale/map_nodes.csv', 'utf8').trim().split(/\r?\n/).slice(1).map((line) => line.split(','));
const levels = ['Continent', 'Nation', 'Region', 'City', 'Village'];
rows.sort((a,b) => levels.indexOf(a[2]) - levels.indexOf(b[2]) || a[0].localeCompare(b[0], 'en'));
const nodes = new Map(rows.map((row, id) => [row[0], id]));
const server = await preview({ preview: { port: 4194, strictPort: true } });
const url = server.resolvedUrls.local[0];
let browser;
const results = [];

async function highlight(width, height, record) {
  const started = performance.now(), errors = [];
  const context = await browser.newContext({ viewport: { width, height }, reducedMotion: 'no-preference', serviceWorkers: 'block',
    ...(record ? { recordVideo: { dir: resolve(output, 'video'), size: { width, height } } } : {}) });
  const page = await context.newPage(), video = page.video();
  page.on('pageerror', (error) => errors.push(String(error)));
  page.on('request', (request) => { if (new URL(request.url()).protocol.startsWith('http') && new URL(request.url()).origin !== new URL(url).origin) errors.push(`External request: ${request.url()}`); });
  await page.addInitScript(() => { localStorage.setItem('fes-guide-s1-done', '1'); localStorage.setItem('fes-volume', '0'); });
  const hold = async (seconds) => { if (record) await delay(seconds * 1000); };
  const still = (name) => page.screenshot({ path: resolve(output, `${name}-${width}x${height}.png`) });
  const settle = (before) => page.waitForFunction((previous) => document.getElementById('results') || document.getElementById('dialog') ||
    (document.getElementById('deadline')?.textContent !== previous && document.getElementById('next')?.getAttribute('aria-busy') === 'false'), before, { timeout: 60000 });
  const week = async (growth = false) => {
    const before = await page.textContent('#deadline');
    await page.click('#next');
    if (growth) { await page.waitForSelector('#growth', { timeout: 60000 }); await still('growth'); }
    await settle(before);
    while (await page.locator('#dialog').count()) { await hold(2); await page.click('#dialog-ok'); await settle(before); }
    return await page.locator('#results').count() === 0;
  };
  const go = async (key, protectedTrip) => {
    assert(nodes.has(key), `Missing destination ${key}`);
    await travelTo(page, nodes.get(key), protectedTrip);
    await week();
    while ((await page.textContent('#where'))?.startsWith('Travelling')) await week();
  };
  const good = async (name) => {
    await page.click('#tab-market');
    return Number(await page.getByRole('button', { name: `${name} (mid)`, exact: true }).evaluate((button) => button.closest('tr').dataset.good));
  };
  try {
    await page.goto(url); await page.waitForSelector('#scenario-1'); await still('menu'); await hold(4);
    await page.click('#scenario-1'); await page.waitForSelector('#dialog', { timeout: 60000 }); await hold(6); await page.click('#dialog-ok');
    assert.equal(await page.locator('#guide').count(), 0, 'Guide must be disabled');
    await page.waitForSelector('#map canvas'); await still('map'); await hold(3);
    const grain = await good('Golden wheat');
    await tradeGoods(page, 'buy', grain, 30); await hold(3); await go('k0n0r0c0', false);
    await tradeGoods(page, 'sell', grain, 30); await week(true); await hold(2);
    await page.click('#tab-goal'); await page.click('#accept-tools');
    await page.waitForFunction(() => document.getElementById('scenario-contracts')?.textContent?.includes('Accepted'));
    const tools = await good('Smithed tools'); await tradeGoods(page, 'buy', tools, 18); await hold(3);
    await go('k0n0r0', false); await hold(2); await go('k0n0r0c1', true); await week();
    await page.click('#tab-goal');
    assert.equal(await page.locator('#objectives li', { hasText: 'Deliver the tool order' }).locator('.objective-mark.checked').count(), 1, 'Real contract must be delivered');
    await hold(3);
    while (await week());
    await page.waitForSelector('#results');
    const score = await page.textContent('#score');
    assert.equal(score, 'Net profit 1,359 coins in 20 weeks', 'Capture must match the real scenario golden');
    await still('results');
    if (record) await delay(Math.max(0, 75000 - (performance.now() - started)));
    assert.deepEqual(errors, []);
    const elapsed = (performance.now() - started) / 1000;
    if (record) assert(elapsed >= 60 && elapsed <= 90, `Capture length ${elapsed.toFixed(2)} s, expected 60–90 s`);
    results.push({ width, height, score, elapsedSeconds: elapsed });
  } finally { await context.close(); }
  if (video) {
    const path = resolve(output, 'scenario-1.webm'); renameSync(await video.path(), path);
    assert(statSync(path).size > 0, 'Video must contain recorded frames');
    const probe = await browser.newPage();
    try {
      const metadata = await probe.evaluate(async (base64) => {
        const video = document.createElement('video');
        const loaded = new Promise((resolve, reject) => { video.onloadedmetadata = () => resolve({ width: video.videoWidth, height: video.videoHeight, duration: video.duration }); video.onerror = () => reject(new Error('Recorded WebM cannot be decoded')); });
        video.src = URL.createObjectURL(new Blob([Uint8Array.from(atob(base64), (char) => char.charCodeAt(0))], { type: 'video/webm' }));
        return loaded;
      }, readFileSync(path).toString('base64'));
      assert.equal(metadata.width, 1920); assert.equal(metadata.height, 1080);
      assert(metadata.duration >= 60 && metadata.duration <= 90, `Recorded video duration ${metadata.duration}`);
      results[0].video = metadata;
    } finally { await probe.close(); }
  }
}
try {
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
  await highlight(1920, 1080, true);
  await highlight(1280, 720, false);
  writeFileSync(resolve(output, 'capture.json'), JSON.stringify(results, null, 2));
  console.log(JSON.stringify({ output, results }, null, 2));
} finally { await browser?.close(); await new Promise((done) => server.httpServer.close(done)); }
