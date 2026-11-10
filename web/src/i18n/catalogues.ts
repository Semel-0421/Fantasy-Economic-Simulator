import { en } from './en';
import type { Catalogue, MessageId } from './types';
import type { LanguageCode } from './languages';
import { zh_Hans } from './zh-Hans';
import { hi } from './hi';
import { es } from './es';
import { ar } from './ar';
import { fr } from './fr';
import { bn } from './bn';
import { pt } from './pt';
import { ru } from './ru';
import { id } from './id';
import { ur } from './ur';
import { de } from './de';
import { ja } from './ja';
import { pcm } from './pcm';
import { mr } from './mr';
import { te } from './te';
import { tr } from './tr';
import { ha } from './ha';
import { ta } from './ta';
import { vi } from './vi';
import { ko } from './ko';

export const catalogues: Record<LanguageCode, Catalogue> = { en, "zh-Hans": zh_Hans, "hi": hi, "es": es, "ar": ar, "fr": fr, "bn": bn, "pt": pt, "ru": ru, "id": id, "ur": ur, "de": de, "ja": ja, "pcm": pcm, "mr": mr, "te": te, "tr": tr, "ha": ha, "ta": ta, "vi": vi, "ko": ko };
export const COMPLETE_LANGUAGES: readonly LanguageCode[] = ["ko","es","pt","fr","de","tr","id","vi","ha","pcm","ru","ja","zh-Hans","hi","mr","bn","te","ta","ar","ur"];

const parameters = (text: string): string => [...new Set([...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]))].sort().join(',');

export function catalogueCoverage(catalogue: Catalogue): { missing: MessageId[]; parameterMismatches: MessageId[] } {
  const ids = Object.keys(en) as MessageId[];
  return {
    missing: ids.filter(id => catalogue[id] === undefined),
    parameterMismatches: ids.filter(id => catalogue[id] !== undefined && parameters(catalogue[id]!) !== parameters(en[id])),
  };
}
