import { t as i18nText } from '../i18n';
import { button, el } from './dom';

const KEY = 'fes-guide-s1-done';
export const GUIDE_STEPS = [
  { title: i18nText("guide.your-purse"), body: i18nText("guide.these-are-your-coins-grow-them-by-buying-cheap-and-s") },
  { title: i18nText("guide.the-market"), body: i18nText("guide.prices-here-are-today-s-buy-the-cheap-grain-here-pic") },
  { title: i18nText("guide.choose-where-to-sell"), body: i18nText("guide.tap-a-town-to-see-its-routes-guards-and-insurance-pr") },
  { title: i18nText("guide.end-your-week"), body: i18nText("guide.when-your-orders-are-set-press-next-week-the-other-m") },
] as const;

export function guideDone(): boolean { try { return localStorage.getItem(KEY) === '1'; } catch { return false; } }
export function markGuideDone(): void { try { localStorage.setItem(KEY, '1'); } catch { /* Play still works. */ } }
export function resetGuide(): void { try { localStorage.removeItem(KEY); } catch { /* Play still works. */ } }

export function showGuide(game: HTMLElement, targets: (step: number) => DOMRect[]): () => void {
  let step = 0, closed = false;
  const previous = document.activeElement, wasInert = game.inert;
  const title = el('h2', { id: 'guide-title' });
  const text = el('p', { id: 'guide-text' });
  const finish = (record = true): void => {
    if (closed) return;
    closed = true;
    if (record) markGuideDone();
    window.removeEventListener('resize', draw);
    overlay.remove();
    game.inert = wasInert;
    if (previous instanceof HTMLElement && previous.isConnected && previous.getAttribute('aria-selected') !== 'false') previous.focus();
    else game.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus();
  };
  const next = button(i18nText("guide.next"), () => { if (step === 3) finish(); else { step++; draw(); } }, 'guide-next');
  const skip = button(i18nText("guide.skip-guide"), () => finish(), 'guide-skip');
  const box = el('div', { className: 'dialog guide-card', id: 'guide' }, title, text, el('div', { className: 'row' }, next, skip));
  box.setAttribute('role', 'dialog');
  box.setAttribute('aria-modal', 'true');
  box.setAttribute('aria-labelledby', title.id);
  box.setAttribute('aria-describedby', text.id);
  const marks = el('div');
  marks.setAttribute('aria-hidden', 'true');
  const overlay = el('div', { className: 'guide-overlay' }, marks, box);
  box.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') { event.preventDefault(); finish(); }
    if (event.key === 'Tab' && ((event.shiftKey && document.activeElement === next) || (!event.shiftKey && document.activeElement === skip))) {
      event.preventDefault(); (event.shiftKey ? skip : next).focus();
    }
  });
  function draw(): void {
    if (closed) return;
    title.textContent = GUIDE_STEPS[step]!.title;
    text.textContent = GUIDE_STEPS[step]!.body;
    next.textContent = step === 3 ? i18nText("guide.start-trading") : i18nText("guide.next");
    const rects = targets(step).filter((rect) => rect.width > 0 && rect.height > 0);
    marks.replaceChildren(...rects.map((rect) => {
      const mark = el('div', { className: 'guide-focus' });
      Object.assign(mark.style, { left: `${rect.left - 3}px`, top: `${rect.top - 3}px`, width: `${rect.width + 6}px`, height: `${rect.height + 6}px` });
      return mark;
    }));
    const rect = rects.at(-1), size = box.getBoundingClientRect();
    let top = (rect?.bottom ?? 72) + 12;
    if (top + size.height > innerHeight - 12) top = (rect?.top ?? innerHeight) - size.height - 12;
    box.style.top = `${Math.max(12, Math.min(innerHeight - size.height - 12, top))}px`;
    box.style.left = `${Math.max(12, Math.min(innerWidth - size.width - 12, rect?.left ?? 12))}px`;
  }
  game.inert = true;
  document.body.append(overlay);
  draw();
  next.focus();
  window.addEventListener('resize', draw);
  return () => finish(false);
}

let freeHintShown = false;
export function freeHint(host: HTMLElement): void {
  try { if (localStorage.getItem('fes-free-hint-done') === '1') return; } catch { /* Session-only fallback. */ }
  if (freeHintShown) return;
  freeHintShown = true;
  try { localStorage.setItem('fes-free-hint-done', '1'); } catch { /* Session-only fallback. */ }
  const dismiss = button(i18nText("guide.close"), () => hint.remove());
  dismiss.setAttribute('aria-label', i18nText("guide.dismiss-hint"));
  const hint = el('div', { className: 'free-hint' }, el('p', { text: i18nText("guide.free-mode-the-full-economy-no-goals-save-your-game-a") }), dismiss);
  host.prepend(hint);
}
