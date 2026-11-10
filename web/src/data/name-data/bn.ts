import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: bn name tables, same order and length as web/src/data/names.ts. Places are transliterated by sound.
const curatedPlaces: Record<string, string> = {
  k0: 'অল্ডারমিয়ার', k1: 'কেসরিন', k2: 'সলভেন', k3: 'থালগার্ড', k4: 'এমবারভেল',
  k0n0: 'ভার্ন', k0n1: 'হালভেশ', k0n2: 'কর্উইথ', k0n3: 'ব্র্যানক', k0n4: 'এলসমিয়ার',
  k1n0: 'ড্রাভিক', k1n1: 'সেরাভেল', k1n2: 'মোরোফেন', k1n3: 'ইস্ট্রেল', k1n4: 'কুয়েলমার্ক',
  k2n0: 'আশকার', k2n1: 'তামারিস', k2n2: 'অন্ড্রিয়া', k2n3: 'জেফ্রান', k2n4: 'ক্যালভোস',
  k3n0: 'গ্রিমহোল্ট', k3n1: 'নরভিক', k3n2: 'স্কেল্ডে', k3n3: 'রথমার', k3n4: 'আইজেনওয়াল্ড',
  k4n0: 'পিরান', k4n1: 'লুমেথ', k4n2: 'সিন্ড্রেল', k4n3: 'ভ্যারোমুর', k4n4: 'সোলেস',
  k0n0r0: 'অ্যাশফোর্ড উপত্যকা', k0n0r1: 'গ্রেফেন', k0n0r0c0: 'অ্যাশফোর্ড', k0n0r0c1: 'থর্নহোল্ড', k0n0r0c2: 'ফার্নলি',
  k0n0r0c3: 'ওকহার্স্ট', k0n0r0c4: 'সল্টমার্শ', k0n0r1c4: 'রেভেনসগেট', k0n0r0c0v1: 'মিলব্রুক',
};
const curatedGoods: Record<string, string> = { g1_0: 'সোনালি গম', g6_0: 'কামারের যন্ত্র', g17_5: 'তামার সাজ', g19_0: 'সূর্যমণি' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
const starts = 'অল বেল কর ডাল এল ফাল গাল হাল ইল কেল লর মেল নর অর পেল ভেল'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
const middles = 'অ বা দে এন ফা গা হা ই লা মে না ও রা সে তা ভে'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
const ends = 'বান ডেন ফেন গার লেন মের মন নার রেন রিস সার টেন টর ভাল ভেন উইন'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
const landforms = ['উপত্যকা', 'জলাভূমি', 'টিলা', 'বিস্তার', 'প্রান্তর', 'খাদ', 'সীমান্ত'];
// qualifiers (prefix before the noun)
const qualifiers = ['উচ্চভূমির', 'নিম্নভূমির', 'উপকূলীয়', 'নদীতীরের', 'উত্তরের', 'দক্ষিণের', 'পূর্বের', 'পশ্চিমের', 'পাহাড়ি', 'জলাভূমির', 'উপত্যকার', 'পুরনো শহরের'];
// Fallback goods name: `Trade goods ${qualifier}` -> `{qualifier} বাণিজ্যপণ্য`

const categoryNouns = [
  'শালগম|বাঁধাকপি|পেঁয়াজ|লিক|গাজর|বিট',
  'গম|যব|জই|রাই|বাজরা|স্পেল্ট',
  'আপেল|নাশপাতি|আলুবোখারা|চেরি|ডুমুর|কুইন্স',
  'ইলিশ|কড মাছ|বাইন মাছ|ট্রাউট|স্যামন|ধোঁয়া-দেওয়া মাছ',
  'খাসির মাংস|শূকরের মাংস|গরুর মাংস|সসেজ|নোনা হ্যাম|হরিণের মাংস',
  'পনির|মাখন|ক্রিম|ছানা|ছাগলের পনির|ডিম',
  'যন্ত্রপাতি|কুড়াল|কাস্তে|হাতুড়ি|করাত|লাঙল',
  'ওক কাঠ|পাইন তক্তা|অ্যাশ কাঠের খুঁটি|কড়িকাঠ|ছাদের টালি|কাঠকয়লা',
  'গ্র্যানাইট|স্লেট|চুনাপাথর|মার্বেল|ইট|পাথরের ফলক',
  'লৌহ আকরিক|তামা আকরিক|টিন আকরিক|সীসা আকরিক|রুপা আকরিক|পাথুরে কয়লা',
  'লোহার পাত|তামার পিণ্ড|টিনের পিণ্ড|ব্রোঞ্জের দণ্ড|ইস্পাতের দণ্ড|সীসার চাদর',
  'লিনেন|পশমি কাপড়|ক্যানভাস|ফেল্ট|সুতো|সুতি কাপড়',
  'কাঁচা চামড়া|পাকা চামড়া|জিন|বুট|কোমরবন্ধ|পশম-চামড়া',
  'মাটির বাসন|কলসি|বাটি|টাইল|কাচের বাসন|প্রদীপ',
  'এল|সাইডার|মিড|মদ|পেরি|চোলাই মদ',
  'গোলমরিচ|জাফরান|লবঙ্গ|নুন|সরষে|ভেষজ',
  'মঞ্জিষ্ঠা|ওয়োড|নীল রং|গেরিমাটি|রঙের চাকতি|কালি',
  'তামার জোড়|কবজা|পেরেক|বকলস|তালা|শিকল',
  'চেয়ার|সিন্দুক|টেবিল|পিপে|ঠেলাগাড়ি|মালগাড়ি',
  'সূর্যকান্তমণি|অম্বর|রেশম|মুক্তো|আতর|গয়না',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `${qualifier} বাণিজ্যপণ্য`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "bn", patterns, banned: new RegExp("চোদা|মাগী", "iu") };
