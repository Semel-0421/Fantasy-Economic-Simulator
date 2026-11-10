import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: hi name tables, same order and length as web/src/data/names.ts. Places are transliterated by sound.
const curatedPlaces: Record<string, string> = {
  k0: 'ऐल्डरमीयर', k1: 'केसरिन', k2: 'सोलवेन', k3: 'थालगार्ड', k4: 'एम्बरवेल',
  k0n0: 'वार्न', k0n1: 'हल्वेश', k0n2: 'कॉर्विथ', k0n3: 'ब्रैनॉक', k0n4: 'एल्समीयर',
  k1n0: 'द्रविक', k1n1: 'सेरावेल', k1n2: 'मोरोफेन', k1n3: 'इस्त्रेल', k1n4: 'क्वेलमार्क',
  k2n0: 'अश्कर', k2n1: 'तामारिस', k2n2: 'ओन्ड्रिया', k2n3: 'ज़ेफ़्रान', k2n4: 'कैल्वोस',
  k3n0: 'ग्रिमहोल्ट', k3n1: 'नॉर्विक', k3n2: 'स्केल्डे', k3n3: 'रोथमार', k3n4: 'आइज़नवाल्ड',
  k4n0: 'पिरान', k4n1: 'लुमेथ', k4n2: 'सिंड्रेल', k4n3: 'वैरोमूर', k4n4: 'सोलेस',
  k0n0r0: 'ऐशफ़ोर्ड घाटी', k0n0r1: 'ग्रेफ़ेन', k0n0r0c0: 'ऐशफ़ोर्ड', k0n0r0c1: 'थॉर्नहोल्ड', k0n0r0c2: 'फ़र्नली',
  k0n0r0c3: 'ओकहर्स्ट', k0n0r0c4: 'सॉल्टमार्श', k0n0r1c4: 'रेवन्सगेट', k0n0r0c0v1: 'मिलब्रुक',
};
const curatedGoods: Record<string, string> = { g1_0: 'सुनहरा गेहूँ', g6_0: 'लोहार के औज़ार', g17_5: 'तांबे का साज़', g19_0: 'सूर्यमणि' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
const starts = 'अल बेल कोर दल एल फल गल हल इल केल लोर मेल नोर ओर पेल वेल'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
const middles = 'अ बा दे एन फा गा हा इ ला मे ना ओ रा से ता वे'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
const ends = 'बन डेन फेन गर लेन मेर मोन नर रेन रिस सर टेन टोर वल वेन विन'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
const landforms = ['घाटी', 'दलदल', 'टीले', 'विस्तार', 'बंजर भूमि', 'खोह', 'सीमांत'];
// qualifiers (prefix before the noun)
const qualifiers = ['ऊँचे इलाके के', 'निचले इलाके के', 'तटीय', 'नदी किनारे के', 'उत्तरी', 'दक्षिणी', 'पूर्वी', 'पश्चिमी', 'पहाड़ी', 'दलदली', 'घाटी के', 'पुराने शहर के'];
// Fallback goods name: `Trade goods ${qualifier}` -> `{qualifier} व्यापारिक माल`

const categoryNouns = [
  'शलजम|पत्ता गोभी|प्याज़|लीक|गाजर|चुकंदर',
  'गेहूँ|जौ|जई|राई|बाजरा|स्पेल्ट',
  'सेब|नाशपाती|आलूबुखारा|चेरी|अंजीर|श्रीफल',
  'हिल्सा|कॉड मछली|ईल|ट्राउट|सैल्मन|स्मोक्ड मछली',
  'मटन|सूअर का मांस|गोमांस|सॉसेज|नमकीन हैम|हिरन का मांस',
  'पनीर|मक्खन|मलाई|दही के थक्के|बकरी का पनीर|अंडे',
  'औज़ार|कुल्हाड़ी|दरांती|हथौड़ा|आरी|हल',
  'बलूत की लकड़ी|चीड़ के तख़्ते|ऐश के डंडे|कड़ियाँ|छत की पट्टियाँ|लकड़ी का कोयला',
  'ग्रेनाइट|स्लेट|चूना पत्थर|संगमरमर|ईंटें|पटिया',
  'लौह अयस्क|तांबा अयस्क|टिन अयस्क|सीसा अयस्क|चांदी अयस्क|पत्थर का कोयला',
  'लोहे की छड़ें|तांबे की सिल्लियाँ|टिन की सिल्लियाँ|कांसे की छड़ें|इस्पात की छड़ें|सीसे की चादरें',
  'लिनेन|ऊनी कपड़ा|तिरपाल|फ़ेल्ट|धागा|सूती कपड़ा',
  'कच्चे चमड़े|पक्का चमड़ा|ज़ीन|जूते|पट्टे|समूर',
  'मिट्टी के बर्तन|सुराही|कटोरे|खपरैल|काँच के बर्तन|दीये',
  'एल|साइडर|मीड|शराब|पेरी|आसवित मदिरा',
  'काली मिर्च|केसर|लौंग|नमक|सरसों|जड़ी-बूटियाँ',
  'मजीठ|वोड|नील|गेरू|रंग की टिकिया|स्याही',
  'तांबे के फिटिंग|कब्ज़े|कीलें|बकल|ताले|ज़ंजीरें',
  'कुर्सियाँ|संदूक|मेज़ें|पीपे|ठेले|बैलगाड़ियाँ',
  'सूर्यकांत|कहरुबा|रेशम|मोती|इत्र|गहने',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `${qualifier} व्यापारिक माल`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "hi", patterns, banned: new RegExp("चूतिया|भोसड़ी", "iu") };
