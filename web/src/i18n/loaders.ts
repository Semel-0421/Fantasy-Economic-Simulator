import type { Catalogue } from './types';
import type { LanguageCode } from './languages';
import type { NameData } from '../data/name-types';
export interface LanguageBundle { catalogue: Catalogue; scenario: Record<string,string>; names: NameData }
const loaders = {
  "ko": () => import("./bundles/language-ko").then(module => module.default),
  "es": () => import("./bundles/language-es").then(module => module.default),
  "pt": () => import("./bundles/language-pt").then(module => module.default),
  "fr": () => import("./bundles/language-fr").then(module => module.default),
  "de": () => import("./bundles/language-de").then(module => module.default),
  "tr": () => import("./bundles/language-tr").then(module => module.default),
  "id": () => import("./bundles/language-id").then(module => module.default),
  "vi": () => import("./bundles/language-vi").then(module => module.default),
  "ha": () => import("./bundles/language-ha").then(module => module.default),
  "pcm": () => import("./bundles/language-pcm").then(module => module.default),
  "ru": () => import("./bundles/language-ru").then(module => module.default),
  "ja": () => import("./bundles/language-ja").then(module => module.default),
  "zh-Hans": () => import("./bundles/language-zh-Hans").then(module => module.default),
  "hi": () => import("./bundles/language-hi").then(module => module.default),
  "mr": () => import("./bundles/language-mr").then(module => module.default),
  "bn": () => import("./bundles/language-bn").then(module => module.default),
  "te": () => import("./bundles/language-te").then(module => module.default),
  "ta": () => import("./bundles/language-ta").then(module => module.default),
  "ar": () => import("./bundles/language-ar").then(module => module.default),
  "ur": () => import("./bundles/language-ur").then(module => module.default),
};
export const loadLanguage = (code: Exclude<LanguageCode,'en'>): Promise<LanguageBundle> => loaders[code]();
