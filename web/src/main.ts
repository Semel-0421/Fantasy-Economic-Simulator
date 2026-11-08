// Entry point: main menu, then a free-mode game on the simulation Worker.
import './ui/styles.css';
import { BROKEN_MESSAGE, SaveStore, type Slot } from './save/store';
import { SimBridge } from './sim/bridge';
import { renderHud, type Hud } from './ui/hud';
import { renderMenu } from './ui/menu';
import { describeEvent, playTurn } from './ui/turn';
import { createMap, type MapScreen } from './map/map';
import type { MapData } from './map/layout';
import { rejectionsIn } from './ui/errors';
import { travelPanel, type TravelPanel } from './ui/travel';

const root = document.getElementById('app');
if (!root) throw new Error('Missing #app');
const app: HTMLElement = root;

let saves: SaveStore | null = null;
let bridge: SimBridge | null = null;
let mapScreen: MapScreen | null = null;

/** The screens of one running game that refresh from views. */
interface Screens {
  hud: Hud;
  travel: TravelPanel;
}

const message = (e: unknown): string => (e instanceof Error ? e.message : String(e));

async function openSaves(): Promise<void> {
  try {
    saves ??= await SaveStore.open();
  } catch {
    saves = null;
  }
}

async function showMenu(notice = ''): Promise<void> {
  bridge?.dispose();
  bridge = null;
  mapScreen?.destroy();
  mapScreen = null;
  await openSaves();
  const list = saves ? await saves.list().catch(() => []) : [];
  const latest = saves ? await saves.latest().catch(() => null) : null;
  const unavailable = saves ? '' : 'Saving is unavailable in this browser.';
  renderMenu(
    app,
    list,
    latest,
    {
      freeMode: () => void begin(SimBridge.start(Math.floor(Math.random() * 0x7fffffff))),
      continueLatest: (slot) => void resume(slot),
      loadSlot: (slot) => void resume(slot),
    },
    notice || unavailable,
  );
}

async function resume(slot: Slot): Promise<void> {
  try {
    const bytes = await (saves as SaveStore).read(slot);
    await begin(SimBridge.load(bytes));
  } catch {
    await showMenu(BROKEN_MESSAGE);
  }
}

async function begin(starting: Promise<SimBridge>): Promise<void> {
  app.replaceChildren(Object.assign(document.createElement('p'), { className: 'loading', id: 'status', textContent: 'Loading the simulation...' }));
  try {
    bridge = await starting;
  } catch (e) {
    await showMenu(`Could not start: ${message(e)}`);
    return;
  }
  const game = bridge;
  const hud = renderHud(app, 'free', {
    nextWeek: () => void nextWeek(game, screens),
    save: (slot) => void saveNow(game, hud, slot),
    menu: () => void showMenu(),
  });
  const map: MapData = await game.view('map');
  const select = (node: number): void => {
    mapScreen?.select(node);
    travel.select(node);
  };
  const travel = travelPanel(map, {
    preview: (c) => game.preview(c),
    queue: (c) => game.queue(c),
    select,
    ordered: (text) => hud.addMessage(text),
  });
  hud.mapTools.append(travel.node);
  const screens: Screens = { hud, travel };
  try {
    mapScreen = await createMap(hud.mapHost, map, select);
    // The browser tests find towns on the canvas through this handle (development builds only).
    if (import.meta.env.DEV) (window as unknown as { __fesMap: MapScreen }).__fesMap = mapScreen;
  } catch (e) {
    hud.mapHost.append(Object.assign(document.createElement('p'), { className: 'note', textContent: `The map could not be drawn (${message(e)}); use the routes list.` }));
  }
  await refresh(game, screens);
}

async function refresh(game: SimBridge, s: Screens): Promise<void> {
  const [turn, purse] = await Promise.all([game.turn(), game.view('purse')]);
  s.hud.setState(turn, purse.coins, purse.local_continent);
  s.travel.setPlayer(purse.node, purse.travel_destination, purse.arrival_turn);
  mapScreen?.setPlayer(purse.node, purse.travel_destination);
}

async function nextWeek(game: SimBridge, s: Screens): Promise<void> {
  const hud = s.hud;
  hud.setBusy(true);
  try {
    const { report, saveError } = await playTurn({ bridge: game, saves, mode: 'free' });
    hud.addTurn(report.turn, report.events.map(describeEvent));
    for (const reason of rejectionsIn(report)) hud.addMessage(`Order refused: ${reason}`);
    if (saveError) hud.addMessage(`Autosave failed: ${saveError}`);
    await refresh(game, s);
  } catch (e) {
    hud.addMessage(`The turn failed: ${message(e)}`);
  } finally {
    hud.setBusy(false);
  }
}

async function saveNow(game: SimBridge, hud: Hud, slot: Slot): Promise<void> {
  try {
    if (!saves) throw new Error('Saving is unavailable in this browser.');
    await saves.write(slot, 'free', await game.save(), await game.turn());
    hud.addMessage(`Saved to ${slot.replace('manual', 'slot ')}.`);
  } catch (e) {
    hud.addMessage(`Save failed: ${message(e)}`);
  }
}

showMenu().catch((e: unknown) => {
  app.textContent = `Could not start: ${message(e)}`;
});
