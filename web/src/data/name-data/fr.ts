import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: fr name tables. Latin script: the English fantasy place names are kept.
// Fallback goods name: `Marchandises ${qualifier}`; generated goods names use noun first: `${noun} ${qualifier}`.
export const curatedGoods: Record<string, string> = { g1_0: 'Blé doré', g6_0: 'Outils de forge', g17_5: 'Ferrures en cuivre', g19_0: 'Pierre solaire' };
export const landforms = ['Val', 'Marais', 'Collines', 'Rive', 'Lande', 'Creux', 'Marche'];
export const qualifiers = ['des hautes terres', 'des basses terres', 'de la côte', 'de la rivière', 'du nord', 'du sud', 'de l\'est', 'de l\'ouest', 'de la colline', 'du marécage', 'de la vallée', 'de la vieille ville'];
export const categoryNouns = [
  'navets|choux|oignons|poireaux|carottes|betteraves',
  'blé|orge|avoine|seigle|millet|épeautre',
  'pommes|poires|prunes|cerises|figues|coings',
  'harengs|morue|anguilles|truites|saumon|poisson fumé',
  'mouton|porc|bœuf|saucisses|jambon salé|gibier',
  'fromage|beurre|crème|caillé|fromage de chèvre|œufs',
  'outils|haches|faucilles|marteaux|scies|charrues',
  'bois de chêne|planches de pin|perches de frêne|poutres|bardeaux|charbon de bois',
  'granit|ardoise|calcaire|marbre|briques|dalles',
  'minerai de fer|minerai de cuivre|minerai d\'étain|minerai de plomb|minerai d\'argent|houille',
  'barres de fer|lingots de cuivre|lingots d\'étain|barres de bronze|tiges d\'acier|plaques de plomb',
  'lin|drap de laine|toile|feutre|fil|coton',
  'peaux brutes|cuir|selles|bottes|ceintures|fourrures',
  'poterie|cruches|bols|carreaux|verrerie|lampes',
  'bière|cidre|hydromel|vin|poiré|spiritueux',
  'poivre|safran|clous de girofle|sel|moutarde|herbes',
  'garance|pastel|indigo|ocre|pains de teinture|encre',
  'ferrures en cuivre|charnières|clous|boucles|serrures|chaînes',
  'chaises|coffres|tables|tonneaux|charrettes|chariots',
  'pierre solaire|ambre|soie|perles|parfum|bijoux',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Marchandises ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "fr", patterns, banned: new RegExp("\\b(?:merde|putain)\\b", "iu") };
