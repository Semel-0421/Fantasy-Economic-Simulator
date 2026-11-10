import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: tr name tables (Latin script: English fantasy place names are kept, so no place/syllable tables).
// Fallback goods name pattern: `${qualifier} ${noun}` (e.g. "Yayla Şalgam", no suffix needed); fallback goods name: `Ticaret malı ${qualifier}`
export const curatedGoods: Record<string, string> = { 'g1_0': 'Altın buğday', 'g6_0': 'Demirci aletleri', 'g17_5': 'Bakır bağlantılar', 'g19_0': 'Güneş taşı' };
export const landforms = ['Vadi', 'Bataklık', 'Tepeler', 'Kıyı', 'Bozkır', 'Çukur', 'Sınır'];
export const qualifiers = ['Yayla', 'Ova', 'Kıyı', 'Nehir', 'Kuzey', 'Güney', 'Doğu', 'Batı', 'Tepe', 'Bataklık', 'Vadi', 'Eski şehir'];
export const categoryNouns = [
  'Şalgam|Lahana|Soğan|Pırasa|Havuç|Pancar',
  'Buğday|Arpa|Yulaf|Çavdar|Darı|Alaca buğday',
  'Elma|Armut|Erik|Kiraz|İncir|Ayva',
  'Ringa|Morina|Yılan balığı|Alabalık|Somon|Füme balık',
  'Koyun eti|Domuz eti|Sığır eti|Sosis|Tuzlu jambon|Geyik eti',
  'Peynir|Tereyağı|Krema|Lor|Keçi peyniri|Yumurta',
  'Aletler|Baltalar|Oraklar|Çekiçler|Testereler|Sabanlar',
  'Meşe kereste|Çam tahta|Dişbudak sırık|Kirişler|Çatı levhası|Odun kömürü',
  'Granit|Arduvaz|Kireçtaşı|Mermer|Tuğla|Döşeme taşı',
  'Demir cevheri|Bakır cevheri|Kalay cevheri|Kurşun cevheri|Gümüş cevheri|Kömür',
  'Demir çubuk|Bakır külçe|Kalay külçe|Bronz çubuk|Çelik çubuk|Kurşun levha',
  'Keten|Yün kumaş|Branda bezi|Keçe|İplik|Pamuk',
  'Post|Deri|Eyer|Çizme|Kemer|Kürk',
  'Çömlek|Testi|Kase|Çini|Cam eşya|Kandil',
  'Bira|Elma şarabı|Bal şarabı|Şarap|Armut şarabı|Sert içki',
  'Karabiber|Safran|Karanfil|Tuz|Hardal|Şifalı otlar',
  'Kökboya|Çivit otu|Çivit|Aşı boyası|Boya kalıbı|Mürekkep',
  'Bakır bağlantılar|Menteşe|Çivi|Toka|Kilit|Zincir',
  'Sandalye|Sandık|Masa|Fıçı|El arabası|Yük arabası',
  'Güneş taşı|Kehribar|İpek|İnci|Parfüm|Mücevher',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `Ticaret malı ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "tr", patterns, banned: new RegExp("\\b(?:siktir|orospu)\\b", "iu") };
