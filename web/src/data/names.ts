import { activeNames } from '../i18n';
import { createNames } from './name-generator';

export const { placeName, goodName } = createNames(activeNames);

/** Replace complete key tokens only, preserving surrounding punctuation and ordinary words. */
export function displayText(text: string): string {
  return text.replace(/\b(k\d+(?:n\d+)?(?:r\d+)?(?:c\d+)?(?:v\d+)?|g\d+_\d+)(?:@([a-z]+))?\b/g, (token: string, key: string, grade: string | undefined) => {
    const name = key.startsWith('g') ? goodName(key) : placeName(key);
    return grade ? `${name} (${grade})` : name || token;
  });
}
