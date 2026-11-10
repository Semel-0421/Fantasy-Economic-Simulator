import { t as i18nText, applyLanguage } from './i18n';
// Entry point: main menu, then a free-mode or scenario game on the simulation Worker.
import './ui/fonts.css';
import '@fontsource/atkinson-hyperlegible/latin-400.css';
import '@fontsource/atkinson-hyperlegible/latin-700.css';
import './ui/styles.css';
import './ui/shell.css';
import './ui/phone.css';
import './ui/presentation.css';
import './ui/guide.css';
import { prepareAssets } from './assets/loader';
import { installSound, musicScene, setVolume, setMusic } from './audio/sound';
import { readVolume, readMusic } from './ui/menu';
import { BROKEN_MESSAGE, SaveStore, type Slot } from './save/store';
import { SimBridge } from './sim/bridge';
import type { PlayerCommand, Views } from './sim/types';
import { renderHud, type Hud } from './ui/hud';
import { renderMenu } from './ui/menu';
import { calendarDate, describeEvent, playTurn, scenarioDate, type DateLabel } from './ui/turn';
import { createMap, type MapScreen } from './map/map';
import type { MapData } from './map/layout';
import { rejectionsIn } from './ui/errors';
import { travelPanel, type TravelPanel } from './ui/travel';
import { goodLabel, marketPanel, type MarketPanel } from './ui/market';
import { placeName } from './data/names';
import { cargoPanel } from './ui/cargo';
import { pursePanel } from './ui/purse';
import { registerServiceWorker } from './ui/update';
import { dialogs, type Dialogs } from './ui/dialog';
import { scenarioDialogs, scenarioEnd, scenarioPanel, type ScenarioPanel } from './ui/scenario';
import { renderResults } from './ui/results';
import { nextScenario, scenarioNodeKeys, SCENARIOS } from './data/scenarios';
import { bondsPanel, exchangePanel, licencePanel, type BondsPanel, type ExchangePanel, type LicencePanel } from './ui/finance';
import { gainsOf, presentGrowth } from './ui/growth';
import { finishWeek } from './ui/week_end';
import { guideDone, showGuide, freeHint } from './ui/guide';

applyLanguage();
const root = document.getElementById('app');
if (!root) throw new Error('Missing #app');
const app: HTMLElement = root;

let saves: SaveStore | null = null;
let bridge: SimBridge | null = null;
let mapScreen: MapScreen | null = null;
let hudScreen: Hud | null = null;
let guideCleanup: (() => void) | null = null;

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
  dateLabel: DateLabel;
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
  guideCleanup?.();
  guideCleanup = null;
  hudScreen?.destroy();
  hudScreen = null;
  bridge?.dispose();
  bridge = null;
  mapScreen?.destroy();
  mapScreen = null;
  await openSaves();
  const list = saves ? await saves.list().catch(() => []) : [];
  const latest = saves ? await saves.latest().catch(() => null) : null;
  const unavailable = saves ? '' : i18nText("main.saving-is-unavailable-in-this-browser");
  renderMenu(
    app,
    list,
    latest,
    {
      scenario: (n) => void begin(SimBridge.scenario(n), n),
      freeMode: () => {
        const seed = Math.floor(Math.random() * 0x7fffffff);
        void begin(SimBridge.start(seed), undefined, `free-${seed}`);
      },
      continueLatest: (slot) => void resume(slot),
      loadSlot: (slot) => void resume(slot),
    },
    notice || unavailable,
  );
}

async function resume(slot: Slot): Promise<void> {
  try {
    const bytes = await (saves as SaveStore).read(slot);
    await begin(SimBridge.load(bytes), undefined, (await (saves as SaveStore).mapSeedOf(slot)) ?? 'world');
  } catch {
    await showMenu(BROKEN_MESSAGE);
  }
}

/** Every player sees the same map in a scenario; free mode gets its own from the game seed. */
const scenarioMapSeed = (n: number): string => `scenario-${n}`;

type Info = Views['scenario'];

/** The player's orders, previewed or queued through the game, with a note shown on the HUD. */
function orderActions(game: SimBridge, hud: Hud) {
  return {
    preview: (c: PlayerCommand) => game.preview(c),
    queue: (c: PlayerCommand) => game.queue(c),
    ordered: (text: string) => hud.addMessage(text),
  };
}

function buildScreens(game: SimBridge, hud: Hud, map: MapData, goods: Views['goods'], travel: TravelPanel, dateLabel: DateLabel): Screens {
  const goodName = (g: number): string => (goods[g] ? goodLabel(goods[g].key) : i18nText("main.good", { arg1: g }));
  const orders = orderActions(game, hud);
  const townOf = (n: number): string => map.nodes[n]?.name ?? i18nText("main.place", { arg1: n });
  return {
    hud,
    dateLabel,
    travel,
    map,
    goodName,
    market: marketPanel(goodName, orders, (g) => goods[g]?.key ?? ''),
    exchange: exchangePanel(townOf, orders),
    bonds: bondsPanel(orders),
    licences: licencePanel(goods, goodName, orders, { csv: game.sourceFiles['goods.csv'] ?? '', scenario: game.scenarioSource, nation: townOf, dateLabel }),
    cargo: cargoPanel(goodName, (g) => goods[g]?.key ?? ''),
    purse: pursePanel(map.nodes.filter((n) => n.level === 0).sort((a, b) => a.continent - b.continent).map((n) => n.name)),
  };
}

/** Adds a scenario's goal panel and its news dialogs to the screens. */
function addScenarioScreen(game: SimBridge, screens: Screens, info: NonNullable<Info>, scenarioNumber: number): void {
  const { hud, map, goodName } = screens;
  const panel = scenarioPanel(info.title, info.weeks, { good: goodName, node: (n) => map.nodes[n]?.name ?? i18nText("main.place", { arg1: n }) }, (id) => void accept(game, screens, id));
  screens.scenario = { n: scenarioNumber, title: info.title, panel, dialogs: dialogs(document.body, () => app.querySelector('.game')) };
  hud.panels.goal.append(panel.node);
}

/** Draws the map into the HUD; returns the towns a scenario teaches with (none in free mode). */
async function mountMap(game: SimBridge, hud: Hud, map: MapData, select: (node: number) => void, scenarioNumber: number | undefined): Promise<number[]> {
  let teachingTargets: number[] = [];
  try {
    mapScreen = await createMap(hud.mapHost, map, select, game.mapSeed);
    if (scenarioNumber !== undefined) {
      const keys = new Set(scenarioNodeKeys(game.scenarioSource));
      teachingTargets = map.nodes.filter((node) => keys.has(node.key)).map((node) => node.id);
      mapScreen.setTargets(teachingTargets);
    }
    // The browser tests find towns on the canvas through this handle (development builds only).
    if (import.meta.env.DEV) Object.assign(window, { __fesMap: mapScreen, __fesGame: game });
  } catch (e) {
    hud.mapHost.append(Object.assign(document.createElement('p'), { className: 'note', textContent: i18nText("main.the-map-couldn-t-be-drawn-use-the-routes-list-to-tra", { arg1: message(e) }) }));
  }
  return teachingTargets;
}

/** The first scenario's guide, pointing at the market tab, the target town and the next-week button. */
function startFirstGuide(hud: Hud, map: MapData, select: (node: number) => void, teachingTargets: number[]): void {
  const target = map.nodes.find((node) => node.level === 3 && teachingTargets.includes(node.id))?.id ?? -1;
  guideCleanup = showGuide(app.querySelector<HTMLElement>('.game')!, (step) => {
    if (step === 1) hud.showTab('market');
    if (step === 2) {
      if (target >= 0) select(target);
      const point = mapScreen?.screenOf(target), mapBox = hud.mapHost.getBoundingClientRect();
      const card = app.querySelector<HTMLElement>(matchMedia('(max-width: 899px)').matches ? '#phone-travel' : '#travel-panel');
      return [...(point ? [new DOMRect(mapBox.left + point.x - 12, mapBox.top + point.y - 12, 24, 24)] : []), ...(card ? [card.getBoundingClientRect()] : [])];
    }
    return [app.querySelector<HTMLElement>(step === 0 ? '#coins' : step === 1 ? '#tab-market' : '#next')!.getBoundingClientRect()];
  });
}

async function begin(starting: Promise<SimBridge>, scenarioNumber?: number, mapSeed?: string): Promise<void> {
  musicScene('game');
  app.replaceChildren(Object.assign(document.createElement('p'), { className: 'loading', id: 'status', textContent: i18nText("main.loading-the-simulation") }));
  try {
    bridge = await starting;
  } catch (e) {
    await showMenu(i18nText("main.couldn-t-start-the-game", { arg1: message(e) }));
    return;
  }
  const game = bridge;
  game.mapSeed = mapSeed ?? (scenarioNumber === undefined ? 'world' : scenarioMapSeed(scenarioNumber));
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
    info?.weeks,
    info?.title,
  );
  hudScreen = hud;
  game.onFailure((e) => hud.fail(e.message));
  const [map, goods] = await Promise.all([game.view('map'), game.view('goods')]);
  for (const node of map.nodes) if (/^k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?$/.test(node.key)) node.name = placeName(node.key);
  const select = (node: number): void => {
    mapScreen?.select(node);
    travel.select(node);
    hud.setSelected(map.nodes[node]?.name ?? '');
  };
  const travel = travelPanel(map, {
    preview: (c) => game.preview(c),
    queue: (c) => game.queue(c),
    select,
    ordered: (text) => hud.addMessage(text),
    dateLabel,
  });
  hud.mapTools.append(travel.node);
  const screens = buildScreens(game, hud, map, goods, travel, dateLabel);
  if (info && scenarioNumber !== undefined) addScenarioScreen(game, screens, info, scenarioNumber);
  hud.panels.market.append(screens.market.node);
  hud.panels.cargo.append(screens.cargo.node);
  hud.panels.finance.append(screens.purse.node, screens.exchange.node, screens.bonds.node, screens.licences.node);
  const teachingTargets = await mountMap(game, hud, map, select, scenarioNumber);
  await refresh(game, screens);
  if (info && screens.scenario) await showDialogs(screens, [{ title: info.title, text: info.intro }]);
  if (scenarioNumber === 1 && !guideDone()) startFirstGuide(hud, map, select, teachingTargets);
  else if (!info) freeHint(hud.panels.market);
}

/** News dialogs; the game behind them is inert (no input) until the last is dismissed. */
async function showDialogs(s: Screens, items: { title: string; text: string }[]): Promise<void> {
  if (s.scenario) await s.scenario.dialogs.show(items);
}

async function accept(game: SimBridge, s: Screens, id: string): Promise<void> {
  try {
    await game.acceptContract(id);
    s.hud.addMessage(i18nText("main.contract-accepted"));
  } catch (e) {
    s.hud.addMessage(i18nText("main.couldn-t-accept-the-contract", { arg1: message(e) }));
  }
  await refresh(game, s);
}

async function showResults(game: SimBridge, s: Screens): Promise<void> {
  const scenario = s.scenario;
  const [statement, objectives] = await Promise.all([game.view('results'), game.view('objectives')]);
  if (!scenario || !statement) return;
  const next = nextScenario(scenario.n);
  guideCleanup?.();
  guideCleanup = null;
  bridge?.dispose();
  bridge = null;
  hudScreen?.destroy();
  hudScreen = null;
  mapScreen?.destroy();
  mapScreen = null;
  renderResults(app, scenario.title, statement, objectives, {
    next: next === null ? undefined : () => void begin(SimBridge.scenario(next), next),
    menu: () => void showMenu(),
  }, SCENARIOS.find((entry) => entry.n === scenario.n)?.targetProfit);
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
  s.hud.setState(turn, purse.coins, purse.local_continent, s.map.nodes.filter((node) => node.level === 0).sort((a, b) => a.continent - b.continent).map((node) => node.name));
  s.market.update(s.map.nodes[purse.node]?.name ?? '', market.quotes, new Map(cargo.cart));
  s.cargo.update(cargo);
  s.purse.update(purse);
  s.exchange.update(exchange);
  s.bonds.update(purse);
  s.licences.update(licences);
  mapScreen?.setCaravans(caravans);
  s.travel.setPlayer(purse.node, purse.travel_destination, purse.arrival_turn);
  s.hud.setTravel(purse.travel_destination >= 0
    ? i18nText("main.travelling-to-arrives", { arg1: s.map.nodes[purse.travel_destination]?.name ?? '', arg2: s.dateLabel(purse.arrival_turn) })
    : i18nText("main.at", { arg1: s.map.nodes[purse.node]?.name ?? '' }));
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
    hud.addTurn(report.turn, report.events.map((event) => describeEvent(event, (field, value) => {
      if (typeof value !== 'number' || value < 0) return String(value);
      if (field === 'good') return s.goodName(value);
      if (['node', 'market', 'from', 'to', 'destination', 'nation', 'nation_node', 'home', 'headquarters'].includes(field)) return s.map.nodes[value]?.name ?? String(value);
      if (field.endsWith('continent')) return s.map.nodes.find((node) => node.level === 0 && node.continent === value)?.name ?? String(value);
      return String(value);
    })));
    for (const reason of rejectionsIn(report)) hud.addMessage(i18nText("main.order-refused", { arg1: reason }));
    if (saveError) hud.addMessage(i18nText("main.autosave-failed", { arg1: saveError }));
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
      onError: (e) => hud.addMessage(i18nText("main.something-went-wrong-while-showing-this-week", { arg1: message(e) })),
    });
  } catch (e) {
    hud.addMessage(i18nText("main.the-turn-failed", { arg1: message(e) }));
  } finally {
    mapScreen?.moveCaravans(false);
    if (bridge === game) hud.setBusy(false);
  }
}

const localCoins = (p: { coins: number[]; local_continent: number }): number => p.coins[p.local_continent] ?? 0;

async function saveNow(game: SimBridge, hud: Hud, slot: Slot): Promise<void> {
  try {
    if (!saves) throw new Error(i18nText("main.saving-is-unavailable-in-this-browser"));
    await saves.write(slot, 'free', await game.save(), await game.turn(), game.mapSeed);
    hud.addMessage(slot.startsWith('manual') ? i18nText('main.saved-slot', { slot: slot.slice('manual'.length) }) : i18nText('main.saved-to', { arg1: slot }));
  } catch (e) {
    hud.addMessage(i18nText("main.save-failed", { arg1: message(e) }));
  }
}

if (new URLSearchParams(location.search).get('bench') === 'fullscale') {
  showFullscaleBench();
} else {
  setVolume(readVolume());
  setMusic(readMusic());
  installSound();
  prepareAssets().then(() => showMenu()).catch((e: unknown) => {
    app.textContent = i18nText("main.couldn-t-start-the-game", { arg1: message(e) });
  });

  // Offline after one visit, and a reload prompt when a new build is waiting (production only).
  registerServiceWorker();
}
