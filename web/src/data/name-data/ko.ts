import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 2/4 (Claude): Korean name tables, same order and length as web/src/data/names.ts.
// Places are transliterations of the English fantasy names; syllables map one-to-one so a generated name is
// the same name in Korean. Goods and generic words are translated.
const curatedPlaces: Record<string, string> = {
  k0: '알더미어', k1: '케스린', k2: '솔베인', k3: '탈가드', k4: '엠버베일',
  k0n0: '반', k0n1: '할베시', k0n2: '코르위스', k0n3: '브래녹', k0n4: '엘스미어',
  k1n0: '드라빅', k1n1: '세라벨', k1n2: '모로펜', k1n3: '이스트렐', k1n4: '퀠마크',
  k2n0: '아시카르', k2n1: '타마리스', k2n2: '온드레아', k2n3: '제프란', k2n4: '칼보스',
  k3n0: '그림홀트', k3n1: '노르빅', k3n2: '스켈데', k3n3: '흐로스마르', k3n4: '이젠발트',
  k4n0: '피란', k4n1: '루메스', k4n2: '신드렐', k4n3: '배로무어', k4n4: '솔러스',
  k0n0r0: '애시퍼드 계곡', k0n0r1: '그레이펜',
  k0n0r0c0: '애시퍼드', k0n0r0c1: '손홀드', k0n0r0c2: '펀리', k0n0r0c3: '오크허스트', k0n0r0c4: '솔트마시',
  k0n0r1c4: '레이븐스게이트', k0n0r0c0v1: '밀브룩',
};
const curatedGoods: Record<string, string> = { g1_0: '황금 밀', g6_0: '대장간 도구', g17_5: '구리 장식', g19_0: '태양석' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
const starts = '알 벨 코르 달 엘 팔 갈 할 일 켈 로르 멜 노르 오르 펠 벨'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
const middles = '아 바 데 엔 파 가 하 이 라 메 나 오 라 세 타 베'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
const ends = '반 덴 펜 가르 렌 메르 몬 나르 렌 리스 사르 텐 토르 발 벤 윈'.split(' ');
// Note: 'bel'/'vel' -> 벨, 'ra'/'la' -> 라, 'len'/'ren' -> 렌 collide in Hangul. The generator must check
// uniqueness on the Korean string and probe to the next index on a clash, exactly as for English.

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
const landforms = ['계곡', '습지', '구릉', '강변', '황야', '분지', '변경'];
// qualifiers (prefix before the noun)
const qualifiers = ['고지대', '저지대', '해안', '강가', '북부', '남부', '동부', '서부', '언덕', '늪지', '골짜기', '옛 마을'];
// Fallback goods name: `Trade goods ${qualifier}` -> `${qualifier} 교역품`

const categoryNouns = [
  '순무|양배추|양파|리크|당근|비트', '밀|보리|귀리|호밀|기장|스펠트밀',
  '사과|배|자두|체리|무화과|모과', '청어|대구|장어|송어|연어|훈제 생선',
  '양고기|돼지고기|소고기|소시지|소금 햄|사슴고기', '치즈|버터|크림|커드|염소 치즈|달걀',
  '연장|도끼|낫|망치|톱|쟁기', '참나무 목재|소나무 판자|물푸레 장대|들보|지붕널|숯',
  '화강암|점판암|석회암|대리석|벽돌|판석', '철광석|구리 광석|주석 광석|납 광석|은광석|석탄',
  '철괴|구리 주괴|주석 주괴|청동 막대|강철 봉|납판', '아마포|모직물|범포|펠트|실|면포',
  '생가죽|무두질 가죽|안장|장화|허리띠|모피', '도기|물주전자|사발|타일|유리그릇|등잔',
  '에일|사과주|벌꿀술|포도주|배술|증류주', '후추|사프란|정향|소금|겨자|약초',
  '꼭두서니|대청|쪽빛 염료|황토|염료 덩어리|잉크', '구리 장식|경첩|못|버클|자물쇠|사슬',
  '의자|궤짝|탁자|술통|손수레|짐마차', '태양석|호박|비단|진주|향수|장신구',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `${qualifier} 교역품`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "ko", patterns, banned: new RegExp("씨발|병신", "iu") };
