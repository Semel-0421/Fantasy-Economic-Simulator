import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: id name tables (Latin script: English fantasy place names are kept, so no place/syllable tables).
// Fallback goods name pattern: NOTE: Indonesian puts the qualifier AFTER the noun (e.g. "Wortel Dataran Tinggi"); intended pattern `${noun} ${qualifier}`; fallback goods name: `Barang dagangan ${qualifier}`
const curatedGoods: Record<string, string> = { 'g1_0': 'Gandum emas', 'g6_0': 'Perkakas tempaan', 'g17_5': 'Perlengkapan tembaga', 'g19_0': 'Batu surya' };
const landforms = ['Lembah', 'Rawa', 'Bukit', 'Tepian', 'Padang', 'Cekungan', 'Perbatasan'];
const qualifiers = ['Dataran Tinggi', 'Dataran Rendah', 'Pesisir', 'Sungai', 'Utara', 'Selatan', 'Timur', 'Barat', 'Bukit', 'Rawa', 'Lembah', 'Kota Lama'];
const categoryNouns = [
  'Lobak|Kubis|Bawang bombai|Bawang perai|Wortel|Bit',
  'Gandum|Jelai|Haver|Gandum hitam|Millet|Gandum spelta',
  'Apel|Pir|Prem|Ceri|Ara|Kwins',
  'Haring|Kod|Belut|Trout|Salmon|Ikan asap',
  'Daging domba|Daging babi|Daging sapi|Sosis|Ham asin|Daging rusa',
  'Keju|Mentega|Krim|Dadih|Keju kambing|Telur',
  'Perkakas|Kapak|Sabit|Palu|Gergaji|Bajak',
  'Kayu ek|Papan pinus|Tiang ash|Balok|Sirap|Arang',
  'Granit|Batu tulis|Batu kapur|Marmer|Batu bata|Batu lantai',
  'Bijih besi|Bijih tembaga|Bijih timah|Bijih timbal|Bijih perak|Batu bara',
  'Batang besi|Batangan tembaga|Batangan timah|Batang perunggu|Batang baja|Lembaran timbal',
  'Linen|Kain wol|Kanvas|Kain kempa|Benang|Katun',
  'Kulit mentah|Kulit samak|Pelana|Sepatu bot|Ikat pinggang|Bulu binatang',
  'Tembikar|Kendi|Mangkuk|Ubin|Barang kaca|Lampu',
  'Bir|Sari apel|Mead|Anggur|Sari pir|Minuman keras',
  'Lada|Safron|Cengkeh|Garam|Mostar|Rempah herbal',
  'Akar madder|Tanaman woad|Nila|Oker|Kue pewarna|Tinta',
  'Perlengkapan tembaga|Engsel|Paku|Gesper|Kunci|Rantai',
  'Kursi|Peti|Meja|Tong|Gerobak|Kereta barang',
  'Batu surya|Ambar|Sutra|Mutiara|Parfum|Perhiasan',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Barang dagangan ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "id", patterns, banned: new RegExp("\\b(?:kontol|memek)\\b", "iu") };
