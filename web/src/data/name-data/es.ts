import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: es name tables. Latin script: the English fantasy place names are kept.
// Fallback goods name: `Mercancías ${qualifier}`; generated goods names use noun first: `${noun} ${qualifier}`.
export const curatedGoods: Record<string, string> = { g1_0: 'Trigo dorado', g6_0: 'Herramientas de herrería', g17_5: 'Herrajes de cobre', g19_0: 'Piedra solar' };
export const landforms = ['Valle', 'Ciénaga', 'Colinas', 'Ribera', 'Páramo', 'Hondonada', 'Marca'];
export const qualifiers = ['de las tierras altas', 'de las tierras bajas', 'de la costa', 'del río', 'del norte', 'del sur', 'del este', 'del oeste', 'de la colina', 'del pantano', 'del valle', 'del casco antiguo'];
export const categoryNouns = [
  'nabos|coles|cebollas|puerros|zanahorias|remolachas',
  'trigo|cebada|avena|centeno|mijo|espelta',
  'manzanas|peras|ciruelas|cerezas|higos|membrillos',
  'arenques|bacalao|anguilas|truchas|salmón|pescado ahumado',
  'cordero|cerdo|ternera|salchichas|jamón salado|venado',
  'queso|mantequilla|nata|cuajada|queso de cabra|huevos',
  'herramientas|hachas|hoces|martillos|sierras|arados',
  'madera de roble|tablones de pino|postes de fresno|vigas|tejas de madera|carbón vegetal',
  'granito|pizarra|caliza|mármol|ladrillos|losas',
  'mineral de hierro|mineral de cobre|mineral de estaño|mineral de plomo|mineral de plata|carbón mineral',
  'barras de hierro|lingotes de cobre|lingotes de estaño|barras de bronce|varillas de acero|láminas de plomo',
  'lino|paño de lana|lona|fieltro|hilo|algodón',
  'pieles crudas|cuero|sillas de montar|botas|cinturones|pieles finas',
  'cerámica|jarras|cuencos|azulejos|cristalería|lámparas',
  'cerveza|sidra|hidromiel|vino|sidra de pera|licores',
  'pimienta|azafrán|clavo de olor|sal|mostaza|hierbas',
  'rubia|glasto|índigo|ocre|panes de tinte|tinta',
  'herrajes de cobre|bisagras|clavos|hebillas|cerraduras|cadenas',
  'sillas|cofres|mesas|barriles|carretas|carros',
  'piedra solar|ámbar|seda|perlas|perfume|joyería',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Mercancías ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "es", patterns, banned: new RegExp("\\b(?:mierda|joder)\\b", "iu") };
