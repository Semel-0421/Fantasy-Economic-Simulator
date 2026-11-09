// The page's handle on the simulation Worker: every method is one typed message and a promise.
import { defaultDataUrl, loadGameFiles, type GameFiles } from '../data/files';
import { loadScenarioText, scenarioDataUrl } from '../data/scenarios';
import type { FullscaleBenchResult, Request, RequestBody, Response } from './protocol';
import type { CommandResult, PlayerCommand, TurnReport, ViewName, Views } from './types';

/** What a Worker and a MessagePort have in common. */
export interface Port {
  postMessage(message: Request, transfer?: Transferable[]): void;
  onmessage: ((e: MessageEvent<Response>) => void) | null;
  onerror?: ((e: unknown) => void) | null;
  onmessageerror?: ((e: unknown) => void) | null;
  terminate?(): void;
  close?(): void;
}

interface Pending {
  resolve(value: unknown): void;
  reject(reason: Error): void;
}

function errorText(e: unknown): string {
  const message = (e as { message?: unknown } | null)?.message;
  return typeof message === 'string' && message ? message : 'the Worker raised an error';
}

export class SimBridge {
  /** Seed of the generated map shown for this game (UI only; saved with free-mode saves). */
  mapSeed = 'world';
  private nextId = 1;
  private loadedFiles: GameFiles = {};
  private loadedScenario = '';
  get sourceFiles(): Readonly<GameFiles> { return this.loadedFiles; }
  get scenarioSource(): string { return this.loadedScenario; }
  private readonly pending = new Map<number, Pending>();

  private failure: Error | null = null;
  private readonly failListeners: ((error: Error) => void)[] = [];

  constructor(private readonly port: Port) {
    port.onmessage = (e) => this.receive(e.data);
    port.onerror = (e) => this.fail(`The simulation stopped: ${errorText(e)}`);
    port.onmessageerror = () => this.fail('The simulation stopped: a message could not be read.');
  }

  /** The error that stopped the Worker, or null while it runs. */
  get failed(): Error | null {
    return this.failure;
  }

  /** Calls `listener` once if the Worker dies (immediately if it already has). */
  onFailure(listener: (error: Error) => void): void {
    if (this.failure) listener(this.failure);
    else this.failListeners.push(listener);
  }

  private fail(message: string): void {
    if (this.failure) return;
    const error = new Error(message);
    this.failure = error;
    for (const p of this.pending.values()) p.reject(error);
    this.pending.clear();
    for (const l of this.failListeners.splice(0)) l(error);
  }

  /** Starts a Worker and a new world from the game data next to the page. */
  static async start(seed: number | bigint, files?: GameFiles): Promise<SimBridge> {
    const bridge = new SimBridge(SimBridge.spawn());
    return bridge.startWorld(files ?? (await loadGameFiles(defaultDataUrl())), seed);
  }

  /** Starts a Worker and restores a saved world. */
  static async load(bytes: Uint8Array, files?: GameFiles): Promise<SimBridge> {
    const bridge = new SimBridge(SimBridge.spawn());
    return bridge.loadWorld(files ?? (await loadGameFiles(defaultDataUrl())), bytes);
  }

  /** Starts a Worker and scenario `n` on the full-scale world (scenarios never save). */
  static async scenario(n: number): Promise<SimBridge> {
    const [files, text] = await Promise.all([loadGameFiles(scenarioDataUrl(), []), loadScenarioText(n)]);
    const bridge = new SimBridge(SimBridge.spawn());
    return bridge.startScenario(files, text);
  }

  /** Runs the full-scale free-mode benchmark in a dedicated simulation Worker. */
  static async fullscaleBench(): Promise<FullscaleBenchResult> {
    const base = scenarioDataUrl();
    const [files, turnsResponse] = await Promise.all([
      loadGameFiles(base, []),
      fetch(new URL('turns.json', base)),
    ]);
    if (!turnsResponse.ok) throw new Error(`Could not load turns.json: HTTP ${turnsResponse.status}`);
    const turns = (await turnsResponse.json()) as { turns: { world_hash: string }[] };
    const expectedHash11 = turns.turns[11]?.world_hash;
    if (!expectedHash11) throw new Error('turns.json has no turn 11 hash.');
    const bridge = new SimBridge(SimBridge.spawn());
    try {
      return await bridge.call<FullscaleBenchResult>({ op: 'bench', files, expectedHash11 });
    } finally {
      bridge.dispose();
    }
  }

  private static spawn(): Port {
    return new Worker(new URL('./worker.ts', import.meta.url), { type: 'module' }) as unknown as Port;
  }

  async startWorld(files: GameFiles, seed: number | bigint): Promise<this> {
    this.loadedFiles = files;
    this.loadedScenario = '';
    await this.call({ op: 'start', files, seed: BigInt(seed) });
    return this;
  }

  async loadWorld(files: GameFiles, bytes: Uint8Array): Promise<this> {
    this.loadedFiles = files;
    this.loadedScenario = '';
    await this.call({ op: 'load', files, bytes }, [bytes.buffer]);
    return this;
  }

  async startScenario(files: GameFiles, text: string): Promise<this> {
    this.loadedFiles = files;
    this.loadedScenario = text;
    await this.call({ op: 'scenario', files, text });
    return this;
  }

  /** Accepts a contract the scenario offered (takes effect at once, not next week). */
  async acceptContract(id: string): Promise<void> {
    await this.call({ op: 'accept', contract: id });
  }

  next(): Promise<TurnReport> {
    return this.call<TurnReport>({ op: 'next' });
  }

  async queue(command: PlayerCommand): Promise<void> {
    await this.call({ op: 'queue', command });
  }

  preview(command: PlayerCommand): Promise<CommandResult> {
    return this.call<CommandResult>({ op: 'preview', command });
  }

  view<K extends ViewName>(name: K, arg?: number): Promise<Views[K]> {
    return this.call<Views[K]>({ op: 'view', name, arg });
  }

  save(): Promise<Uint8Array> {
    return this.call<Uint8Array>({ op: 'save' });
  }

  turn(): Promise<number> {
    return this.call<number>({ op: 'turn' });
  }

  /** The world hash as a decimal string. */
  worldHash(): Promise<string> {
    return this.call<string>({ op: 'hash' });
  }

  /** Stops the Worker; calls still waiting reject. */
  dispose(): void {
    this.port.terminate?.();
    this.port.close?.();
    for (const p of this.pending.values()) p.reject(new Error('The simulation was closed.'));
    this.pending.clear();
  }

  private call<T>(body: RequestBody, transfer: Transferable[] = []): Promise<T> {
    const id = this.nextId++;
    return new Promise<T>((resolve, reject) => {
      if (this.failure) {
        reject(this.failure);
        return;
      }
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
      this.port.postMessage({ id, ...body } as Request, transfer);
    });
  }

  private receive(response: Response): void {
    const p = this.pending.get(response.id);
    if (!p) return;
    this.pending.delete(response.id);
    if (response.ok) p.resolve(response.value);
    else p.reject(new Error(response.message));
  }
}
