// The page's handle on the simulation Worker: every method is one typed message and a promise.
import { defaultDataUrl, loadGameFiles, type GameFiles } from '../data/files';
import type { Request, RequestBody, Response } from './protocol';
import type { CommandResult, PlayerCommand, TurnReport, ViewName, Views } from './types';

/** What a Worker and a MessagePort have in common. */
export interface Port {
  postMessage(message: Request, transfer?: Transferable[]): void;
  onmessage: ((e: MessageEvent<Response>) => void) | null;
  terminate?(): void;
  close?(): void;
}

interface Pending {
  resolve(value: unknown): void;
  reject(reason: Error): void;
}

export class SimBridge {
  private nextId = 1;
  private readonly pending = new Map<number, Pending>();

  constructor(private readonly port: Port) {
    port.onmessage = (e) => this.receive(e.data);
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

  private static spawn(): Port {
    return new Worker(new URL('./worker.ts', import.meta.url), { type: 'module' }) as unknown as Port;
  }

  async startWorld(files: GameFiles, seed: number | bigint): Promise<this> {
    await this.call({ op: 'start', files, seed: BigInt(seed) });
    return this;
  }

  async loadWorld(files: GameFiles, bytes: Uint8Array): Promise<this> {
    await this.call({ op: 'load', files, bytes }, [bytes.buffer]);
    return this;
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
