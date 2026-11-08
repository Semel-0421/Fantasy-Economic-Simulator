// Starts the Vite dev server, opens the bench in a Chromium browser (headed unless HEADLESS=1) and prints
// the result as JSON. Run `npm run wasm` first.
import { chromium } from 'playwright';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';
import { createServer } from 'vite';

// Regression ceiling (ms) for the slowest of turns 0-53; CI runners differ, so it is generous. See ceiling.json.
const ceiling = JSON.parse(readFileSync(fileURLToPath(new URL('./ceiling.json', import.meta.url)), 'utf8'));
const worstCeilingMs = Number(process.env.BENCH_WORST_CEILING_MS ?? ceiling.worstMs);

const server = await createServer({ configFile: fileURLToPath(new URL('./vite.config.mjs', import.meta.url)), server: { port: 5181, strictPort: true } });
await server.listen();
const browser = await chromium.launch({
  headless: process.env.HEADLESS === '1',
  // Playwright's bundled Chromium fails to start on the development desktop (side-by-side
  // configuration error), so the installed Edge (Chromium) is the default; BROWSER_CHANNEL=chromium
  // picks the bundled build, `chrome` an installed Chrome.
  channel: process.env.BROWSER_CHANNEL ?? 'msedge',
});
try {
  const page = await browser.newPage();
  await page.goto('http://localhost:5181/');
  await page.waitForFunction(() => 'benchResult' in window, null, { timeout: 600_000 });
  const result = await page.evaluate(() => window.benchResult);
  result.browser = `${browser.browserType().name()} ${browser.version()}`;
  console.log(JSON.stringify(result, null, 2));
  if (result.error || !result.hashMatches) process.exitCode = 1;
  if (result.worstMs > worstCeilingMs) {
    console.error(`bench regression: worst turn ${result.worstMs} ms exceeds the ceiling ${worstCeilingMs} ms`);
    process.exitCode = 1;
  }
} finally {
  await browser.close();
  await server.close();
}
