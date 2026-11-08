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
