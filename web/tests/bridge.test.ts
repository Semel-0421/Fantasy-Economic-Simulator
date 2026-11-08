import { readFileSync } from 'node:fs';
import { MessageChannel } from 'node:worker_threads';
import { afterEach, describe, expect, it } from 'vitest';
import { SimBridge } from '../src/sim/bridge';
import { createHost } from '../src/sim/host';
import type { GameFiles } from '../src/data/files';
import init, { WasmSim } from '../src/sim/pkg/fes_wasm.js';

const root = new URL('../../', import.meta.url);
const dataDir = new URL('Assets/StreamingAssets/Data/', root);
const wasmBytes = readFileSync(new URL('web/src/sim/pkg/fes_wasm_bg.wasm', root));

function files(): GameFiles {
  const names = ['config', 'events', 'facilities', 'goods', 'map_nodes', 'map_routes', 'market_profiles', 'recipes', 'specialties'];
  return Object.fromEntries(names.map((n) => [`${n}.csv`, readFileSync(new URL(`${n}.csv`, dataDir), 'utf8')]));
}

const open: SimBridge[] = [];

/** A bridge wired to a host over a MessageChannel: the same messages a Worker carries. */
async function connect(seed: number, load?: Uint8Array): Promise<SimBridge> {
  const { port1, port2 } = new MessageChannel();
  await init({ module_or_path: wasmBytes });
  const host = createHost(WasmSim);
  port2.on('message', (request) => {
    void host.handle(request).then(({ response, transfer }) => port2.postMessage(response, transfer as never));
  });
  const bridge = new SimBridge(port1 as never);
  open.push(bridge);
  return load ? bridge.loadWorld(files(), load) : bridge.startWorld(files(), seed);
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
});
