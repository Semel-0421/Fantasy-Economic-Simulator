// Offline play and updates: registers the service worker and, when a new build is waiting, offers a
// reload. The waiting worker activates only when the player agrees, so a running game is never
// swapped under them.
import { button, el } from './dom';

/** The "new version" bar; the page reloads once the new worker has taken over. */
export function showUpdatePrompt(root: HTMLElement, activate: () => void): HTMLElement {
  const existing = document.getElementById('update-prompt');
  if (existing) return existing;
  const bar = el('div', { id: 'update-prompt', className: 'update-prompt' }, el('span', { text: 'A new version is ready — ' }));
  bar.setAttribute('role', 'status');
  bar.append(button('Reload', activate, 'update-reload'));
  root.append(bar);
  return bar;
}

export function registerServiceWorker(): void {
  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return;
  const sw = navigator.serviceWorker;
  let reloading = false;
  let asked = false;
  sw.addEventListener('controllerchange', () => {
    // Only reload for an update the player asked for, never for the first install.
    if (!asked || reloading) return;
    reloading = true;
    window.location.reload();
  });
  const offer = (waiting: ServiceWorker): void => {
    showUpdatePrompt(document.body, () => {
      asked = true;
      waiting.postMessage({ type: 'activate' });
    });
  };
  window.addEventListener('load', () => {
    sw.register('./sw.js')
      .then((reg) => {
        // A controller means an older build is running; without one this is the first install.
        if (reg.waiting && sw.controller) offer(reg.waiting);
        reg.addEventListener('updatefound', () => {
          const worker = reg.installing;
          worker?.addEventListener('statechange', () => {
            if (worker.state === 'installed' && sw.controller) offer(worker);
          });
        });
        document.addEventListener('visibilitychange', () => {
          if (document.visibilityState === 'visible') void reg.update().catch(() => undefined);
        });
      })
      .catch((e: unknown) => console.warn('Service worker not registered', e));
  });
}
