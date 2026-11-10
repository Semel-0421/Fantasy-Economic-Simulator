import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: ja name tables, same order and length as web/src/data/names.ts.
// Places are transliterations of the English fantasy names; the syllable tables map one to one.
const curatedPlaces: Record<string, string> = {
  k0: 'アルダーミア', k1: 'ケスリン', k2: 'ソルヴェイン', k3: 'タールガード', k4: 'エンバーヴェイル',
  k0n0: 'ヴァーン', k0n1: 'ハルヴェッシュ', k0n2: 'コーウィス', k0n3: 'ブラノック', k0n4: 'エルズミア',
  k1n0: 'ドラヴィク', k1n1: 'セラヴェル', k1n2: 'モローフェン', k1n3: 'イストレル', k1n4: 'クウェルマーク',
  k2n0: 'アシュカル', k2n1: 'タマリス', k2n2: 'オンドレア', k2n3: 'ゼフラン', k2n4: 'カルヴォス',
  k3n0: 'グリムホルト', k3n1: 'ノルヴィク', k3n2: 'スケルデ', k3n3: 'ロスマール', k3n4: 'イーゼンヴァルト',
  k4n0: 'ピラン', k4n1: 'ルメス', k4n2: 'シンドレル', k4n3: 'ヴァロウムーア', k4n4: 'ソレイス',
  k0n0r0: 'アッシュフォード谷', k0n0r1: 'グレイフェン', k0n0r0c0: 'アッシュフォード', k0n0r0c1: 'ソーンホールド', k0n0r0c2: 'ファーンリー',
  k0n0r0c3: 'オークハースト', k0n0r0c4: 'ソルトマーシュ', k0n0r1c4: 'レイヴンズゲート', k0n0r0c0v1: 'ミルブルック',
};
const curatedGoods: Record<string, string> = { g1_0: '黄金の小麦', g6_0: '鍛冶道具', g17_5: '銅の飾り金具', g19_0: '太陽の石' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
const starts = 'アル ベル コル ダル エル ファル ガル ハル イル ケル ロル メル ノル オル ペル ヴェル'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
const middles = 'ア バ デ エン ファ ガ ハ イ ラ メ ナ オ ラー セ タ ヴェ'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
const ends = 'バン デン フェン ガル レン メル モン ナル レーン リス サル テン トル ヴァル ヴェン ウィン'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
const landforms = ['谷', '沼沢', '丘陵', '河畔', '荒野', '窪地', '辺境'];
// qualifiers (prefix before the noun)
const qualifiers = ['高地の', '低地の', '沿岸の', '川沿いの', '北部の', '南部の', '東部の', '西部の', '丘陵の', '湿地の', '谷間の', '旧市街の'];
// Fallback goods name (English: `Trade goods ${qualifier}`): `${qualifier}交易品`

const categoryNouns = [
  'かぶ|キャベツ|玉ねぎ|ポロねぎ|にんじん|ビート',
  '小麦|大麦|オーツ麦|ライ麦|キビ|スペルト小麦',
  'りんご|梨|すもも|さくらんぼ|いちじく|マルメロ',
  'ニシン|タラ|ウナギ|マス|サケ|燻製魚',
  '羊肉|豚肉|牛肉|ソーセージ|塩漬けハム|鹿肉',
  'チーズ|バター|クリーム|カード|ヤギのチーズ|卵',
  '道具|斧|鎌|金槌|のこぎり|すき',
  'オーク材|松の板|トネリコの棒|梁|屋根板|木炭',
  '花崗岩|スレート|石灰岩|大理石|レンガ|敷石',
  '鉄鉱石|銅鉱石|錫鉱石|鉛鉱石|銀鉱石|石炭',
  '鉄の棒|銅のインゴット|錫のインゴット|青銅の棒|鋼の棒|鉛板',
  '亜麻布|ウール生地|帆布|フェルト|毛糸|綿布',
  '生皮|革|鞍|ブーツ|ベルト|毛皮',
  '陶器|水差し|鉢|タイル|ガラス器|ランプ',
  'エール|シードル|蜂蜜酒|ワイン|梨酒|蒸留酒',
  'コショウ|サフラン|クローブ|塩|マスタード|薬草',
  'アカネ|大青|インディゴ|黄土|染料の塊|インク',
  '銅の金具|蝶番|釘|バックル|錠前|鎖',
  '椅子|櫃|テーブル|樽|荷車|馬車',
  '太陽石|琥珀|絹|真珠|香水|宝飾品',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier}${noun}`, fallback: qualifier => `${qualifier}交易品`, place: (name,landform) => `${name}${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "ja", patterns, banned: new RegExp("くそ|ちんこ|まんこ", "iu") };
