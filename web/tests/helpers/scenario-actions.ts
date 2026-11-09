import type { Page } from 'playwright';

export async function confirmOrder(page: Page, ask: string, prefix: string, confirm = `#${prefix}-confirm`): Promise<void> {
  const before = await page.locator('#log li.message').count();
  await page.click(ask);
  const error = `#${prefix}-error`;
  await page.waitForSelector(`${confirm}:visible, ${error}:visible`);
  if (await page.locator(`${error}:visible`).count()) throw new Error(`refused: ${await page.textContent(error)}`);
  await page.click(confirm);
  await page.waitForFunction((n) => document.querySelectorAll('#log li.message').length > n, before);
}
export async function tradeGoods(page: Page, side: 'buy' | 'sell', good: number, quantity: number): Promise<void> {
  await page.click('#tab-market');
  await page.click(`#market-table tr[data-good="${good}"] button`);
  await page.fill('#qty', String(quantity));
  await confirmOrder(page, `#${side}`, 'trade', '#confirm');
}
export async function travelTo(page: Page, destination: number, protectedTrip: boolean): Promise<void> {
  if (await page.locator('#travel-panel[open]').count() === 0) await page.click('#travel-summary');
  await page.click(`#routes button[data-node="${destination}"]`);
  await page.setChecked('#guards', protectedTrip);
  await page.setChecked('#insurance', protectedTrip);
  const before = await page.locator('#log li.message').count();
  await page.click('#travel');
  await page.waitForFunction((n) => document.querySelectorAll('#log li.message').length > n || !document.getElementById('travel-error')?.hidden, before);
  if (await page.locator('#travel-error:visible').count()) throw new Error(`refused: ${await page.textContent('#travel-error')}`);
}
