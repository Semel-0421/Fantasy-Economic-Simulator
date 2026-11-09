import { button, el } from './dom';
import type { GameTabs } from './tabs';

/** Move the existing controls across the breakpoint, preserving ids and state. */
export function phoneShell(game: HTMLElement, tabs: GameTabs, controls: {
  header: HTMLElement; tools: HTMLElement; next: HTMLElement; map: HTMLElement;
  travel: HTMLElement; extras: HTMLElement[]; menu(): void;
}): { select(text: string): void; destroy(): void } {
  const mobile = matchMedia('(max-width: 899px)');
  const states = ['half', 'full', 'collapsed'] as const;
  let height = 0;
  const handle = button('', () => setHeight((height + 1) % states.length), 'sheet-handle');
  handle.className = 'sheet-handle phone-only';
  handle.append(el('span', { className: 'sheet-grip' }));
  handle.setAttribute('aria-controls', 'merchant-panels');
  const heading = el('h2', { text: 'Travel options', className: 'phone-only' });
  const extras = el('div', { className: 'more-tools phone-only' });
  const menu = button('Menu', controls.menu, 'more-menu');
  extras.append(menu);
  tabs.panels.more.append(heading, extras);
  const selected = el('p');
  const compact = el('div', { className: 'phone-travel phone-only card', id: 'phone-travel' }, selected, button('Travel options', () => {
    tabs.activate('more');
    setHeight(1);
    controls.travel.scrollIntoView({ block: 'nearest' });
  }));
  compact.hidden = true;
  controls.map.append(compact);
  tabs.node.prepend(handle);

  function setHeight(index: number): void {
    height = index;
    game.dataset.sheet = states[index];
    handle.setAttribute('aria-label', index === 1 ? 'Collapse panel' : 'Expand panel');
    handle.setAttribute('aria-expanded', String(index !== 2));
  }
  const layout = (): void => {
    if (mobile.matches) {
      heading.after(controls.travel);
      extras.prepend(...controls.extras);
      tabs.node.append(controls.next);
    } else {
      controls.map.append(controls.travel);
      controls.tools.prepend(...controls.extras);
      controls.header.append(controls.next);
    }
  };
  tabs.node.addEventListener('tab-change', (event) => {
    if (!mobile.matches) return;
    const repeat = (event as CustomEvent<{ repeat: boolean }>).detail.repeat;
    setHeight(repeat ? (height + 1) % states.length : height === 2 ? 0 : height);
  });
  setHeight(0);
  layout();
  mobile.addEventListener('change', layout);
  return {
    select(text) { selected.textContent = text; compact.hidden = !text; },
    destroy() { mobile.removeEventListener('change', layout); },
  };
}
