import { t as i18nText } from '../i18n';
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

const levelName = (level: number): string => (level === LEVEL.city ? i18nText("travel.city") : level === LEVEL.village ? i18nText("travel.village") : i18nText("travel.area"));

const checkLabel = (box: HTMLInputElement, text: string): HTMLLabelElement => el('label', { className: 'check' }, box, text);

function routeItem(r: { to: number; turns: number }, nameOf: (id: number) => string, actions: TravelActions): HTMLLIElement {
  const b = button(i18nText('travel.route-button', { name: nameOf(r.to), weeks: r.turns }), () => actions.select(r.to));
  b.dataset.node = String(r.to);
  return el('li', {}, b);
}

export function travelPanel(map: MapData, actions: TravelActions): TravelPanel {
  const nameOf = (id: number): string => map.nodes[id]?.name ?? i18nText('travel.node', { id });
  const where = el('p', { id: 'where' });
  const selected = el('p', { id: 'selected', text: i18nText("travel.tap-a-town-on-the-map-to-select-it") });
  const routes = el('ul', { id: 'routes', className: 'routes' });
  const guards = el('input', { type: 'checkbox', id: 'guards' });
  const insurance = el('input', { type: 'checkbox', id: 'insurance' });
  const error = errorBox('travel-error');
  let player = -1;
  let target = -1;
  let busy = false;

  const go = button(i18nText("travel.travel"), () => void travel(), 'travel');
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
        actions.ordered(i18nText("travel.travel-to-ordered-you-leave-when-the-week-ends", { arg1: nameOf(target) }));
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
    const how = route ? i18nText("travel.week-by", { arg1: route.turns, arg2: route.turns === 1 ? '' : i18nText("travel.s"), arg3: TERRAIN_NAMES[route.terrain] ?? i18nText("travel.road") }) : i18nText("travel.no-direct-route-from-here");
    const place = i18nText('travel.place-kind', { name: nameOf(target), kind: levelName(n?.level ?? -1) });
    selected.textContent = i18nText('travel.selected-details', { place, details: target === player ? i18nText("travel.you-are-here") : how });
    go.disabled = target < 0 || target === player;
  };

  const listRoutes = (): void => {
    routes.replaceChildren(...destinations(map, player).map((r) => routeItem(r, nameOf, actions)));
  };

  // Collapsed to one line until the player opens it or picks a town, so it never hides the map.
  const summaryText = el('span', { className: 'travel-where' });
  const summary = el('summary', { id: 'travel-summary' }, el('span', { className: 'travel-title', text: i18nText("travel.travel") }), summaryText);
  const node = el(
    'details',
    { id: 'travel-panel', className: 'card travel' },
    summary,
    where,
    selected,
    el('div', { className: 'row' }, checkLabel(guards, i18nText("travel.guards")), checkLabel(insurance, i18nText("travel.insurance")), go),
    error.node,
    el('h3', { text: i18nText("travel.routes-from-here") }),
    routes,
  );

  return {
    node,
    setPlayer(at, destination, arrival) {
      player = at;
      if (destination >= 0) {
        where.textContent = i18nText("travel.travelling-from-to-arriving", { arg1: nameOf(at), arg2: nameOf(destination), arg3: (actions.dateLabel ?? calendarDate)(arrival) });
      } else where.textContent = i18nText("travel.you-are-in", { arg1: nameOf(at) });
      summaryText.textContent = destination >= 0 ? i18nText("travel.to", { arg1: nameOf(destination) }) : i18nText("travel.at", { arg1: nameOf(at) });
      listRoutes();
      describeSelected();
    },
    select(id) {
      target = id;
      if (id >= 0) (node as HTMLDetailsElement).open = true;
      error.show('');
      describeSelected();
    },
  };
}
