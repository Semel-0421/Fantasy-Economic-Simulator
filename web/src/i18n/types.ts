import type { en } from './en';

export type MessageId = keyof typeof en;
// Unfinished catalogues deliberately omit IDs to use the English fallback.
export type Catalogue = Partial<Record<MessageId, string>>;
export type Params = Record<string, string | number>;
