import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: vi name tables. Latin script: the English fantasy place names are kept, so there are
// no curatedPlaces/starts/middles/ends. Same order and length as web/src/data/names.ts.
const curatedGoods: Record<string, string> = { g1_0: 'Lúa mì vàng', g6_0: 'Dụng cụ rèn', g17_5: 'Phụ kiện đồng', g19_0: 'Đá mặt trời' };

// landforms (7): a prefix before the place name
const landforms = ['Thung lũng', 'Đầm lầy', 'Đồi', 'Vùng', 'Đồng hoang', 'Trũng', 'Biên địa'];
// qualifiers (12): placed after the noun
const qualifiers = ['vùng cao', 'vùng thấp', 'ven biển', 'ven sông', 'phương Bắc', 'phương Nam', 'phương Đông', 'phương Tây', 'đồi', 'đầm lầy', 'thung lũng', 'phố cổ'];
// Fallback goods name pattern: `Hàng hóa ${qualifier}`  (the English source is `Trade goods ${qualifier}`)

const categoryNouns = [
  'củ cải|cải bắp|hành tây|tỏi tây|cà rốt|củ dền',
  'lúa mì|đại mạch|yến mạch|lúa mạch đen|kê|lúa mì spelt',
  'táo|lê|mận|anh đào|sung|mộc qua',
  'cá trích|cá tuyết|lươn|cá hồi suối|cá hồi|cá hun khói',
  'thịt cừu|thịt lợn|thịt bò|xúc xích|giăm bông muối|thịt nai',
  'phô mai|bơ|kem|sữa đông|phô mai dê|trứng',
  'dụng cụ|rìu|liềm|búa|cưa|cày',
  'gỗ sồi|ván thông|cột tần bì|xà gỗ|ngói gỗ|than củi',
  'đá granit|đá phiến|đá vôi|đá cẩm thạch|gạch|đá lát',
  'quặng sắt|quặng đồng|quặng thiếc|quặng chì|quặng bạc|than đá',
  'thanh sắt|thỏi đồng|thỏi thiếc|thanh đồng thanh|que thép|tấm chì',
  'vải lanh|vải len|vải bạt|nỉ|sợi|vải bông',
  'da sống|da thuộc|yên ngựa|ủng|thắt lưng|lông thú',
  'đồ gốm|bình|bát|gạch men|đồ thủy tinh|đèn',
  'bia|rượu táo|rượu mật ong|rượu vang|rượu lê|rượu mạnh',
  'tiêu|nghệ tây|đinh hương|muối|mù tạt|thảo mộc',
  'thiên thảo|chàm lá|chàm|đất son|bánh thuốc nhuộm|mực',
  'phụ kiện đồng|bản lề|đinh|khóa cài|ổ khóa|xích',
  'ghế|rương|bàn|thùng gỗ|xe đẩy|xe ngựa',
  'đá mặt trời|hổ phách|lụa|ngọc trai|nước hoa|đồ trang sức',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${noun} ${qualifier}`, fallback: qualifier => `Hàng hóa ${qualifier}`, place: (name,landform) => `${landform} ${name}` };
export const names: NameData = { ...englishNames, curatedGoods, landforms, qualifiers, categoryNouns, curatedPlaces: { ...englishNames.curatedPlaces, k0n0r0: patterns.place("Ashford", landforms[0]!) }, code: "vi", patterns, banned: new RegExp("\\b(?:địt|lồn)\\b", "iu") };
