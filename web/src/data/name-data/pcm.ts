import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: pcm name tables. Latin script: the English fantasy place names are kept, so there are
// no curatedPlaces/starts/middles/ends. Same order and length as web/src/data/names.ts.
const curatedGoods: Record<string, string> = { g1_0: 'Gold wheat', g6_0: 'Smith tools', g17_5: 'Copper fitting', g19_0: 'Sunstone' };

// landforms (7): a suffix after the place name
const landforms = ['Valley', 'Swamp', 'Hills', 'Reach', 'Moor', 'Hollow', 'Border'];
// qualifiers (12): placed before the noun
const qualifiers = ['High land', 'Low land', 'Sea side', 'River side', 'North', 'South', 'East', 'West', 'Hill', 'Swamp', 'Valley', 'Old town'];
// Fallback goods name pattern: `Trade goods ${qualifier}`  (the English source is `Trade goods ${qualifier}`)

const categoryNouns = [
  'turnip|cabbage|onion|leek|carrot|beetroot',
  'wheat|barley|oat|rye|millet|spelt',
  'apple|pear|plum|cherry|fig|quince',
  'herring|cod|eel|trout|salmon|smoke fish',
  'goat meat|pig meat|cow meat|sausage|salt ham|deer meat',
  'cheese|butter|cream|curd|goat cheese|egg',
  'tool|axe|sickle|hammer|saw|plough',
  'oak wood|pine plank|ash pole|beam|roof tile|charcoal',
  'granite|slate|limestone|marble|brick|flagstone',
  'iron ore|copper ore|tin ore|lead ore|silver ore|coal',
  'iron bar|copper ingot|tin ingot|bronze bar|steel rod|lead sheet',
  'linen|wool cloth|canvas|felt|thread|cotton',
  'raw skin|leather|saddle|boot|belt|fur',
  'pot|jug|bowl|tile|glass ware|lamp',
  'ale|cider|honey wine|wine|pear wine|strong drink',
  'pepper|saffron|clove|salt|mustard|herb',
  'madder|woad|indigo|ochre|dye cake|ink',
  'copper fitting|hinge|nail|buckle|lock|chain',
  'chair|box|table|barrel|cart|wagon',
  'sunstone|amber|silk|pearl|perfume|jewel',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `Trade goods ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "pcm", patterns, banned: new RegExp("\\b(?:fuck|shit)\\b", "iu") };
