import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, describe, expect, it } from 'vitest';
import { BROKEN_MESSAGE, SaveStore, SLOTS } from '../src/save/store';

let store: SaveStore;
const bytes = (...n: number[]): Uint8Array => new Uint8Array(n);

beforeEach(async () => {
  store = await SaveStore.open(new IDBFactory());
});

describe('save store', () => {
  it('lists every slot as empty at first', async () => {
    const list = await store.list();
    expect(list.map((s) => s.slot)).toEqual([...SLOTS]);
    expect(list.every((s) => s.status === 'empty')).toBe(true);
    expect(await store.latest()).toBeNull();
  });

  it('round-trips bytes and turn in a free-mode slot', async () => {
    await store.write('manual1', 'free', bytes(1, 2, 3), 7);
    expect(Array.from(await store.read('manual1'))).toEqual([1, 2, 3]);
    const info = (await store.list()).find((s) => s.slot === 'manual1');
    expect(info).toMatchObject({ status: 'ok', turn: 7 });
  });

  it('never saves a scenario', async () => {
    await expect(store.write('auto', 'scenario', bytes(1), 1)).rejects.toThrow(/scenario/i);
    expect((await store.list()).every((s) => s.status === 'empty')).toBe(true);
  });

  it('rejects unknown slots', async () => {
    await expect(store.write('manual9' as never, 'free', bytes(1), 1)).rejects.toThrow(/slot/i);
  });

  it('overwrites a slot', async () => {
    await store.write('auto', 'free', bytes(1), 1);
    await store.write('auto', 'free', bytes(9), 2);
    expect(Array.from(await store.read('auto'))).toEqual([9]);
  });

  it('latest is the most recently saved slot', async () => {
    await store.write('manual2', 'free', bytes(1), 3);
    await new Promise((r) => setTimeout(r, 5));
    await store.write('auto', 'free', bytes(2), 4);
    expect((await store.latest())?.slot).toBe('auto');
  });

  it('shows a corrupt entry as broken, keeps the others, and refuses to read it', async () => {
    await store.write('auto', 'free', bytes(1, 2), 1);
    await store.write('manual1', 'free', bytes(5, 6), 2);
    await store.putRaw('manual2', { format: 'something-else', bytes: 'nope' });
    await store.putRaw('manual3', { format: 'fes-save-1', mode: 'free', turn: 1, savedAt: 1, bytes: bytes(1), checksum: 0 });
    const list = await store.list();
    const by = (s: string) => list.find((x) => x.slot === s);
    expect(by('manual2')).toMatchObject({ status: 'broken', message: BROKEN_MESSAGE });
    expect(by('manual3')?.status).toBe('broken');
    expect(by('auto')?.status).toBe('ok');
    expect(by('manual1')?.status).toBe('ok');
    await expect(store.read('manual2')).rejects.toThrow(BROKEN_MESSAGE);
    expect(Array.from(await store.read('manual1'))).toEqual([5, 6]);
    expect((await store.latest())?.status).toBe('ok');
  });
});
