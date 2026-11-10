// Production review: actual scenario start/market/travel, plus the existing production news/results fixture.
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { build, preview } from 'vite';
import { chromium } from 'playwright';
import AxeBuilder from '@axe-core/playwright';
import { LANGUAGES } from '../src/i18n/languages.ts';
import { en } from '../src/i18n/en.ts';

const screenshots = new Set(['ko', 'ar', 'ur', 'ja', 'zh-Hans', 'hi', 'de', 'ru']);
const requested = process.argv.slice(2);
if (requested.some(code => !LANGUAGES.some(language => language.code === code))) throw new Error('Unknown language code');
const languages = LANGUAGES.filter(language => !requested.length || requested.includes(language.code));
const report = [], output = 'test-results';
mkdirSync(output, { recursive: true });
process.env.NODE_ENV = 'production';
await build({ mode: 'production', logLevel: 'silent', build: {
  outDir: 'dist-ui', rollupOptions: { input: { app: resolve('index.html'), fixture: resolve('tests/e2e/fixtures/ui.html') } },
} });
const server = await preview({ build: { outDir: 'dist-ui' }, preview: { port: 4195, strictPort: true } });
const url = server.resolvedUrls.local[0];
let browser;
const save = () => writeFileSync(`${output}/ui-languages-report.json`, JSON.stringify(report, null, 2));
try {
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'msedge', headless: true });
  for (const language of languages) for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
    const context = await browser.newContext({ viewport, reducedMotion: 'reduce', serviceWorkers: 'block' });
    const page = await context.newPage(), errors = [];
    let stage = 'startup';
    page.on('pageerror', error => errors.push(String(error)));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('request', request => {
      const target = new URL(request.url());
      if (target.protocol.startsWith('http') && target.origin !== new URL(url).origin) errors.push(`External request: ${target.href}`);
    });
    await context.addInitScript(code => {
      localStorage.setItem('fes-lang', code); localStorage.setItem('fes-volume', '0'); localStorage.setItem('fes-music', 'off');
    }, language.code);
    const check = async name => {
      stage = name;
      await page.evaluate(async () => { await document.fonts.ready; });
      const measured = await page.evaluate(({ expected, ids }) => {
        const overflows = [];
        for (const node of document.querySelectorAll('button,select,.hud span,.hud strong,.dialog h2,.dialog p,.menu h1,.menu h2,.menu p,.menu label,.menu summary,.menu a,.guide-card p,#selected,#where,.travel-where,.phone-travel p')) {
          if (node.closest('.sr-only,[aria-hidden="true"]') || !node.getClientRects().length) continue;
          if (!node.textContent?.trim() || node.clientWidth === 0 || node.scrollWidth <= node.clientWidth + 1) continue;
          overflows.push({ selector: node.id ? `#${node.id}` : `${node.tagName.toLowerCase()}.${node.className}`, text: node.textContent.trim(), width: node.clientWidth, scrollWidth: node.scrollWidth });
        }
        const text = document.body.innerText;
        return {
          direction: document.documentElement.dir, expectedDirection: expected,
          documentOverflow: document.documentElement.scrollWidth > innerWidth + 1,
          overflows, missingIds: ids.filter(id => text.includes(id)), placeholders: text.match(/\{(?:arg\d+|qty|good|dest|deadline|reward|paid|slot)\}/g) ?? [],
        };
      }, { expected: language.dir, ids: Object.keys(en) });
      const record = { language: language.code, width: viewport.width, screen: name, ...measured, errors: [...errors] };
      if (screenshots.has(language.code)) {
        const folder = `${output}/ui-${language.code}`; mkdirSync(folder, { recursive: true });
        record.screenshot = `${folder}/${viewport.width}x${viewport.height}-${name}.png`;
        await page.screenshot({ path: record.screenshot, fullPage: true });
        const audit = await new AxeBuilder({ page }).analyze();
        record.accessibility = audit.violations.filter(issue => ['serious', 'critical'].includes(issue.impact)).map(issue => ({ id: issue.id, nodes: issue.nodes.map(node => node.target) }));
      }
      record.passed = record.direction === record.expectedDirection && !record.documentOverflow && !record.overflows.length && !record.missingIds.length && !record.placeholders.length && !record.errors.length && !(record.accessibility?.length);
      report.push(record); save();
    };
    try {
      await page.goto(url); await page.waitForSelector('#scenario-1'); await page.locator('.settings summary').click();
      await check('menu');
      await page.click('#scenario-1'); await page.waitForSelector('#dialog'); await page.keyboard.press('Escape'); await page.waitForSelector('#guide');
      await check('guide'); await page.click('#guide-skip');
      await page.click('#tab-market'); await page.locator('#market-table tbody button').first().click();
      await check('market');
      if (viewport.width < 900) await page.click('#tab-more');
      await page.click('#travel-summary'); await page.locator('#routes button').first().click();
      await check('travel');
      await page.goto(`${url}tests/e2e/fixtures/ui.html?screen=news`); await page.waitForSelector('#dialog'); await check('news');
      await page.goto(`${url}tests/e2e/fixtures/ui.html`); await page.waitForSelector('#score'); await check('results');
      await page.click('#results-menu'); await page.waitForSelector('#credits'); await page.click('#credits'); await check('credits');
    } catch (error) {
      report.push({ language: language.code, width: viewport.width, screen: stage, passed: false, fatal: String(error), errors: [...errors] }); save();
    } finally { await context.close(); }
    console.log(`${language.code} ${viewport.width}: ${report.filter(row => row.language === language.code && row.width === viewport.width && !row.passed).length} failing screens`);
  }
} finally {
  await browser?.close(); await new Promise(done => server.httpServer.close(done)); save();
}
const failures = report.filter(row => !row.passed);
if (report.length !== languages.length * 14) throw new Error('Incomplete language review');
console.log(JSON.stringify({ checked: report.length, failures: failures.length, screenshots: report.filter(row => row.screenshot).length, report: `${output}/ui-languages-report.json` }));
if (failures.length) process.exitCode = 1;
