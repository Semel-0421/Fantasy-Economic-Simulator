// Travel controls under the map: where the player is, the selected town, the routes out of the
// current town as buttons (keyboard and small screens), guards and insurance, and Travel, which
// previews the command and queues it only if the simulation accepts it.
import { destinations, LEVEL, TERRAIN_NAMES, type MapData } from '../map/layout';
import type { CommandResult, PlayerCommand } from '../sim/types';
import { button, el } from './dom';
import { errorBox, rejection } from './errors';
import { calendarDate, type DateLabel } from './turn';

export interface TravelActions {
  preview(command: PlayerCommand): Promise<CommandResult>;
  queue(command: PlayerCommand): Promise<void>;
  select(node: number): void;
  ordered(text: string): void;
  /** Names the arrival turn; the calendar unless set (a scenario counts its own weeks). */
  dateLabel?: DateLabel;
}

export interface TravelPanel {
  node: HTMLElement;
  setPlayer(node: number, destination: number, arrival: number): void;
  select(node: number): void;
}

const levelName = (level: number): string => (level === LEVEL.city ? 'City' : level === LEVEL.village ? 'Village' : 'Area');

export function travelPanel(map: MapData, actions: TravelActions): TravelPanel {
  const nameOf = (id: number): string => map.nodes[id]?.name ?? `Node ${id}`;
  const where = el('p', { id: 'where' });
  const selected = el('p', { id: 'selected', text: 'Tap a town on the map to select it.' });
  const routes = el('ul', { id: 'routes', className: 'routes' });
  const guards = el('input', { type: 'checkbox', id: 'guards' });
  const insurance = el('input', { type: 'checkbox', id: 'insurance' });
  const error = errorBox('travel-error');
  let player = -1;
  let target = -1;
  let busy = false;

  const go = button('Travel', () => void travel(), 'travel');
  go.disabled = true;

  async function travel(): Promise<void> {
    if (target < 0 || busy) return;
    busy = true;
    go.disabled = true;
    const command: PlayerCommand = { Travel: { destination: target, guards: guards.checked, insurance: insurance.checked } };
    try {
      const refused = rejection(await actions.preview(command));
      error.show(refused ?? '');
      if (refused === null) {
        await actions.queue(command);
        actions.ordered(`Travel to ${nameOf(target)} ordered; you leave when the week ends.`);
      }
    } catch (e) {
      error.show(e instanceof Error ? e.message : String(e));
    } finally {
      busy = false;
      go.disabled = target < 0;
    }
  }

  const describeSelected = (): void => {
    if (target < 0) return;
    const n = map.nodes[target];
    const route = map.routes.find((r) => r.from === player && r.to === target);
    const how = route ? `${route.turns} week${route.turns === 1 ? '' : 's'} by ${TERRAIN_NAMES[route.terrain] ?? 'road'}` : 'no direct route from here';
    selected.textContent = `${nameOf(target)} (${levelName(n?.level ?? -1)}): ${target === player ? 'you are here' : how}.`;
    go.disabled = target < 0 || target === player;
  };

  const listRoutes = (): void => {
    routes.replaceChildren(
      ...destinations(map, player).map((r) => {
        const b = button(`${nameOf(r.to)} - ${r.turns} wk`, () => actions.select(r.to));
        b.dataset.node = String(r.to);
        return el('li', {}, b);
      }),
    );
  };

  const check = (box: HTMLInputElement, text: string): HTMLLabelElement => {
    const label = el('label', { className: 'check' }, box, text);
    return label;
  };

  const node = el(
    'section',
    { id: 'travel-panel', className: 'card' },
    el('h2', { text: 'Travel' }),
    where,
    selected,
    el('div', { className: 'row' }, check(guards, 'Guards'), check(insurance, 'Insurance'), go),
    error.node,
    el('h3', { text: 'Routes from here' }),
    routes,
  );

  return {
    node,
    setPlayer(at, destination, arrival) {
      player = at;
      if (destination >= 0) {
        where.textContent = `Travelling from ${nameOf(at)} to ${nameOf(destination)}; arriving ${(actions.dateLabel ?? calendarDate)(arrival)}.`;
      } else where.textContent = `You are in ${nameOf(at)}.`;
      listRoutes();
      describeSelected();
    },
    select(id) {
      target = id;
      error.show('');
      describeSelected();
    },
  };
}
