import { button, el } from './dom';

export type Tab = 'goal' | 'market' | 'cargo' | 'finance' | 'log';
const TITLES: Record<Tab, string> = { goal: 'Goal', market: 'Market', cargo: 'Cargo', finance: 'Finance', log: 'Log' };

export function gameTabs(scenario: boolean): {
  node: HTMLElement; panels: Record<Tab, HTMLElement>; activate(id: Tab): void;
} {
  const ids: Tab[] = scenario ? ['goal', 'market', 'cargo', 'finance', 'log'] : ['market', 'cargo', 'finance', 'log'];
  const panels: Record<Tab, HTMLElement> = {
    goal: el('div', { id: 'panel-goal', className: 'tab-panel' }),
    market: el('div', { id: 'panel-market', className: 'tab-panel' }),
    cargo: el('div', { id: 'panel-cargo', className: 'tab-panel' }),
    finance: el('div', { id: 'panel-finance', className: 'tab-panel' }),
    log: el('div', { id: 'panel-log', className: 'tab-panel' }),
  };
  const list = el('div', { className: 'tabs' });
  list.setAttribute('role', 'tablist');
  list.setAttribute('aria-label', 'Merchant desk');
  const buttons = ids.map((id) => {
    const tab = button(TITLES[id], () => activate(id), `tab-${id}`);
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-controls', `panel-${id}`);
    panels[id].setAttribute('role', 'tabpanel');
    panels[id].setAttribute('aria-labelledby', `tab-${id}`);
    panels[id].tabIndex = 0;
    tab.addEventListener('keydown', (event) => {
      const index = ids.indexOf(id);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? ids.length - 1
        : event.key === 'ArrowRight' ? (index + 1) % ids.length : event.key === 'ArrowLeft' ? (index + ids.length - 1) % ids.length : -1;
      if (next < 0) return;
      event.preventDefault();
      activate(ids[next]!);
      buttons[next]!.focus();
    });
    return tab;
  });
  function activate(id: Tab): void {
    for (const [index, name] of ids.entries()) {
      const selected = name === id;
      buttons[index]!.setAttribute('aria-selected', String(selected));
      buttons[index]!.tabIndex = selected ? 0 : -1;
      panels[name].hidden = !selected;
    }
  }
  list.append(...buttons);
  activate(scenario ? 'goal' : 'market');
  return { node: el('aside', { className: 'side' }, list, ...ids.map((id) => panels[id])), panels, activate };
}
