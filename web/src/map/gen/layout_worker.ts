// Generates a map layout off the main thread, so building the world never stalls the page.
import { layoutHexMap } from '../hex_layout';
import type { MapData } from '../layout';

self.onmessage = (event: MessageEvent<{ map: MapData; seed: string }>) => {
  try {
    self.postMessage({ layout: layoutHexMap(event.data.map, event.data.seed) });
  } catch (e) {
    self.postMessage({ error: e instanceof Error ? e.message : String(e) });
  }
};
