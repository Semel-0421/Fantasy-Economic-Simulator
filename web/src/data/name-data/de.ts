import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: de name tables (Latin script: English fantasy place names are kept, so no place/syllable tables).
// Fallback goods name pattern: `${qualifier} ${noun}` (e.g. "Hochland Rüben"); fallback goods name: `Handelsware ${qualifier}`
const curatedGoods: Record<string, string> = { 'g1_0': 'Goldweizen', 'g6_0': 'Schmiedewerkzeuge', 'g17_5': 'Kupferbeschläge', 'g19_0': 'Sonnenstein' };
const landforms = ['Tal', 'Sumpf', 'Hügel', 'Ufer', 'Heide', 'Senke', 'Mark'];
const qualifiers = ['Hochland', 'Tiefland', 'Küsten', 'Fluss', 'Nord', 'Süd', 'Ost', 'West', 'Hügel', 'Sumpf', 'Tal', 'Altstadt'];
const categoryNouns = [
  'Rüben|Kohl|Zwiebeln|Lauch|Karotten|Rote Bete',
  'Weizen|Gerste|Hafer|Roggen|Hirse|Dinkel',
  'Äpfel|Birnen|Pflaumen|Kirschen|Feigen|Quitten',
  'Hering|Dorsch|Aale|Forellen|Lachs|Räucherfisch',
  'Hammelfleisch|Schweinefleisch|Rindfleisch|Würste|Pökelschinken|Wildbret',
  'Käse|Butter|Sahne|Quark|Ziegenkäse|Eier',
  'Werkzeuge|Äxte|Sicheln|Hämmer|Sägen|Pflüge',
  'Eichenholz|Kiefernbretter|Eschenstangen|Balken|Schindeln|Holzkohle',
  'Granit|Schiefer|Kalkstein|Marmor|Ziegel|Steinplatten',
  'Eisenerz|Kupfererz|Zinnerz|Bleierz|Silbererz|Kohle',
  'Eisenbarren|Kupferbarren|Zinnbarren|Bronzestangen|Stahlstäbe|Bleiplatten',
  'Leinen|Wolltuch|Segeltuch|Filz|Garn|Baumwolle',
  'Felle|Leder|Sättel|Stiefel|Gürtel|Pelze',
  'Töpferware|Krüge|Schalen|Fliesen|Glaswaren|Lampen',
  'Bier|Apfelwein|Met|Wein|Birnenwein|Branntwein',
  'Pfeffer|Safran|Nelken|Salz|Senf|Kräuter',
  'Krapp|Waid|Indigo|Ocker|Farbkuchen|Tinte',
  'Kupferbeschläge|Scharniere|Nägel|Schnallen|Schlösser|Ketten',
  'Stühle|Truhen|Tische|Fässer|Karren|Wagen',
  'Sonnenstein|Bernstein|Seide|Perlen|Parfüm|Schmuck',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `Handelsware ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "de", patterns, banned: new RegExp("\\b(?:scheiße|fotze)\\b", "iu") };
