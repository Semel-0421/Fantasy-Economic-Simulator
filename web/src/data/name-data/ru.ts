import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: ru name tables, same order and length as web/src/data/names.ts.
// Places are transliterations of the English fantasy names; the syllable tables map one to one.
export const curatedPlaces: Record<string, string> = {
  k0: 'Олдермир', k1: 'Кессрин', k2: 'Солвейн', k3: 'Талгард', k4: 'Эмбервейл',
  k0n0: 'Варн', k0n1: 'Халвеш', k0n2: 'Корвит', k0n3: 'Браннок', k0n4: 'Элсмир',
  k1n0: 'Дравик', k1n1: 'Серавел', k1n2: 'Морроуфен', k1n3: 'Истрел', k1n4: 'Куэллмарк',
  k2n0: 'Ашкар', k2n1: 'Тамарис', k2n2: 'Ондреа', k2n3: 'Зефран', k2n4: 'Калвос',
  k3n0: 'Гримхольт', k3n1: 'Норвик', k3n2: 'Скельде', k3n3: 'Хротмар', k3n4: 'Изенвальд',
  k4n0: 'Пирран', k4n1: 'Лумет', k4n2: 'Синдрел', k4n3: 'Варроумур', k4n4: 'Солас',
  k0n0r0: 'Эшфорд-Вейл', k0n0r1: 'Грейфен', k0n0r0c0: 'Эшфорд', k0n0r0c1: 'Торнхолд', k0n0r0c2: 'Ферли',
  k0n0r0c3: 'Оакхерст', k0n0r0c4: 'Солтмарш', k0n0r1c4: 'Рейвенсгейт', k0n0r0c0v1: 'Миллбрук',
};
export const curatedGoods: Record<string, string> = { g1_0: 'Золотая пшеница', g6_0: 'Кованые инструменты', g17_5: 'Медная отделка', g19_0: 'Камень солнца' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
export const starts = 'Ал Бел Кор Дал Эл Фал Гал Хал Ил Кел Лор Мел Нор Ор Пел Вел'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
export const middles = 'а ба де эн фа га ха и ла ме на о ра се та ве'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
export const ends = 'бан ден фен гар лен мер мон нар рен рис сар тен тор вал вен вин'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
export const landforms = ['Долина', 'Болото', 'Холмы', 'Прибрежье', 'Пустошь', 'Лощина', 'Марка'];
// qualifiers (prefix before the noun)
export const qualifiers = ['Нагорье:', 'Низина:', 'Побережье:', 'Река:', 'Север:', 'Юг:', 'Восток:', 'Запад:', 'Холмы:', 'Болота:', 'Долина:', 'Старый город:'];
// Fallback goods name (English: `Trade goods ${qualifier}`): `${qualifier} товары` (Russian adjectives would have to agree with 120 nouns of different gender and number, so the qualifier is a label ending in a colon: "Нагорье: репа"; the fallback goods name is "Нагорье: товары")

export const categoryNouns = [
  'репа|капуста|лук|лук-порей|морковь|свёкла',
  'пшеница|ячмень|овёс|рожь|просо|полба',
  'яблоки|груши|сливы|вишня|инжир|айва',
  'сельдь|треска|угри|форель|лосось|копчёная рыба',
  'баранина|свинина|говядина|колбасы|солёная ветчина|оленина',
  'сыр|масло|сливки|творог|козий сыр|яйца',
  'инструменты|топоры|серпы|молотки|пилы|плуги',
  'дубовый брус|сосновые доски|ясеневые жерди|балки|дранка|древесный уголь',
  'гранит|сланец|известняк|мрамор|кирпич|плитняк',
  'железная руда|медная руда|оловянная руда|свинцовая руда|серебряная руда|каменный уголь',
  'железные слитки|медные слитки|оловянные слитки|бронзовые прутья|стальные прутья|свинцовые листы',
  'лён|шерстяная ткань|парусина|войлок|пряжа|хлопок',
  'шкуры|кожа|сёдла|сапоги|ремни|меха',
  'керамика|кувшины|миски|черепица|стеклянная посуда|лампы',
  'эль|сидр|медовуха|вино|грушевый сидр|крепкие напитки',
  'перец|шафран|гвоздика|соль|горчица|травы',
  'марена|вайда|индиго|охра|красочные брикеты|чернила',
  'медная фурнитура|петли|гвозди|пряжки|замки|цепи',
  'стулья|сундуки|столы|бочки|телеги|повозки',
  'солнечный камень|янтарь|шёлк|жемчуг|духи|украшения',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `${qualifier} товары`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "ru", patterns, banned: new RegExp("хуй|пизд|бляд", "iu") };
