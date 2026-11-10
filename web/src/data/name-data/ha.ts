import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: ha name tables. Latin script: the English fantasy place names are kept, so there are
// no curatedPlaces/starts/middles/ends. Same order and length as web/src/data/names.ts.
const curatedGoods: Record<string, string> = { g1_0: 'Alkamar zinariya', g6_0: 'Kayan aikin maƙera', g17_5: 'Kayan jan ƙarfe', g19_0: 'Dutsen rana' };

// landforms (7): a prefix before the place name
const landforms = ['Kwari', 'Fadama', 'Tuddai', 'Yanki', 'Fili', 'Rami', 'Iyaka'];
// qualifiers (12): placed after the noun
const qualifiers = ['na tudu', 'na ƙasa', 'na bakin teku', 'na kogi', 'na arewa', 'na kudu', 'na gabas', 'na yamma', 'na tsauni', 'na fadama', 'na kwari', 'na tsohon gari'];
// Fallback goods name pattern: `Kayan ciniki ${qualifier}`  (the English source is `Trade goods ${qualifier}`)

const categoryNouns = [
  'turnif|kabeji|albasa|lik|karas|gwoza',
  "alkama|sha'ir|oatsi|hatsin rai|gero|alkamar spelt",
  'tuffa|pia|plam|cheri|ɓaure|kwins',
  'kifin herin|kifin kod|kifin maciji|kifin trawt|kifin salmon|kifi busasshe',
  'naman tunkiya|naman alade|naman sa|tsiran alade|naman alade mai gishiri|naman barewa',
  'cuku|man shanu|kirim|madara mai kauri|cukun akuya|ƙwai',
  'kayan aiki|gatari|lauje|guduma|zarto|garma',
  'katakon oak|allunan pine|sandunan ash|ginshiƙan katako|shingil|gawayi',
  'dutsen granite|dutsen slate|dutsen farar ƙasa|marmara|bulo|dutsen shimfiɗa',
  'ɗanyen ƙarfe|ɗanyen jan ƙarfe|ɗanyen kwano|ɗanyen darma|ɗanyen azurfa|kwal',
  'sandunan ƙarfe|tubalin jan ƙarfe|tubalin kwano|sandunan tagulla|sandunan ƙarfen ƙera|faranti na darma',
  'lilin|zanen ulu|zanen tanti|felt|zare|auduga',
  'ɗanyar fata|fatar da aka sarrafa|sirdi|takalmi|ɗamara|fatun gashi',
  'tukwane|tuluna|kwanoni|tayal|kayan gilashi|fitilu',
  'giyar ale|ruwan tuffa|giyar zuma|ruwan inabi|giyar pia|giya mai ƙarfi',
  'barkono|saffron|kanunfari|gishiri|mustard|ganyaye',
  'madder|woad|indigo|jan ƙasa|ƙullin rini|tawada',
  'kayan jan ƙarfe|hinji|ƙusoshi|abin ɗaure ɗamara|makulli|sarƙoƙi',
  'kujeru|akwatuna|teburori|ganga|amalanke|karusa',
  "dutsen rana|ambar|siliki|lu'ulu'u|turare|kayan ado",
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Kayan ciniki ${qualifier}`, place: (name,landform) => `${landform} ${name}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "ha", patterns, banned: new RegExp("\\b(?:shege)\\b", "iu") };
