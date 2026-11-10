import type { NameData } from '../name-types';
// UI-only names for the shipped full-scale key namespace; engine keys and save bytes stay intact.
export const curatedPlaces: Record<string, string> = {
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
export const curatedGoods: Record<string, string> = { g1_0: 'Golden wheat', g6_0: 'Smithed tools', g17_5: 'Copper fittings', g19_0: 'Sunstone' };
export const starts = 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'.split(' ');
export const middles = 'a ba de en fa ga ha i la me na o ra se ta ve'.split(' ');
export const ends = 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'.split(' ');
export const landforms = ['Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March'];
export const qualifiers = ['Highland', 'Lowland', 'Coastal', 'River', 'Northern', 'Southern', 'Eastern', 'Western', 'Hill', 'Marsh', 'Valley', 'Old-town'];
export const categoryNouns = [
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
export const banned = /fag|nig|cum|tit|rap|nazi|kkk|shit|fuck|dick|cock|slut|whor|ass/i;


export const englishNames: NameData = {code: "en", curatedPlaces, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, banned, patterns: { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `Trade goods ${qualifier}`, place: (name,landform) => `${name} ${landform}` }};
