import { t as i18nText } from '../i18n';
// Modal dialogs for scenario news: one at a time, in order. While one is open the rest of the page
// takes no input (it is inert behind a backdrop) until the player presses Continue.
import { button, el } from './dom';
import { play } from '../audio/sound';

export interface DialogText {
  title: string;
  text: string;
}

export interface Dialogs {
  /** Shows each dialog in turn; resolves once the last is dismissed. */
  show(items: DialogText[]): Promise<void>;
  /** True while a dialog is open. */
  readonly open: boolean;
}

/** Dialogs appended to `host`; `page()` is the element made inert while one is open. */
export function dialogs(host: HTMLElement, page: () => HTMLElement | null): Dialogs {
  let open = false;
  const one = (item: DialogText): Promise<void> =>
    new Promise((resolve) => {
      const close = (): void => { backdrop.remove(); resolve(); };
      const ok = button(
        i18nText("dialog.continue"),
        close,
        'dialog-ok',
      );
      ok.classList.add('primary');
      const box = el('div', { className: 'dialog', id: 'dialog' }, el('h2', { text: item.title, id: 'dialog-title' }));
      for (const para of item.text.split('\n\n')) box.append(el('p', { text: para }));
      box.append(ok);
      box.setAttribute('role', 'dialog');
      box.setAttribute('aria-modal', 'true');
      box.setAttribute('aria-labelledby', 'dialog-title');
      box.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') { event.preventDefault(); close(); }
        if (event.key === 'Tab') { event.preventDefault(); ok.focus(); }
      });
      const backdrop = el('div', { className: 'backdrop' }, box);
      host.append(backdrop);
      play('event');
      ok.focus();
    });
  return {
    get open() {
      return open;
    },
    async show(items) {
      if (items.length === 0) return;
      open = true;
      const behind = page();
      const previous = document.activeElement;
      behind?.setAttribute('inert', '');
      try {
        for (const item of items) await one(item);
      } finally {
        behind?.removeAttribute('inert');
        open = false;
        if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
        else behind?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus();
      }
    },
  };
}
