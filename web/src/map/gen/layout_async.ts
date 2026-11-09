// Runs layoutHexMap in a Web Worker (falls back to the main thread where workers are missing).
import { layoutHexMap, type HexLayout } from '../hex_layout';
import type { MapData } from '../layout';

export async function layoutInWorker(map: MapData, seed: string): Promise<HexLayout> {
  if (typeof Worker !== 'function') return layoutHexMap(map, seed);
  const worker = new Worker(new URL('./layout_worker.ts', import.meta.url), { type: 'module' });
  try {
    return await new Promise<HexLayout>((resolve, reject) => {
      worker.onmessage = (event: MessageEvent<{ layout?: HexLayout; error?: string }>) =>
        event.data.layout ? resolve(event.data.layout) : reject(new Error(event.data.error ?? 'Map generation failed'));
      worker.onerror = (event) => reject(new Error(event.message || 'Map generation failed'));
      worker.postMessage({ map, seed });
    });
  } finally {
    worker.terminate();
  }
}
