// Free-mode saves in IndexedDB: an autosave slot (written each Next week) and three manual slots.
// Scenarios never save (spec 9.2). A corrupt or foreign entry is reported as broken and never
// blocks the other slots.
export const SLOTS = ['auto', 'manual1', 'manual2', 'manual3'] as const;
export type Slot = (typeof SLOTS)[number];
export type SaveMode = 'free' | 'scenario';

export const BROKEN_MESSAGE = "This save can't be loaded.";

const DB_NAME = 'fes-saves';
const STORE = 'slots';
const FORMAT = 'fes-save-1';

export interface SlotInfo {
  slot: Slot;
  status: 'empty' | 'ok' | 'broken';
  turn?: number;
  savedAt?: number;
  message?: string;
}

interface Entry {
  format: string;
  mode: 'free';
  turn: number;
  savedAt: number;
  bytes: Uint8Array;
  checksum: number;
  /** The seed the map was generated from (absent in saves made before seeded maps). */
  mapSeed?: string;
}

/** FNV-1a over the bytes: catches truncated or tampered entries. */
function checksum(bytes: Uint8Array): number {
  let h = 0x811c9dc5;
  for (const b of bytes) h = Math.imul(h ^ b, 0x01000193) >>> 0;
  return h;
}

function isEntry(v: unknown): v is Entry {
  if (typeof v !== 'object' || v === null) return false;
  const e = v as Record<string, unknown>;
  return (
    e.format === FORMAT &&
    e.mode === 'free' &&
    typeof e.turn === 'number' &&
    typeof e.savedAt === 'number' &&
    e.bytes instanceof Uint8Array &&
    e.checksum === checksum(e.bytes)
  );
}

const done = <T>(req: IDBRequest<T>): Promise<T> =>
  new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error("Your browser couldn't open its save storage."));
  });

export class SaveStore {
  private constructor(private readonly db: IDBDatabase) {}

  static async open(factory: IDBFactory = indexedDB): Promise<SaveStore> {
    const req = factory.open(DB_NAME, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    return new SaveStore(await done(req));
  }

  close(): void {
    this.db.close();
  }

  async write(slot: Slot, mode: SaveMode, bytes: Uint8Array, turn: number, mapSeed?: string): Promise<void> {
    if (mode === 'scenario') throw new Error("Scenarios start fresh each time and can't be saved.");
    if (!SLOTS.includes(slot)) throw new Error(`Unknown save slot: ${String(slot)}`);
    const entry: Entry = { format: FORMAT, mode: 'free', turn, savedAt: Date.now(), bytes: bytes.slice(), checksum: checksum(bytes), ...(mapSeed ? { mapSeed } : {}) };
    await this.putRaw(slot, entry);
  }

  /** Stores any value in a slot; used by tests to plant corrupt entries. */
  async putRaw(slot: Slot, value: unknown): Promise<void> {
    const tx = this.db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).put(value, slot);
    await new Promise<void>((resolve, reject) => {
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error ?? new Error('Save failed.'));
      tx.onabort = () => reject(tx.error ?? new Error('Save failed.'));
    });
  }

  async list(): Promise<SlotInfo[]> {
    const store = this.db.transaction(STORE).objectStore(STORE);
    const out: SlotInfo[] = [];
    for (const slot of SLOTS) {
      const v: unknown = await done(store.get(slot));
      if (v === undefined) out.push({ slot, status: 'empty' });
      else if (isEntry(v)) out.push({ slot, status: 'ok', turn: v.turn, savedAt: v.savedAt });
      else out.push({ slot, status: 'broken', message: BROKEN_MESSAGE });
    }
    return out;
  }

  /** The most recently written loadable slot, or null. */
  async latest(): Promise<SlotInfo | null> {
    const ok = (await this.list()).filter((s) => s.status === 'ok');
    ok.sort((a, b) => (b.savedAt ?? 0) - (a.savedAt ?? 0));
    return ok[0] ?? null;
  }

  /** The map seed stored with a slot, if any. */
  async mapSeedOf(slot: Slot): Promise<string | undefined> {
    const v: unknown = await done(this.db.transaction(STORE).objectStore(STORE).get(slot));
    return isEntry(v) && typeof v.mapSeed === 'string' ? v.mapSeed : undefined;
  }

  async read(slot: Slot): Promise<Uint8Array> {
    const v: unknown = await done(this.db.transaction(STORE).objectStore(STORE).get(slot));
    if (!isEntry(v)) throw new Error(BROKEN_MESSAGE);
    return v.bytes.slice();
  }
}
