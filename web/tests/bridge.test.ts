import { readFileSync } from 'node:fs';
import { MessageChannel } from 'node:worker_threads';
import { afterEach, describe, expect, it } from 'vitest';
import { SimBridge } from '../src/sim/bridge';
import { createHost, serve } from '../src/sim/host';
import type { GameFiles } from '../src/data/files';
import init, { WasmSim } from '../src/sim/pkg/fes_wasm.js';

const root = new URL('../../', import.meta.url);
const dataDir = new URL('Assets/StreamingAssets/Data/', root);
const wasmBytes = readFileSync(new URL('web/src/sim/pkg/fes_wasm_bg.wasm', root));

function files(): GameFiles {
  const names = ['config', 'events', 'facilities', 'goods', 'map_nodes', 'map_routes', 'market_profiles', 'recipes', 'specialties'];
  return Object.fromEntries(names.map((n) => [`${n}.csv`, readFileSync(new URL(`${n}.csv`, dataDir), 'utf8')]));
}

const fullscale = new URL('engine/fixtures/fullscale/', root);
function fullscaleFiles(): GameFiles {
  const names = ['config', 'events', 'facilities', 'goods', 'map_nodes', 'map_routes', 'market_profiles', 'recipes'];
  return Object.fromEntries(names.map((n) => [`${n}.csv`, readFileSync(new URL(`${n}.csv`, fullscale), 'utf8')]));
}
const scenarioText = (file: string): string => readFileSync(new URL(`data/scenarios/${file}`, root), 'utf8');

const open: SimBridge[] = [];

/** A bridge wired to a host over a MessageChannel: the same messages a Worker carries. */
async function connect(seed: number, load?: Uint8Array): Promise<SimBridge> {
  const { port1, port2 } = new MessageChannel();
  await init({ module_or_path: wasmBytes });
  const host = createHost(WasmSim);
  port2.on("message", (request) => {
    void host.handle(request).then(({ response, transfer }) => port2.postMessage(response, transfer as never));
  });
  const bridge = new SimBridge(port1 as never);
  open.push(bridge);
  return load ? bridge.loadWorld(files(), load) : bridge.startWorld(files(), seed);
}

/** A bridge running a scenario file on the full-scale world. */
async function connectScenario(file: string): Promise<SimBridge> {
  const bridge = await connect(1);
  return bridge.startScenario(fullscaleFiles(), scenarioText(file));
}

afterEach(() => {
  for (const b of open.splice(0)) b.dispose();
});

describe('SimBridge', () => {
  it('plays turns and reports typed events', async () => {
    const bridge = await connect(1);
    expect(await bridge.turn()).toBe(0);
    const report = await bridge.next();
    expect(report.turn).toBe(0);
    expect(Array.isArray(report.events)).toBe(true);
    expect(await bridge.turn()).toBe(1);
    expect(await bridge.worldHash()).toMatch(/^\d+$/);
  });

  it('is deterministic: the same seed gives the same hash', async () => {
    const a = await connect(7);
    for (let i = 0; i < 3; i++) await a.next();
    const b = await connect(7);
    for (let i = 0; i < 3; i++) await b.next();
    expect(await a.worldHash()).toBe(await b.worldHash());
  });

  it('serves views', async () => {
    const bridge = await connect(1);
    await bridge.next();
    const purse = await bridge.view('purse');
    expect(purse.coins.length).toBeGreaterThan(0);
    const market = await bridge.view('market', purse.node);
    expect(market.quotes.length).toBeGreaterThan(0);
    expect(Array.isArray(await bridge.view('contracts'))).toBe(true);
  });

  it('previews and queues commands', async () => {
    const bridge = await connect(1);
    await bridge.next();
    const ok = await bridge.preview({ BankTransfer: { amount: 0 } });
    expect(['Ok', 'Error']).toContain(ok.result);
    await expect(bridge.preview({ Bogus: { amount: 1 } } as never)).rejects.toThrow();
    await bridge.queue('BuyPlot');
    const report = await bridge.next();
    expect(report.events.some((e) => typeof e === 'object' && ('CommandApplied' in e || 'CommandRejected' in e))).toBe(true);
  });

  it('runs a scenario: intro, objectives, the offered contract accepted, results', async () => {
    const bridge = await connectScenario('s1_first_venture.ron');
    const info = await bridge.view('scenario');
    expect(info?.title).toBe('First Venture');
    expect(info?.intro).toContain('500 coins');
    expect(info?.weeks).toBe(20);
    expect((await bridge.view('objectives')).map((o) => o.id)).toEqual(['first_sale', 'contract', 'triple']);
    expect((await bridge.view('results'))?.start_coins).toBe(500);
    await expect(bridge.acceptContract('tools')).rejects.toThrow(/No open contract/);
    for (let i = 0; i < 3; i++) await bridge.next(); // the offer comes by its fallback week
    expect((await bridge.view('scenario_contracts')).map((c) => c.id)).toEqual(['tools']);
    await bridge.acceptContract('tools');
    expect((await bridge.view('scenario_contracts'))[0]?.accepted).toBe(true);
  });

  it('has no scenario view in free mode', async () => {
    const bridge = await connect(1);
    expect(await bridge.view('scenario')).toBeNull();
    expect(await bridge.view('results')).toBeNull();
  });

  it('rejects with the engine message for unknown views and keeps working', async () => {
    const bridge = await connect(1);
    await expect(bridge.view('nope' as never)).rejects.toThrow(/Unknown view/);
    await expect(bridge.next()).resolves.toBeDefined();
  });

  it('saves and loads a world with the same hash', async () => {
    const bridge = await connect(3);
    for (let i = 0; i < 4; i++) await bridge.next();
    const bytes = await bridge.save();
    expect(bytes).toBeInstanceOf(Uint8Array);
    const hash = await bridge.worldHash();
    const loaded = await connect(0, bytes);
    expect(await loaded.worldHash()).toBe(hash);
    expect(await loaded.turn()).toBe(4);
  });

  it('rejects every call and reports the failure when the Worker dies mid-call', async () => {
    const sent: number[] = [];
    const port = {
      postMessage: (m: { id: number }) => void sent.push(m.id),
      onmessage: null,
      onerror: null,
      onmessageerror: null,
    } as unknown as ConstructorParameters<typeof SimBridge>[0];
    const bridge = new SimBridge(port);
    const failures: string[] = [];
    bridge.onFailure((e) => failures.push(e.message));
    const a = bridge.turn();
    const b = bridge.next();
    expect(sent).toHaveLength(2);
    port.onerror?.({ message: 'wasm trapped' });
    await expect(a).rejects.toThrow(/stopped.*wasm trapped/);
    await expect(b).rejects.toThrow(/stopped/);
    await expect(bridge.turn()).rejects.toThrow(/stopped/); // later calls fail at once, nothing hangs
    expect(sent).toHaveLength(2);
    expect(bridge.failed).not.toBeNull();
    expect(failures).toHaveLength(1);
    bridge.onFailure((e) => failures.push(e.message)); // already failed: told at once
    expect(failures).toHaveLength(2);
  });

  it('rejects pending calls when a message cannot be read', async () => {
    const port = { postMessage: () => undefined, onmessage: null, onerror: null, onmessageerror: null } as unknown as ConstructorParameters<typeof SimBridge>[0];
    const bridge = new SimBridge(port);
    const call = bridge.turn();
    port.onmessageerror?.({});
    await expect(call).rejects.toThrow(/could not be read/);
  });

  it('answers every request with an error when the wasm fails to start', async () => {
    const { port1, port2 } = new MessageChannel();
    port2.on('message', serve(Promise.reject(new Error('bad wasm')), (response) => port2.postMessage(response)));
    const bridge = new SimBridge(port1 as never);
    open.push(bridge);
    await expect(bridge.startWorld(files(), 1)).rejects.toThrow(/bad wasm/);
    await expect(bridge.next()).rejects.toThrow(/bad wasm/);
  });

  it('answers a request whose call throws, and keeps answering', async () => {
    const { port1, port2 } = new MessageChannel();
    let calls = 0;
    const host = {
      handle: (request: { id: number }) => {
        if (calls++ === 0) return Promise.reject(new Error('unreachable executed'));
        return Promise.resolve({ response: { id: request.id, ok: true as const, value: 5 }, transfer: [] });
      },
    };
    port2.on('message', serve(Promise.resolve(host), (response) => port2.postMessage(response)));
    const bridge = new SimBridge(port1 as never);
    open.push(bridge);
    await expect(bridge.turn()).rejects.toThrow(/unreachable executed/);
    await expect(bridge.turn()).resolves.toBe(5);
  });
});
