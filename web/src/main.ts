// Entry point: main menu, then a free-mode or scenario game on the simulation Worker.
import '@fontsource/pixelify-sans/latin-400.css';
import '@fontsource/atkinson-hyperlegible/latin-400.css';
import '@fontsource/atkinson-hyperlegible/latin-700.css';
import './ui/styles.css';
import { prepareAssets } from './assets/loader';
import { installSound, musicScene, setVolume } from './audio/sound';
import { readVolume } from './ui/menu';
import { BROKEN_MESSAGE, SaveStore, type Slot } from './save/store';
import { SimBridge } from './sim/bridge';
import type { PlayerCommand } from './sim/types';
import { renderHud, type Hud } from './ui/hud';
import { renderMenu } from './ui/menu';
import { calendarDate, describeEvent, playTurn, scenarioDate } from './ui/turn';
import { createMap, type MapScreen } from './map/map';
import type { MapData } from './map/layout';
import { rejectionsIn } from './ui/errors';
import { travelPanel, type TravelPanel } from './ui/travel';
import { goodLabel, marketPanel, type MarketPanel } from './ui/market';
import { cargoPanel } from './ui/cargo';
import { pursePanel } from './ui/purse';
import { registerServiceWorker } from './ui/update';
import { dialogs, type Dialogs } from './ui/dialog';
import { scenarioDialogs, scenarioEnd, scenarioPanel, type ScenarioPanel } from './ui/scenario';
import { renderResults } from './ui/results';
import { nextScenario } from './data/scenarios';
import { bondsPanel, exchangePanel, licencePanel, type BondsPanel, type ExchangePanel, type LicencePanel } from './ui/finance';
import { gainsOf, presentGrowth } from './ui/growth';
import { finishWeek } from './ui/week_end';

const root = document.getElementById('app');
if (!root) throw new Error('Missing #app');
const app: HTMLElement = root;

let saves: SaveStore | null = null;
let bridge: SimBridge | null = null;
let mapScreen: MapScreen | null = null;

function showFullscaleBench(): void {
  const out = Object.assign(document.createElement('pre'), { id: 'bench-result', textContent: 'Running full-scale benchmark...' });
  app.replaceChildren(out);
  void SimBridge.fullscaleBench().then(
    (result) => {
      out.textContent = [
        `average ms: ${result.averageMs.toFixed(3)}`,
        `worst ms: ${result.worstMs.toFixed(3)} (turn ${result.worstTurn})`,
        `hash after turn 11: ${result.hashAfter11}`,
        `C# hash match: ${result.hashMatches ? 'yes' : 'no'} (expected ${result.expectedHash11})`,
        `browser user agent: ${navigator.userAgent}`,
      ].join('\n');
    },
    (error: unknown) => {
      out.textContent = `Benchmark failed: ${message(error)}`;
    },
  );
}

/** The screens of one running game that refresh from views. */
interface Screens {
  hud: Hud;
  travel: TravelPanel;
  market: MarketPanel;
  cargo: ReturnType<typeof cargoPanel>;
  purse: ReturnType<typeof pursePanel>;
  exchange: ExchangePanel;
  bonds: BondsPanel;
  licences: LicencePanel;
  goodName(good: number): string;
  map: MapData;
  /** Set in a scenario: its number, title, panel and news dialogs. */
  scenario?: { n: number; title: string; panel: ScenarioPanel; dialogs: Dialogs };
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
  musicScene('menu');
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
      scenario: (n) => void begin(SimBridge.scenario(n), n),
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

async function begin(starting: Promise<SimBridge>, scenarioNumber?: number): Promise<void> {
  musicScene('game');
  app.replaceChildren(Object.assign(document.createElement('p'), { className: 'loading', id: 'status', textContent: 'Loading the simulation...' }));
  try {
    bridge = await starting;
  } catch (e) {
    await showMenu(`Could not start: ${message(e)}`);
    return;
  }
  const game = bridge;
  const info = scenarioNumber === undefined ? null : await game.view('scenario');
  // A scenario shows its own week everywhere (header, log, arrivals), as its panel does.
  const dateLabel = info ? scenarioDate(await game.turn()) : calendarDate;
  const hud = renderHud(
    app,
    info ? 'scenario' : 'free',
    {
      nextWeek: () => void nextWeek(game, screens),
      save: (slot) => void saveNow(game, hud, slot),
      menu: () => void showMenu(),
    },
    dateLabel,
  );
  game.onFailure((e) => hud.fail(e.message));
  const [map, goods] = await Promise.all([game.view('map'), game.view('goods')]);
  const goodName = (g: number): string => (goods[g] ? goodLabel(goods[g].key) : `Good ${g}`);
  const select = (node: number): void => {
    mapScreen?.select(node);
    travel.select(node);
  };
  const travel = travelPanel(map, {
    preview: (c) => game.preview(c),
    queue: (c) => game.queue(c),
    select,
    ordered: (text) => hud.addMessage(text),
    dateLabel,
  });
  hud.mapTools.append(travel.node);
  const orders = {
    preview: (c: PlayerCommand) => game.preview(c),
    queue: (c: PlayerCommand) => game.queue(c),
    ordered: (text: string) => hud.addMessage(text),
  };
  const townOf = (n: number): string => map.nodes[n]?.name ?? `Place ${n}`;
  const screens: Screens = {
    hud,
    travel,
    map,
    goodName,
    market: marketPanel(goodName, orders, (g) => goods[g]?.key ?? ''),
    exchange: exchangePanel(townOf, orders),
    bonds: bondsPanel(orders),
    licences: licencePanel(goods, goodName, orders),
    cargo: cargoPanel(goodName, (g) => goods[g]?.key ?? ''),
    purse: pursePanel(map.nodes.filter((n) => n.level === 0).sort((a, b) => a.continent - b.continent).map((n) => n.name)),
  };
  if (info && scenarioNumber !== undefined) {
    const panel = scenarioPanel(info.title, info.weeks, { good: goodName, node: (n) => map.nodes[n]?.name ?? `Place ${n}` }, (id) => void accept(game, screens, id));
    screens.scenario = { n: scenarioNumber, title: info.title, panel, dialogs: dialogs(document.body, () => app.querySelector('.game')) };
    hud.side.append(panel.node);
  }
  hud.side.append(
    screens.market.node,
    screens.cargo.node,
    screens.purse.node,
    screens.exchange.node,
    screens.bonds.node,
    screens.licences.node,
  );
  try {
    mapScreen = await createMap(hud.mapHost, map, select);
    // The browser tests find towns on the canvas through this handle (development builds only).
    if (import.meta.env.DEV) Object.assign(window, { __fesMap: mapScreen, __fesGame: game });
  } catch (e) {
    hud.mapHost.append(Object.assign(document.createElement('p'), { className: 'note', textContent: `The map could not be drawn (${message(e)}); use the routes list.` }));
  }
  await refresh(game, screens);
  if (info && screens.scenario) await showDialogs(screens, [{ title: info.title, text: info.intro }]);
}

/** News dialogs; the game behind them is inert (no input) until the last is dismissed. */
async function showDialogs(s: Screens, items: { title: string; text: string }[]): Promise<void> {
  if (s.scenario) await s.scenario.dialogs.show(items);
}

async function accept(game: SimBridge, s: Screens, id: string): Promise<void> {
  try {
    await game.acceptContract(id);
    s.hud.addMessage('Contract accepted.');
  } catch (e) {
    s.hud.addMessage(`Could not accept: ${message(e)}`);
  }
  await refresh(game, s);
}

async function showResults(game: SimBridge, s: Screens): Promise<void> {
  const scenario = s.scenario;
  const [statement, objectives] = await Promise.all([game.view('results'), game.view('objectives')]);
  if (!scenario || !statement) return;
  const next = nextScenario(scenario.n);
  bridge?.dispose();
  bridge = null;
  mapScreen?.destroy();
  mapScreen = null;
  renderResults(app, scenario.title, statement, objectives, {
    next: next === null ? undefined : () => void begin(SimBridge.scenario(next), next),
    menu: () => void showMenu(),
  });
}

async function refresh(game: SimBridge, s: Screens): Promise<void> {
  const [turn, purse, cargo, exchange, licences, caravans] = await Promise.all([
    game.turn(),
    game.view('purse'),
    game.view('cargo'),
    game.view('exchange'),
    game.view('licences'),
    game.view('caravans'),
  ]);
  const market = await game.view('market', purse.node);
  s.hud.setState(turn, purse.coins, purse.local_continent);
  s.market.update(s.map.nodes[purse.node]?.name ?? '', market.quotes, new Map(cargo.cart));
  s.cargo.update(cargo);
  s.purse.update(purse);
  s.exchange.update(exchange);
  s.bonds.update(purse);
  s.licences.update(licences);
  mapScreen?.setCaravans(caravans);
  s.travel.setPlayer(purse.node, purse.travel_destination, purse.arrival_turn);
  if (s.scenario) {
    const [results, objectives, contracts] = await Promise.all([game.view('results'), game.view('objectives'), game.view('scenario_contracts')]);
    s.scenario.panel.update(results?.weeks ?? 0, objectives, contracts);
  }
  mapScreen?.setPlayer(purse.node, purse.travel_destination);
}

async function nextWeek(game: SimBridge, s: Screens): Promise<void> {
  const hud = s.hud;
  hud.setBusy(true);
  mapScreen?.moveCaravans(true);
  try {
    const before = localCoins(await game.view('purse'));
    const { report, saveError } = await playTurn({ bridge: game, saves, mode: s.scenario ? 'scenario' : 'free' });
    hud.addTurn(report.turn, report.events.map(describeEvent));
    for (const reason of rejectionsIn(report)) hud.addMessage(`Order refused: ${reason}`);
    if (saveError) hud.addMessage(`Autosave failed: ${saveError}`);
    await refresh(game, s);
    mapScreen?.moveCaravans(false);
    const gains = gainsOf(report, s.goodName);
    const after = localCoins(await game.view('purse'));
    await finishWeek({
      ended: s.scenario !== undefined && scenarioEnd(report) !== null,
      present: async () => {
        if (gains.length > 0 && after > before) {
          await presentGrowth({ coins: hud.coins, game: app.querySelector('.game') }, before, after, gains);
        }
        hud.setBusy(false);
      },
      dialogs: () => showDialogs(s, scenarioDialogs(report)),
      results: () => showResults(game, s),
      onError: (e) => hud.addMessage(`The week's presentation failed: ${message(e)}`),
    });
  } catch (e) {
    hud.addMessage(`The turn failed: ${message(e)}`);
  } finally {
    mapScreen?.moveCaravans(false);
    if (bridge === game) hud.setBusy(false);
  }
}

const localCoins = (p: { coins: number[]; local_continent: number }): number => p.coins[p.local_continent] ?? 0;

async function saveNow(game: SimBridge, hud: Hud, slot: Slot): Promise<void> {
  try {
    if (!saves) throw new Error('Saving is unavailable in this browser.');
    await saves.write(slot, 'free', await game.save(), await game.turn());
    hud.addMessage(`Saved to ${slot.replace('manual', 'slot ')}.`);
  } catch (e) {
    hud.addMessage(`Save failed: ${message(e)}`);
  }
}

if (new URLSearchParams(location.search).get('bench') === 'fullscale') {
  showFullscaleBench();
} else {
  setVolume(readVolume());
  installSound();
  prepareAssets().then(() => showMenu()).catch((e: unknown) => {
    app.textContent = `Could not start: ${message(e)}`;
  });

  // Offline after one visit, and a reload prompt when a new build is waiting (production only).
  registerServiceWorker();
}
