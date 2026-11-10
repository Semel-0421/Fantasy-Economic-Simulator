import type { NameData } from '../name-types';
import { englishNames } from './en';
// Plan 7D Task 3: mr name tables, same order and length as web/src/data/names.ts. Places are transliterated by sound.
const curatedPlaces: Record<string, string> = {
  k0: 'ऍल्डरमीयर', k1: 'केसरिन', k2: 'सोलवेन', k3: 'थालगार्ड', k4: 'एम्बरवेल',
  k0n0: 'वार्न', k0n1: 'हल्वेश', k0n2: 'कॉर्विथ', k0n3: 'ब्रॅनॉक', k0n4: 'एल्समीयर',
  k1n0: 'द्रविक', k1n1: 'सेरावेल', k1n2: 'मोरोफेन', k1n3: 'इस्त्रेल', k1n4: 'क्वेलमार्क',
  k2n0: 'अश्कर', k2n1: 'तामारिस', k2n2: 'ऑन्ड्रिया', k2n3: 'झेफ्रान', k2n4: 'कॅल्वोस',
  k3n0: 'ग्रिमहोल्ट', k3n1: 'नॉर्विक', k3n2: 'स्केल्डे', k3n3: 'रोथमार', k3n4: 'आयझनवाल्ड',
  k4n0: 'पिरान', k4n1: 'लुमेथ', k4n2: 'सिंड्रेल', k4n3: 'वॅरोमूर', k4n4: 'सोलेस',
  k0n0r0: 'ऍशफर्ड दरी', k0n0r1: 'ग्रेफेन', k0n0r0c0: 'ऍशफर्ड', k0n0r0c1: 'थॉर्नहोल्ड', k0n0r0c2: 'फर्नली',
  k0n0r0c3: 'ओकहर्स्ट', k0n0r0c4: 'सॉल्टमार्श', k0n0r1c4: 'रेव्हन्सगेट', k0n0r0c0v1: 'मिलब्रुक',
};
const curatedGoods: Record<string, string> = { g1_0: 'सोनेरी गहू', g6_0: 'लोहाराची अवजारे', g17_5: 'तांब्याचे साज', g19_0: 'सूर्यमणी' };

// starts: 'al bel cor dal el fal gal hal il kel lor mel nor or pel vel'
const starts = 'अल बेल कोर दल एल फल गल हल इल केल लोर मेल नोर ओर पेल वेल'.split(' ');
// middles: 'a ba de en fa ga ha i la me na o ra se ta ve'
const middles = 'अ बा दे एन फा गा हा इ ला मे ना ओ रा से ता वे'.split(' ');
// ends: 'ban den fen gar len mer mon nar ren ris sar ten tor val ven win'
const ends = 'बन डेन फेन गर लेन मेर मोन नर रेन रिस सर टेन टोर वल वेन विन'.split(' ');

// landforms: 'Vale', 'Fen', 'Downs', 'Reach', 'Moor', 'Hollow', 'March' (a suffix after the name)
const landforms = ['दरी', 'दलदल', 'टेकड्या', 'विस्तार', 'माळरान', 'खळगा', 'सीमाप्रदेश'];
// qualifiers (prefix before the noun)
const qualifiers = ['उंच प्रदेशातील', 'सखल प्रदेशातील', 'किनारी', 'नदीकाठचे', 'उत्तरेकडील', 'दक्षिणेकडील', 'पूर्वेकडील', 'पश्चिमेकडील', 'डोंगरी', 'दलदलीचे', 'दरीतील', 'जुन्या गावातील'];
// Fallback goods name: `Trade goods ${qualifier}` -> `{qualifier} व्यापारी माल`

const categoryNouns = [
  'सलगम|कोबी|कांदा|लीक|गाजर|बीट',
  'गहू|जव|ओट|राय|बाजरी|स्पेल्ट',
  'सफरचंद|नासपती|आलूबुखार|चेरी|अंजीर|क्विन्स',
  'हिल्सा|कॉड मासा|ईल|ट्राउट|सॅल्मन|धूर दिलेला मासा',
  'मटण|डुकराचे मांस|गोमांस|सॉसेज|खारे हॅम|हरणाचे मांस',
  'चीज|लोणी|मलई|दह्याचे गोळे|शेळीचे चीज|अंडी',
  'अवजारे|कुऱ्हाडी|विळे|हातोडे|करवत|नांगर',
  'ओक लाकूड|पाइनच्या फळ्या|ऍशचे खांब|तुळया|छपराच्या पट्ट्या|लाकडी कोळसा',
  'ग्रॅनाइट|स्लेट|चुनखडी|संगमरवर|विटा|फरशा',
  'लोह खनिज|तांबे खनिज|कथील खनिज|शिसे खनिज|चांदी खनिज|दगडी कोळसा',
  'लोखंडी सळ्या|तांब्याचे ठोकळे|कथिलाचे ठोकळे|कांस्य सळ्या|पोलादी सळ्या|शिशाचे पत्रे',
  'लिनन|लोकरी कापड|तागाचे कापड|फेल्ट|सूत|सुती कापड',
  'कच्ची कातडी|कमावलेले चामडे|खोगीर|बूट|पट्टे|फर',
  'मातीची भांडी|माठ|वाट्या|कौले|काचसामान|दिवे',
  'एल|सायडर|मध-मद्य|वाइन|पेरी|अर्क-मद्य',
  'मिरी|केशर|लवंग|मीठ|मोहरी|औषधी वनस्पती',
  'मंजिष्ठ|वोड|नीळ|गेरू|रंगाच्या वड्या|शाई',
  'तांब्याची जोडणी|बिजागरे|खिळे|बकल|कुलपे|साखळ्या',
  'खुर्च्या|पेट्या|टेबले|पिंपे|हातगाड्या|बैलगाड्या',
  'सूर्यकांत|अंबर|रेशीम|मोती|अत्तर|दागिने',
].map((words) => words.split('|'));

const patterns: NameData["patterns"] = { goods: (qualifier,noun) => `${qualifier} ${noun}`, fallback: qualifier => `${qualifier} व्यापारी माल`, place: (name,landform) => `${name} ${landform}` };
export const names: NameData = { ...englishNames, curatedGoods, starts, middles, ends, landforms, qualifiers, categoryNouns, curatedPlaces: curatedPlaces, code: "mr", patterns, banned: new RegExp("चूतिया|भोसडी", "iu") };
