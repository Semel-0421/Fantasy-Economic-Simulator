import { readFileSync } from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import { MANIFEST, goodAsset } from '../src/assets/manifest';
import { loadAssets } from '../src/assets/loader';
import { play, setVolume } from '../src/audio/sound';

describe('optional assets', () => {
  it('matches every README id, path and size', () => {
    const readme = readFileSync('public/assets/README.md', 'utf8');
    const rows = [...readme.matchAll(/\| `([^`]+)` \| `([^`]+\.(?:png|ogg))` \| ([^|]+) \|/g)]
      .map((m) => ({ id: m[1], path: m[2], size: m[3]!.trim() }));
    expect(Object.entries(MANIFEST).map(([id, value]) => ({ id, ...value }))).toEqual(rows);
  });
  it('maps all 1,029 base keys and their grades', () => {
    const keys = ['../Assets/StreamingAssets/Data/goods.csv', '../engine/fixtures/fullscale/goods.csv'].flatMap((file) =>
      readFileSync(file, 'utf8').trim().split(/\r?\n/).filter((line) => !line.startsWith('#')).slice(1).map((line) => line.split(',')[0]!));
    expect(keys).toHaveLength(1029);
    for (const key of keys) {
      expect(MANIFEST[goodAsset(key)]).toBeDefined();
      expect(goodAsset(`${key}@high`)).toBe(goodAsset(key));
    }
    expect(goodAsset('g1_0')).toBe('good-wheat');
    expect(goodAsset('g6_0')).toBe('good-iron_tools');
    expect(goodAsset('g19_0')).toBe('good-regional-specialty');
  });
  it('requests nothing with no present files, and only allowlisted present paths', async () => {
    const image = vi.fn(async () => ({ source: { scaleMode: 'linear' } }));
    const audio = vi.fn(async () => ({}));
    const deps = { image, audio };
    await loadAssets([], deps);
    expect(image).not.toHaveBeenCalled();
    expect(audio).not.toHaveBeenCalled();
    await loadAssets(['goods/wheat.png', 'audio/sfx/coin.ogg', '../outside.png', 'goods/wheat.png'], deps);
    expect(image.mock.calls).toEqual([['assets/goods/wheat.png']]);
    expect(audio.mock.calls).toEqual([['assets/audio/sfx/coin.ogg']]);
  });
  it('silently falls back on decode failure with one summary', async () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => {});
    await loadAssets(['goods/wheat.png', 'goods/barley.png'], {
      image: async () => { throw new Error('bad PNG'); }, audio: async () => ({}),
    });
    expect(info).toHaveBeenCalledTimes(1);
    info.mockRestore();
  });
  it('volume zero creates no audio context or sources', () => {
    const ctor = vi.fn();
    vi.stubGlobal('AudioContext', ctor);
    setVolume(0);
    play('coin');
    expect(ctor).not.toHaveBeenCalled();
    setVolume(70);
    vi.unstubAllGlobals();
  });
});
