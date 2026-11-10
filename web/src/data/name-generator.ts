import type { NameData } from './name-types';
import { englishNames } from './name-data/en';

function hash(key: string): number {
  let value = 2166136261;
  for (const char of key) value = Math.imul(value ^ char.charCodeAt(0), 16777619);
  return value >>> 0;
}
const capitalise = (word: string): string => word.charAt(0).toUpperCase() + word.slice(1);
const humanise = (key: string): string => capitalise(key.replaceAll('_', ' '));
function stem(data: NameData, index: number): string {
  return capitalise(data.starts[index % data.starts.length]! + data.middles[Math.floor(index / data.starts.length) % data.middles.length]! + data.ends[Math.floor(index / (data.starts.length * data.middles.length)) % data.ends.length]!);
}

function probeIndex(data: NameData, candidate: (index: number) => string, capacity: number, used: Set<string>, place: boolean, start: number): number {
  let index = start, attempts = 0;
  // Probe the rendered string: distinct syllable indices can collapse in another script.
  while (used.has(candidate(index)) || data.banned.test(candidate(index)) || (place && englishNames.banned.test(stem(englishNames, index)))) {
    if (++attempts >= capacity) throw new Error(`Display name table exhausted: ${data.code}`);
    index = (index + 1) % capacity;
  }
  return index;
}

function allocate(data: NameData, keys: string[], curated: Record<string, string>, candidate: (index: number) => string, capacity: number, used: Set<string>, place = false): Map<string, string> {
  const names = new Map<string, string>();
  for (const key of keys) {
    let name = curated[key];
    if (!name) name = candidate(probeIndex(data, candidate, capacity, used, place, hash(key) % capacity));
    used.add(name); names.set(key, name);
  }
  return names;
}

function placeKeyLevels(): string[][] {
  const levels: string[][] = [[], [], [], [], []];
  for (let k = 0; k < 5; k++) {
    levels[0]!.push(`k${k}`);
    for (let n = 0; n < 5; n++) {
      const nation = `k${k}n${n}`; levels[1]!.push(nation);
      addRegions(levels, nation);
    }
  }
  return levels;
}

function addRegions(levels: string[][], nation: string): void {
  for (let r = 0; r < 2; r++) {
    const region = `${nation}r${r}`; levels[2]!.push(region);
    for (let c = 0; c < 5; c++) {
      const city = `${region}c${c}`; levels[3]!.push(city);
      for (let v = 0; v < 5; v++) levels[4]!.push(`${city}v${v}`);
    }
  }
}

export function createNames(data: NameData) {
  const levels = placeKeyLevels();
  const usedPlaces = new Set(Object.values(data.curatedPlaces));
  const places = new Map(levels.flatMap((keys, level) => [...allocate(data, keys, data.curatedPlaces, index => {
    const word = stem(data, index);
    return level === 2 ? data.patterns.place(word, data.landforms[index % data.landforms.length]!) : word;
  }, data.starts.length * data.middles.length * data.ends.length, usedPlaces, true)]));
  const usedGoods = new Set(Object.values(data.curatedGoods));
  const goods = new Map(data.categoryNouns.flatMap((nouns, category) => [...allocate(data,
    Array.from({ length: 50 }, (_, index) => `g${category}_${index}`), data.curatedGoods,
    index => data.patterns.goods(data.qualifiers[index % data.qualifiers.length]!, nouns[Math.floor(index / data.qualifiers.length)]!), data.qualifiers.length * nouns.length, usedGoods,
  )]));
  const placeName = (key: string): string => places.get(key) ?? humanise(key);
  const goodName = (key: string): string => goods.get(key) ?? (/^g\d+_\d+$/.test(key) ? data.patterns.fallback(data.qualifiers[hash(key) % data.qualifiers.length]!) : humanise(key));
  return { places, goods, placeName, goodName };
}
