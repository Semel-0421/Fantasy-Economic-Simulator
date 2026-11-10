import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: pt name tables. Latin script: the English fantasy place names are kept.
// Fallback goods name: `Mercadorias ${qualifier}`; generated goods names use noun first: `${noun} ${qualifier}`.
export const curatedGoods: Record<string, string> = { g1_0: 'Trigo dourado', g6_0: 'Ferramentas de ferraria', g17_5: 'Ferragens de cobre', g19_0: 'Pedra solar' };
export const landforms = ['Vale', 'Pântano', 'Colinas', 'Margem', 'Charneca', 'Grota', 'Marca'];
export const qualifiers = ['das terras altas', 'das terras baixas', 'da costa', 'do rio', 'do norte', 'do sul', 'do leste', 'do oeste', 'da colina', 'do brejo', 'do vale', 'da cidade velha'];
export const categoryNouns = [
  'nabos|repolhos|cebolas|alhos-porós|cenouras|beterrabas',
  'trigo|cevada|aveia|centeio|painço|espelta',
  'maçãs|peras|ameixas|cerejas|figos|marmelos',
  'arenques|bacalhau|enguias|trutas|salmão|peixe defumado',
  'carneiro|porco|carne bovina|linguiças|presunto salgado|carne de veado',
  'queijo|manteiga|nata|coalhada|queijo de cabra|ovos',
  'ferramentas|machados|foices|martelos|serras|arados',
  'madeira de carvalho|tábuas de pinho|postes de freixo|vigas|telhas de madeira|carvão vegetal',
  'granito|ardósia|calcário|mármore|tijolos|lajes',
  'minério de ferro|minério de cobre|minério de estanho|minério de chumbo|minério de prata|carvão mineral',
  'barras de ferro|lingotes de cobre|lingotes de estanho|barras de bronze|varetas de aço|chapas de chumbo',
  'linho|pano de lã|lona|feltro|fio|algodão',
  'couros crus|couro curtido|selas|botas|cintos|peles',
  'cerâmica|jarros|tigelas|azulejos|objetos de vidro|lamparinas',
  'cerveja|cidra|hidromel|vinho|cidra de pera|destilados',
  'pimenta|açafrão|cravo-da-índia|sal|mostarda|ervas',
  'garança|pastel-dos-tintureiros|anil|ocre|pães de tinta|tinta de escrever',
  'ferragens de cobre|dobradiças|pregos|fivelas|fechaduras|correntes',
  'cadeiras|baús|mesas|barris|carroças|carroções',
  'pedra solar|âmbar|seda|pérolas|perfume|joias',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Mercadorias ${qualifier}`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "pt", patterns, banned: new RegExp("\\b(?:merda|caralho)\\b", "iu") };
