// UI-only names for the shipped full-scale key namespace; engine keys and save bytes stay intact.
const curatedPlaces: Record<string, string> = {
  k0: 'Aldermere', k1: 'Kessrin', k2: 'Solvane', k3: 'Thalgard', k4: 'Embervale',
  k0n0: 'Varn', k0n1: 'Halvesh', k0n2: 'Corwyth', k0n3: 'Brannock', k0n4: 'Elsmere',
  k1n0: 'Dravik', k1n1: 'Seravel', k1n2: 'Morrowfen', k1n3: 'Ystrel', k1n4: 'Quellmark',
  k2n0: 'Ashkar', k2n1: 'Tamaris', k2n2: 'Ondrea', k2n3: 'Zephran', k2n4: 'Calvos',
  k3n0: 'Grimholt', k3n1: 'Norvik', k3n2: 'Skelde', k3n3: 'Hrothmar', k3n4: 'Isenwald',
  k4n0: 'Pyrran', k4n1: 'Lumeth', k4n2: 'Cindrel', k4n3: 'Varrowmoor', k4n4: 'Solace',
  k0n0r0: 'Ashford Vale', k0n0r1: 'Greyfen',
  k0n0r0c0: 'Ashford', k0n0r0c1: 'Thornhold', k0n0r0c2: 'Fernley', k0n0r0c3: 'Oakhurst', k0n0r0c4: 'Saltmarsh',
  k0n0r1c4: 'Ravensgate', k0n0r0c0v1: 'Millbrook',
};
const curatedGoods: Record<string, string> = { g1_0: 'Golden wheat', g6_0: 'Smithed tools', g17_5: 'Copper fittings', g19_0: 'Sunstone' };
const starts = 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'.split(' ');
const middles = 'a ba de en fa ga ha i la me na o ra se ta ve'.split(' ');
const ends = 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'.split(' ');
const landforms = ['Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March'];
const qualifiers = ['Highland', 'Lowland', 'Coastal', 'River', 'Northern', 'Southern', 'Eastern', 'Western', 'Hill', 'Marsh', 'Valley', 'Old-town'];
const categoryNouns = [
  'turnips|cabbages|onions|leeks|carrots|beets', 'wheat|barley|oats|rye|millet|spelt',
  'apples|pears|plums|cherries|figs|quinces', 'herring|cod|eels|trout|salmon|smoked fish',
  'mutton|pork|beef|sausages|salted ham|venison', 'cheese|butter|cream|curds|goat cheese|eggs',
  'tools|axes|sickles|hammers|saws|ploughs', 'oak timber|pine planks|ash poles|beams|shingles|charcoal',
  'granite|slate|limestone|marble|bricks|flagstones', 'iron ore|copper ore|tin ore|lead ore|silver ore|coal',
  'iron bars|copper ingots|tin ingots|bronze bars|steel rods|lead sheets', 'linen|wool cloth|canvas|felt|yarn|cotton',
  'hides|leather|saddles|boots|belts|furs', 'pottery|jugs|bowls|tiles|glassware|lamps',
  'ale|cider|mead|wine|perry|spirits', 'pepper|saffron|cloves|salt|mustard|herbs',
  'madder|woad|indigo|ochre|dye cakes|ink', 'copper fittings|hinges|nails|buckles|locks|chains',
  'chairs|chests|tables|barrels|carts|wagons', 'sunstone|amber|silk|pearls|perfume|jewellery',
].map((words) => words.split('|'));
const banned = /fag|nig|cum|tit|rap|nazi|kkk|shit|fuck|dick|cock|slut|whor|ass/i;

function hash(key: string): number {
  let value = 2166136261;
  for (const char of key) value = Math.imul(value ^ char.charCodeAt(0), 16777619);
  return value >>> 0;
}
const capitalise = (word: string): string => word.charAt(0).toUpperCase() + word.slice(1);
const humanise = (key: string): string => capitalise(key.replaceAll('_', ' '));

function generatedPlace(index: number, level: number): string {
  const word = capitalise(starts[index % starts.length]! + middles[Math.floor(index / starts.length) % middles.length]! + ends[Math.floor(index / (starts.length * middles.length)) % ends.length]!);
  return level === 2 ? `${word} ${landforms[index % landforms.length]}` : word;
}

function allocate(keys: string[], curated: Record<string, string>, candidate: (index: number) => string, capacity: number, used = new Set(Object.values(curated))): Map<string, string> {
  const names = new Map<string, string>();
  for (const key of keys) {
    let name = curated[key];
    if (!name) {
      let index = hash(key) % capacity;
      let attempts = 0;
      while (used.has(candidate(index)) || banned.test(candidate(index))) {
        if (++attempts >= capacity) throw new Error('Display name table exhausted');
        index = (index + 1) % capacity;
      }
      name = candidate(index);
    }
    used.add(name);
    names.set(key, name);
  }
  return names;
}

// Allocate in canonical key order, so collisions never depend on which panel was opened first.
const levels: string[][] = [[], [], [], [], []];
for (let k = 0; k < 5; k++) {
  levels[0]!.push(`k${k}`);
  for (let n = 0; n < 5; n++) {
    const nation = `k${k}n${n}`;
    levels[1]!.push(nation);
    for (let r = 0; r < 2; r++) {
      const region = `${nation}r${r}`;
      levels[2]!.push(region);
      for (let c = 0; c < 5; c++) {
        const city = `${region}c${c}`;
        levels[3]!.push(city);
        for (let v = 0; v < 5; v++) levels[4]!.push(`${city}v${v}`);
      }
    }
  }
}
const usedPlaces = new Set(Object.values(curatedPlaces));
const places = new Map(levels.flatMap((keys, level) => [...allocate(keys, curatedPlaces, (index) => generatedPlace(index, level), starts.length * middles.length * ends.length, usedPlaces)]));
// The shipped goods.csv assigns gC_0..gC_49 to catC; the CSV contract test checks this correspondence.
const goods = new Map(categoryNouns.flatMap((nouns, category) => [...allocate(
  Array.from({ length: 50 }, (_, index) => `g${category}_${index}`), curatedGoods,
  (index) => `${qualifiers[index % qualifiers.length]} ${nouns[Math.floor(index / qualifiers.length)]}`, qualifiers.length * nouns.length,
)]));

export const placeName = (key: string): string => places.get(key) ?? humanise(key);
export const goodName = (key: string): string => goods.get(key) ?? (/^g\d+_\d+$/.test(key) ? `Trade goods ${qualifiers[hash(key) % qualifiers.length]}` : humanise(key));

/** Replace complete key tokens only, preserving surrounding punctuation and ordinary words. */
export function displayText(text: string): string {
  return text.replace(/\b(k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?|g\d+_\d+)(?:@([a-z]+))?\b/g, (token: string, key: string, grade: string | undefined) => {
    const name = key.startsWith('g') ? goodName(key) : placeName(key);
    return grade ? `${name} (${grade})` : name || token;
  });
}
