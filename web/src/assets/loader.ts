import { Assets, type Texture } from 'pixi.js';
import { MANIFEST } from './manifest';

export const textures = new Map<string, Texture>();
export const sounds = new Map<string, AudioBuffer>();
export const availableFiles = new Set<string>();
let context: AudioContext | null = null;

export function audioContext(): AudioContext | null {
  try { return context ??= new AudioContext(); } catch { return null; }
}

interface Decoders {
  image(path: string): Promise<unknown>;
  audio(path: string): Promise<unknown>;
}
const decoders: Decoders = {
  image: (path) => Assets.load<Texture>({ src: path, data: { scaleMode: 'nearest' } }),
  async audio(path) {
    const ctx = audioContext();
    if (!ctx) throw new Error('Audio unavailable');
    const res = await fetch(path);
    if (!res.ok) throw new Error('Audio unavailable');
    return ctx.decodeAudioData(await res.arrayBuffer());
  },
};

/** Only the contract's paths that the build found on disk can be requested. */
export async function loadAssets(present: readonly string[], deps: Decoders = decoders): Promise<void> {
  const files = new Set(present);
  availableFiles.clear();
  for (const asset of Object.values(MANIFEST)) if (files.has(asset.path)) availableFiles.add(asset.path);
  let failures = 0;
  await Promise.all(Object.entries(MANIFEST).filter(([, asset]) => files.has(asset.path)).map(async ([id, asset]) => {
    try {
      const path = `assets/${asset.path}`;
      if (asset.path.endsWith('.png')) {
        const texture = await deps.image(path) as Texture;
        texture.source.scaleMode = 'nearest';
        textures.set(id, texture);
      } else sounds.set(id, await deps.audio(path) as AudioBuffer);
    } catch { failures++; }
  }));
  if (failures) console.info(`Optional assets: ${failures} unavailable; using built-in presentation.`);
}

export async function prepareAssets(): Promise<void> {
  try {
    const res = await fetch('assets/present.json');
    if (!res.ok) return;
    const list: unknown = await res.json();
    if (Array.isArray(list)) await loadAssets(list.filter((path): path is string => typeof path === 'string'));
    if (textures.has('ui-frame')) document.documentElement.style.setProperty('--frame-image', 'url("assets/ui/frame.png")');
  } catch { console.info('Optional assets unavailable; using built-in presentation.'); }
}
