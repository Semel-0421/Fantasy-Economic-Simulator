import { button, el } from './dom';

export type Tab = 'goal' | 'market' | 'cargo' | 'finance' | 'log' | 'more';
const TITLES: Record<Tab, string> = { goal: 'Goal', market: 'Market', cargo: 'Cargo', finance: 'Finance', log: 'Log', more: 'More' };
export interface GameTabs {
  node: HTMLElement;
  panels: Record<Tab, HTMLElement>;
  activate(id: Tab): void;
  destroy(): void;
}

export function gameTabs(scenario: boolean): GameTabs {
  const ids: Tab[] = scenario ? ['goal', 'market', 'cargo', 'finance', 'log', 'more'] : ['market', 'cargo', 'finance', 'log', 'more'];
  const panels: Record<Tab, HTMLElement> = {
    goal: el('div', { id: 'panel-goal', className: 'tab-panel' }),
    market: el('div', { id: 'panel-market', className: 'tab-panel' }),
    cargo: el('div', { id: 'panel-cargo', className: 'tab-panel' }),
    finance: el('div', { id: 'panel-finance', className: 'tab-panel' }),
    log: el('div', { id: 'panel-log', className: 'tab-panel' }),
    more: el('div', { id: 'panel-more', className: 'tab-panel' }),
  };
  const mobile = matchMedia('(max-width: 899px)');
  let active: Tab = scenario ? 'goal' : 'market';
  const visibleIds = (): Tab[] => ids.filter((id) => mobile.matches ? id !== 'finance' && id !== 'log' : id !== 'more');
  const list = el('div', { className: 'tabs' });
  list.setAttribute('role', 'tablist');
  list.setAttribute('aria-label', 'Merchant desk');
  const body = el('div', { className: 'sheet-content', id: 'merchant-panels' }, ...ids.map((id) => panels[id]));
  const node = el('aside', { className: 'side' }, list, body);
  const buttons = ids.map((id) => {
    const tab = button(TITLES[id], () => {
      const repeat = active === id;
      activate(id);
      node.dispatchEvent(new CustomEvent('tab-change', { detail: { repeat } }));
    }, `tab-${id}`);
    tab.className = id === 'more' ? 'phone-only' : id === 'finance' || id === 'log' ? 'desktop-tab' : '';
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-controls', `panel-${id}`);
    panels[id].setAttribute('role', 'tabpanel');
    panels[id].setAttribute('aria-labelledby', `tab-${id}`);
    panels[id].tabIndex = 0;
    tab.addEventListener('keydown', (event) => {
      const visible = visibleIds(), index = visible.indexOf(id);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? visible.length - 1
        : event.key === 'ArrowRight' ? (index + 1) % visible.length : event.key === 'ArrowLeft' ? (index + visible.length - 1) % visible.length : -1;
      if (next < 0) return;
      event.preventDefault();
      activate(visible[next]!);
      buttons[ids.indexOf(visible[next]!)]!.focus();
      node.dispatchEvent(new CustomEvent('tab-change', { detail: { repeat: false } }));
    });
    return tab;
  });
  function activate(id: Tab): void {
    active = mobile.matches && (id === 'finance' || id === 'log') ? 'more' : !mobile.matches && id === 'more' ? 'finance' : id;
    for (const [index, name] of ids.entries()) {
      const selected = name === active;
      buttons[index]!.setAttribute('aria-selected', String(selected));
      buttons[index]!.tabIndex = selected ? 0 : -1;
      panels[name].hidden = mobile.matches && (name === 'finance' || name === 'log') ? active !== 'more' : !selected;
    }
  }
  const layout = (): void => {
    for (const id of ['finance', 'log'] as const) {
      (mobile.matches ? panels.more : body).append(panels[id]);
      panels[id].setAttribute('role', mobile.matches ? 'region' : 'tabpanel');
      if (mobile.matches) {
        panels[id].removeAttribute('aria-labelledby');
        panels[id].setAttribute('aria-label', TITLES[id]);
      } else {
        panels[id].setAttribute('aria-labelledby', `tab-${id}`);
        panels[id].removeAttribute('aria-label');
      }
    }
    activate(active);
  };
  list.append(...buttons);
  layout();
  mobile.addEventListener('change', layout);
  return { node, panels, activate, destroy: () => mobile.removeEventListener('change', layout) };
}
