// Serves web/dist, opens it in the installed Edge and checks that the Worker plays a turn.
// Playwright's bundled Chromium does not start on the dev machine, so BROWSER_CHANNEL defaults to msedge.
import { preview } from 'vite';
import { chromium } from 'playwright';

const server = await preview({ preview: { port: 4173, strictPort: true } });
const url = server.resolvedUrls?.local[0] ?? 'http://localhost:4173/';
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
let failed = false;
try {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.goto(url);
  await page.waitForFunction(() => document.getElementById('status')?.textContent === 'Ready.', null, { timeout: 60000 });
  await page.click('#next');
  await page.waitForFunction(() => document.getElementById('turn')?.textContent === '1', null, { timeout: 60000 });
  const hash = await page.textContent('#hash');
  console.log(JSON.stringify({ url, turn: await page.textContent('#turn'), hash, errors }));
  failed = errors.length > 0 || !/^\d+$/.test(hash ?? '');
} finally {
  await browser.close();
  await server.close();
}
process.exit(failed ? 1 : 0);
