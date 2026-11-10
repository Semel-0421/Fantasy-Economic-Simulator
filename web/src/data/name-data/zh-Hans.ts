import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: zh-Hans name tables, same order and length as web/src/data/names.ts.
// Places are transliterations of the English fantasy names; the syllable tables map one to one.
export const curatedPlaces: Record<string, string> = {
  k0: '奥尔德米尔', k1: '凯斯林', k2: '索尔文', k3: '塔尔加德', k4: '恩伯韦尔',
  k0n0: '瓦恩', k0n1: '哈尔韦什', k0n2: '科尔威斯', k0n3: '布兰诺克', k0n4: '埃尔斯米尔',
  k1n0: '德拉维克', k1n1: '塞拉维尔', k1n2: '莫罗芬', k1n3: '伊斯特雷尔', k1n4: '奎尔马克',
  k2n0: '阿什卡尔', k2n1: '塔马里斯', k2n2: '翁德雷亚', k2n3: '泽弗兰', k2n4: '卡尔沃斯',
  k3n0: '格里姆霍尔特', k3n1: '诺维克', k3n2: '斯凯尔德', k3n3: '罗斯马尔', k3n4: '伊森瓦尔德',
  k4n0: '皮兰', k4n1: '卢梅斯', k4n2: '辛德雷尔', k4n3: '瓦罗穆尔', k4n4: '索拉斯',
  k0n0r0: '阿什福德谷', k0n0r1: '格雷芬', k0n0r0c0: '阿什福德', k0n0r0c1: '索恩霍尔德', k0n0r0c2: '弗恩利',
  k0n0r0c3: '奥克赫斯特', k0n0r0c4: '索尔特马什', k0n0r1c4: '雷文斯盖特', k0n0r0c0v1: '米尔布鲁克',
};
export const curatedGoods: Record<string, string> = { g1_0: '金色小麦', g6_0: '锻造工具', g17_5: '铜饰件', g19_0: '日耀石' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
export const starts = '阿尔 贝尔 科尔 达尔 埃尔 法尔 加尔 哈尔 伊尔 凯尔 洛尔 梅尔 诺尔 奥尔 佩尔 韦尔'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
export const middles = '阿 巴 德 恩 法 加 哈 伊 拉 梅 纳 奥 罗 塞 塔 维'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
export const ends = '班 登 芬 加尔 莱恩 梅尔 蒙 纳尔 伦 里斯 萨尔 滕 托尔 瓦尔 文 温'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
export const landforms = ['谷', '沼泽', '丘陵', '河岸', '荒原', '洼地', '边境'];
// qualifiers (prefix before the noun)
export const qualifiers = ['高地的', '低地的', '沿海的', '河畔的', '北部的', '南部的', '东部的', '西部的', '丘陵的', '沼泽的', '山谷的', '老城的'];
// Fallback goods name (English: `Trade goods ${qualifier}`): `${qualifier}货物`

export const categoryNouns = [
  '萝卜|卷心菜|洋葱|韭葱|胡萝卜|甜菜',
  '小麦|大麦|燕麦|黑麦|小米|斯佩尔特小麦',
  '苹果|梨|李子|樱桃|无花果|榅桲',
  '鲱鱼|鳕鱼|鳗鱼|鳟鱼|三文鱼|熏鱼',
  '羊肉|猪肉|牛肉|香肠|咸火腿|鹿肉',
  '奶酪|黄油|奶油|凝乳|山羊奶酪|鸡蛋',
  '工具|斧头|镰刀|锤子|锯子|犁',
  '橡木|松木板|梣木杆|横梁|木瓦|木炭',
  '花岗岩|板岩|石灰岩|大理石|砖块|石板',
  '铁矿石|铜矿石|锡矿石|铅矿石|银矿石|煤',
  '铁条|铜锭|锡锭|青铜条|钢棒|铅板',
  '亚麻布|毛料|帆布|毡|纱线|棉布',
  '生皮|皮革|马鞍|靴子|腰带|毛皮',
  '陶器|水罐|碗|瓦片|玻璃器皿|油灯',
  '麦酒|苹果酒|蜂蜜酒|葡萄酒|梨酒|烈酒',
  '胡椒|藏红花|丁香|盐|芥末|草药',
  '茜草|菘蓝|靛蓝|赭石|染料饼|墨水',
  '铜配件|合页|钉子|带扣|门锁|锁链',
  '椅子|箱子|桌子|木桶|手推车|货车',
  '太阳石|琥珀|丝绸|珍珠|香水|珠宝',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier}${noun}`, fallback: qualifier => `${qualifier}货物`, place: (name,landform) => `${name}${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "zh-Hans", patterns, banned: new RegExp("操你|傻逼", "iu") };
