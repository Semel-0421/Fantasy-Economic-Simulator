// The simulation Worker: loads the wasm once and answers the bridge's requests, one at a time.
import init, { WasmSim } from './pkg/fes_wasm.js';
import wasmUrl from './pkg/fes_wasm_bg.wasm?url';
import { createHost } from './host';
import type { Request } from './protocol';

interface WorkerScope {
  onmessage: ((e: MessageEvent<Request>) => void) | null;
  postMessage(message: unknown, transfer: Transferable[]): void;
}
const scope = self as unknown as WorkerScope;

const ready = init({ module_or_path: wasmUrl }).then(() => createHost(WasmSim));

// Requests are chained so answers keep the order of the requests.
let queue: Promise<void> = Promise.resolve();
scope.onmessage = (e) => {
  queue = queue.then(async () => {
    const host = await ready;
    const { response, transfer } = await host.handle(e.data);
    scope.postMessage(response, transfer);
  });
};
