// The Worker's logic without the Worker: turns one Request into one Response against a WasmSim.
// worker.ts connects it to postMessage; the tests connect it to a MessageChannel.
import type { WasmSim } from './pkg/fes_wasm.js';
import type { Request, Response } from './protocol';

export interface HostResult {
  response: Response;
  /** Buffers to move (not copy) with the response. */
  transfer: Transferable[];
}

export interface Host {
  handle(request: Request): Promise<HostResult>;
}

export function createHost(Sim: typeof WasmSim): Host {
  let sim: WasmSim | null = null;
  const world = (): WasmSim => {
    if (!sim) throw new Error('No world is running: send start or load first.');
    return sim;
  };
  const replace = (next: WasmSim): null => {
    sim?.free();
    sim = next;
    return null;
  };

  function run(request: Request): unknown {
    switch (request.op) {
      case 'start':
        return replace(new Sim(request.files, request.seed));
      case 'load':
        return replace(Sim.load(request.files, request.bytes));
      case 'scenario':
        return replace(Sim.new_scenario(request.files, request.text));
      case 'accept':
        world().accept_contract(request.contract);
        return null;
      case 'next':
        return world().step();
      case 'queue':
        world().queue(request.command);
        return null;
      case 'preview':
        return world().preview(request.command);
      case 'view':
        return world().view(request.name, request.arg);
      case 'save':
        return world().save();
      case 'turn':
        return world().turn();
      case 'hash':
        return world().world_hash();
    }
  }

  return {
    handle(request) {
      try {
        const value = run(request);
        const transfer = value instanceof Uint8Array ? [value.buffer] : [];
        return Promise.resolve({ response: { id: request.id, ok: true, value }, transfer });
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        return Promise.resolve({ response: { id: request.id, ok: false, message }, transfer: [] });
      }
    },
  };
}

/**
 * Answers requests in order from a host that may still be starting. If the start failed, or a call
 * throws, the request still gets an `ok: false` answer, so the page never waits for ever.
 */
export function serve(ready: Promise<Host>, post: (response: Response, transfer: Transferable[]) => void): (request: Request) => void {
  ready.catch(() => undefined); // reported per request below
  let queue: Promise<void> = Promise.resolve();
  return (request) => {
    queue = queue
      .then(async () => {
        const host = await ready;
        const { response, transfer } = await host.handle(request);
        post(response, transfer);
      })
      .catch((error: unknown) => {
        const reason = error instanceof Error ? error.message : String(error);
        post({ id: request.id, ok: false, message: `The simulation failed: ${reason}` }, []);
      });
  };
}
